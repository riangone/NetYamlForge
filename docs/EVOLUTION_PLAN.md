# NetYamlForge 演进计划

> 基线：2026-06-12，`nyf` 分支工作区（含大量未提交改动）。
> 用途：本文档的每个任务写成可直接交给 AI 代理实现的规格（目标 / 改动点 / 验收标准）。
> 总方向：短期补齐工程基线，中期走 API-first + MCP，让 NetYamlForge 成为「AI 可直接驱动的声明式业务后端」。

---

## Phase 1 — 工程卫生与 CI ✅（已完成，待提交）

| 任务 | 状态 |
|---|---|
| 运行时产物（`*.db`/`-wal`/`-shm`、`logs/`、`cache/`、`*.pid`、`reports/`）加入 `.gitignore` 并 `git rm --cached` | ✅ 工作区已完成 |
| 根目录一次性脚本归档到 `scripts/`（`fix.patch`、`fix_users.py` 等 → `scripts/archive/`） | ✅ |
| GitHub Actions CI（`.github/workflows/build-and-test.yml`：build + test + coverage + `dotnet format` + NuGet 漏洞扫描） | ✅ |
| 移除 csproj 中的 `NoWarn`（CS8602/CS8620/CS8625） | ✅ |

**剩余动作**：在 `nyf` 分支提交以上改动（与本地化改动分开提交更清晰）。提交由用户确认后执行。

## Phase 2 — 质量基线 ✅

### 2.1 YAML→运行时 端到端集成测试 ✅
- **现状**：已实现全面端到端集成测试。
- **目标**：覆盖框架核心链路——加载示例项目 → 渲染实体列表 → 提交 Create/Update/Delete → 执行自定义 Action → 校验数据库结果。
- **验收**：`dotnet test` 全绿；覆盖 List/Detail/Create/Update/Delete/InvokeAction 六条路径；测试不向仓库内的 `.db` 写入。

### 2.2 本地化回归测试 ✅
- **现状**：已包含在 `LocalizationIntegrationTests` 中，并验证了多语言切换以及 cookie > claim 优先级。

### 2.3 空引用警告清零 ✅
- **现状**：在 nyf 分支已全面修复编译警告（CS8604/CS8601/CS8767/CS0168/xUnit1031等历史警告全部清零）。

## Phase 3 — 运行时健壮性 ✅

### 3.1 根治 SQLite 写锁 ✅
- **现状**：连接层已统一启用 WAL + busy_timeout，写路径通过门闸串行化，已通过高并发写测试验证。

### 3.2 后台任务持久化（outbox） ✅
- **现状**：已在 system.db 中设计 `job_queue` 表并注册 Outbox 服务，支持事务入队、异步拉取执行及退避重试，测试已全绿。

## Phase 4 — 战略演进（API-first + AI 原生）

### 4.1 实体 REST API + OpenAPI ✅
- **现状**：`/api/{project}/{entity}` CRUD + 查询已实现（`ApiEntityController`），Bearer Token 鉴权、`meta.Api` 权限分级、Swagger 文档（`DynamicEntitySwaggerFilter`）均已就绪。2026-06-12 修复了 Swagger 过滤器中 `/api/{project}/{entity}/{id}` 路径（GET/PUT/PATCH/DELETE）未注册到 `swaggerDoc.Paths` 的 bug（误写为重复设置 `listPath`）。
- **验收**：Swagger UI 能列出全部实体端点（含 by-id 路径）；集成测试覆盖一条完整 API CRUD。

### 4.2 内置 MCP Server ✅
- **目标**：把每个租户的实体 CRUD、Action、查询暴露为 MCP 工具，使 AI 客户端（含 AiChatApp/Hyperion）协议级接入而非爬 HTML。
- **依赖**：4.1（复用同一服务层）；使用官方 C# MCP SDK（`ModelContextProtocol` / `ModelContextProtocol.AspNetCore` 1.4.0），以 Streamable HTTP transport 挂在现有主机上的 `/mcp`。
- **设计文档**：详见 [`docs/PHASE4.2-MCP-DESIGN.md`](./PHASE4.2-MCP-DESIGN.md)（架构、改动文件清单、工具列表、测试与验收标准）。
- **现状（2026-06-12 完成）**：新增 `EntityToolService` / `EntityMcpTools`（`Services/Mcp/`），通过 `IServiceProvider` 延迟解析项目相关服务以避免 `ProjectScope` 未初始化异常；`Program.cs` 注册 `AddMcpServer().WithHttpTransport().WithTools<EntityMcpTools>()` 并 `MapMcp("/mcp")`，要求 `Cookies,ApiToken` 认证。公开 9 个工具：`list_projects` / `list_entities` / `get_entity_meta` / `list_entity_records` / `get_entity_record` / `create_entity_record` / `update_entity_record` / `delete_entity_record` / `invoke_entity_action`。
- **验收**：`McpServerIntegrationTests`（5 个用例）验证 MCP 客户端可 `ListToolsAsync` 看到全部工具、`list_entity_records`/`create_entity_record`/`get_entity_record` 完成创建+读取闭环、对 `meta.Api=disabled` 实体（`comment`）的工具调用返回错误而非异常、未带 Bearer token 访问 `/mcp` 被拒绝。`dotnet build` 0 警告 0 错误，`dotnet test` 653/653 全绿。

### 4.3 YAML Schema 迁移系统 ✅
- **目标**：`DynamicEntityConfigDiffService` 进化为完整迁移管线：diff → 迁移计划（ALTER TABLE / 数据回填）→ 版本记录表 → 可回滚。
- **要点**：迁移计划先以 dry-run 输出 SQL 供确认；版本记录存各租户库内 `_nyf_migrations` 表。
- **设计文档**：详见 [`docs/PHASE4.3-MIGRATION-DESIGN.md`](./PHASE4.3-MIGRATION-DESIGN.md)（架构、改动文件清单、SQLite 表重建方案、测试与验收标准）。
- **现状（2026-06-13 完成）**：新增 `DynamicEntitySchemaMigrationService`，支持 BuildPlan、dry-run SQL、Apply、Rollback 和 `_nyf_migrations` 历史记录；SQLite 破坏性变更统一走表重建并保留备份表用于回滚。Admin UI 已新增 Schema Migration 页面，可预览 Up/Down SQL、应用迁移并回滚历史记录。
- **验收**：修改实体 YAML（加列/改类型/删列）后旧数据完好且可回滚。

### 4.4 PostgreSQL 生产模式
- **目标**：SQLite 保留为开发/单机模式；PostgreSQL（Npgsql 已引入）成为多租户生产模式一等公民（schema-per-tenant），顺带根治写锁问题。
- **依赖**：4.3（迁移系统需先支持两种方言）。

---

## 执行顺序与依赖

```
Phase 1 (✅) → 提交
Phase 2.1/2.2 (骨架✅) → 扩展用例
Phase 3.1 (✅ 2026-06-12 完成：SqliteConnectionHardening 补齐 WAL/busy_timeout=5000/synchronous=NORMAL + SqliteWriteGate 按库文件写串行化 + 9 个并发测试) → 3.2
Phase 4.1 → 4.2
Phase 4.3 → 4.4
```

Phase 3 与 Phase 2 可并行；Phase 4 各项以 4.1 为先导。每阶段完成标准统一为：CI 全绿 + 新功能有测试 + 文档更新本文件状态列。
