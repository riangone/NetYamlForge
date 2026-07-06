# Phase 4.2 — 内置 MCP Server 设计规格

> 前置条件：Phase 4.1（`/api/{project}/{entity}` REST API + Swagger）已完成。
> 本文档为可直接交给实现 Agent 的规格：目标 / 架构 / 改动文件清单 / 验收标准。

## 目标

把每个租户项目（`projects/{project}/project.yaml` 中定义的实体）的 CRUD、查询、自定义 Action
通过 MCP（Model Context Protocol）以 HTTP/SSE transport 暴露出来，使 AI 客户端（AiChatApp/Hyperion
等）可以协议级 list tools / call tool，而不必爬 HTML 或裸调 REST。

## 总体架构

```
MCP Client ──HTTP/SSE──> /mcp  ──>  McpServer (ModelContextProtocol SDK)
                                       │
                                       ▼
                          [McpServerToolType] 工具类（新增）
                                       │  复用：
                                       ▼
            EntityMetadataProvider / IDynamicCrudRepository /
            DynamicEntityCommandService / FormValidationService /
            IProjectActionRegistry / ProjectManager / ProjectScope
```

关键约束：现有这些服务都依赖请求级 `ProjectScope`（由 `ProjectMiddleware` 根据路由 `{project}`
段设置）。MCP 工具调用没有这样的路由段，因此**每个工具方法的第一个业务参数必须是 `project`
(string)**，工具实现内部在调用业务服务前，先通过注入的 `ProjectManager` + `ProjectScope`
（`scope.Set(projectInfo)`，二者均为同一 DI scope 内的 Scoped 服务）完成项目上下文绑定，
再委托给现有服务。

## 改动文件清单

### 1. NuGet 依赖（`NetYamlForge/NetYamlForge.csproj`）
- 新增 `ModelContextProtocol` 及 `ModelContextProtocol.AspNetCore`（官方 C# MCP SDK，取最新
  稳定/预览版本，先用 `dotnet add package` 探测可用版本号，不要凭空猜测版本字符串）。

### 2. 新建 `NetYamlForge/Services/Mcp/EntityToolService.cs`
一个 Scoped 服务，**提取并复用** `ApiEntityController`（`NetYamlForge/Controllers/ApiEntityController.cs`）
中的核心逻辑（`ValidateApiAccess`、`ToApiDto`、`ConvertValue`、列表/详情/创建/更新/删除/Action 调用），
签名形如：

```csharp
public sealed class EntityToolService
{
    // 构造函数注入：IDynamicCrudRepository, IEntityMetadataProvider,
    // DynamicEntityCommandService, DynamicEntityFormValidationService,
    // ProjectManager, ProjectScope, IProjectActionRegistry, ILogger<EntityToolService>

    public McpToolResult ListProjects();
    public McpToolResult ListEntities(string project);
    public McpToolResult GetEntityMeta(string project, string entity);
    public Task<McpToolResult> ListRecordsAsync(string project, string entity, string? search, string? sort, string? dir, int page, int pageSize);
    public Task<McpToolResult> GetRecordAsync(string project, string entity, string id);
    public Task<McpToolResult> CreateRecordAsync(string project, string entity, Dictionary<string, object?> data);
    public Task<McpToolResult> UpdateRecordAsync(string project, string entity, string id, Dictionary<string, object?> data);
    public Task<McpToolResult> DeleteRecordAsync(string project, string entity, string id);
    public Task<McpToolResult> InvokeActionAsync(string project, string entity, string id, string actionKey, Dictionary<string, object?>? inputs);
}
```

- 内部统一帮助方法 `BindProject(string projectName)`：调用 `ProjectManager.TryGet`，找不到则返回
  「项目不存在」错误；找到后 `ProjectScope.Set(info)`（注意 `ProjectScope.Set` 是 `internal`，
  与本类同在 `NetYamlForge` 主程序集内，可直接调用）。
- 复用 `ApiEntityController.ValidateApiAccess` 的判定逻辑（`meta.Api`: `disabled`/`readonly`/其他），
  对写操作（create/update/delete/action）要求非 `disabled` 且非 `readonly`，读操作只要求非 `disabled`。
- `ToApiDto`/`ConvertValue` 可以原样搬到 `EntityToolService` 作为 `private static` 方法（与
  `ApiEntityController` 中重复定义即可，不必强行共享类型，保持改动面小；如能简单提取到
  共享 static helper 类 `NetYamlForge/Services/DynamicEntity/EntityDtoMapper.cs` 并在两处复用更好，
  但不是必须）。

`McpToolResult` 可以直接用简单 DTO（`bool Ok`, `string? Error`, `object? Data`），工具方法返回值
最终序列化为 JSON 字符串或对象交给 MCP SDK（按 SDK 实际要求的返回类型调整，通常 MCP 工具方法
可以直接 `return` 一个可序列化对象或 `string`）。

### 3. 新建 `NetYamlForge/Services/Mcp/EntityMcpTools.cs`
用 MCP SDK 的工具属性标注（`[McpServerToolType]` 类 + 每个方法 `[McpServerTool]` +
`[Description("...")]`），方法体仅做参数转发到 `EntityToolService`（构造函数注入）。
工具命名建议（snake_case，符合 MCP 工具命名习惯）：

| 工具名 | 对应方法 | 说明 |
|---|---|---|
| `list_projects` | ListProjects | 列出所有租户项目 |
| `list_entities` | ListEntities(project) | 列出该项目所有暴露 API 的实体 |
| `get_entity_meta` | GetEntityMeta(project, entity) | 获取实体字段/表单元数据 |
| `list_entity_records` | ListRecordsAsync | 分页/搜索/排序查询记录 |
| `get_entity_record` | GetRecordAsync | 按 id 获取单条记录 |
| `create_entity_record` | CreateRecordAsync | 创建记录 |
| `update_entity_record` | UpdateRecordAsync | 更新记录（PATCH 语义，字段级） |
| `delete_entity_record` | DeleteRecordAsync | 删除记录 |
| `invoke_entity_action` | InvokeActionAsync | 执行自定义 Action |

每个工具方法都要写清楚 `[Description]`（中文或英文均可，但要说明参数含义与 `meta.Api`
权限限制），方便 AI 客户端理解工具用途。

### 4. `Program.cs` 注册与中间件管线
- `builder.Services.AddMcpServer().WithHttpTransport().WithTools<EntityMcpTools>();`
  （具体方法名以已安装 SDK 版本的实际 API 为准，先用 `dotnet add package` 装好后查看
  `obj/.../*.cs` 或官方示例确定准确调用方式）。
- 注册 `EntityToolService`、`EntityMcpTools` 为 Scoped。
- 在中间件管线中（`UseRouting()` 之后、认证之后）增加 `app.MapMcp("/mcp")`（路径可调整，
  但要与下方鉴权要求一致）。
- **鉴权**：`/mcp` 端点必须要求与 `ApiEntityController` 一致的认证方案
  （`Cookies,ApiToken`），即非匿名访问 MCP 工具。如 `MapMcp` 返回的端点构建器支持
  `.RequireAuthorization(...)`，按现有 `ApiToken` scheme 配置；否则在 MCP 工具内部通过
  `IHttpContextAccessor` 检查 `HttpContext.User.Identity?.IsAuthenticated`，未认证则所有工具
  返回错误。
- **重要**：`/mcp` 不带 `{project}` 路由段，因此 `ProjectMiddleware` 不会自动设置 `ProjectScope`
  ——这是设计预期（工具参数里显式传 `project`），不需要修改 `ProjectMiddleware`。但要确认
  `ProjectMiddleware` 在没有 `{project}` 路由参数、且没有 `ReturnUrl` 时的兜底逻辑不会因为
  `/mcp` 请求而抛异常或产生副作用（如有问题，给 `/mcp` 路径加白名单跳过该 middleware）。

### 5. 文档状态更新
- `docs/EVOLUTION_PLAN.md` 中 Phase 4.1 标记 ✅ 已完成（含本次 Swagger by-id 路径未注册的
  bug 修复），Phase 4.2 完成后标记 ✅ 并写明验收结果摘要。

## 测试要求（`NetYamlForge.Tests/Integration/McpServerIntegrationTests.cs`）

用现有 E2E 测试基础设施（`WebApplicationFactory`，参考
`NetYamlForge.Tests/Integration/YamlPipelineEndToEndTests.cs` / `ApiEntityIntegrationTests.cs`
中已有的测试库隔离方式）：

1. 用官方 MCP C# SDK 客户端（`ModelContextProtocol` 包里的 client，或直接用
   `HttpClient` + SSE 手工解析，取 SDK 已提供的最简方式）连接测试服务器 `/mcp`，
   附带与 `ApiEntityIntegrationTests` 相同的 Bearer token。
2. `ListToolsAsync()` 断言能看到 `list_entities` / `list_entity_records` /
   `create_entity_record` 等工具。
3. 调用 `list_entity_records`（project=示例项目, entity=已知实体）返回种子数据。
4. 调用 `create_entity_record` 创建一条记录，再用 `get_entity_record` 验证可读到。
5. 对 `meta.Api == "disabled"` 的实体调用任意工具应返回错误（不是抛异常）。
6. 未带 token 访问 `/mcp` 应被拒绝（401/403 或工具返回未认证错误，按最终鉴权实现二选一）。

## 验收标准

- `dotnet build`：0 警告 0 错误。
- `dotnet test`：全绿，含上述新增 MCP 集成测试。
- MCP 客户端可 `list tools` 并完成一次实体创建（对应 `EVOLUTION_PLAN.md` 4.2 验收标准）。
- 不破坏现有 `/api/{project}/{entity}` REST 端点与 Swagger 文档（Phase 4.1 行为不变）。
