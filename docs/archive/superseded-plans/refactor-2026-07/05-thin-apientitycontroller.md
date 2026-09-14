# 05 — ApiEntityController 业务下沉服务层

> 目标文件: `NetYamlForge/Controllers/ApiEntityController.cs`（529 行）
> 类型: 结构重构（行为不变） · 风险: 中 · 依赖: 无

## 1. 现状分析（实测）

`ApiEntityController` 有 **9 个注入依赖** + 7 个 action，方法体里混入了本应属于服务层的逻辑：

| Action（行号） | 现状 | 问题 |
|----------------|------|------|
| `GetList`(92) | 查询编排 | 分页/过滤/投影编排在控制器 |
| `GetMeta`(139) | 元数据→DTO 映射 | 映射逻辑在控制器 |
| `GetById`(189) | 单条读取 | 尚可 |
| `Create`(214) / `Update`(257) / `PartialUpdate`(301) | 写入 + 校验 + 审计 | 校验/审计/hook 编排在控制器 |
| `Delete`(344) | 删除 + 审计 | 同上 |
| `InvokeAction`(380) | 动作分发（74 行，最长） | action 解析/参数绑定/分发逻辑集中在控制器 |
| `ValidateApiAccess`(55) | 权限判定 | 私有方法，属横切逻辑 |
| `ToApiDto`(454)/`ConvertValue`(472) | DTO 映射 + 类型转换 | 纯映射，应移出 |

**判据**：控制器应只做 HTTP 关注点（模型绑定、状态码、序列化）+ 委托。业务编排（校验→hook→持久化→审计）应在服务层。

> 注：`ApiListResponse`/`ApiDto`/`ApiEntityMeta`/`ApiColumnMeta`/`ApiFormMeta`（487-528）是 API 契约 DTO，**保留**，仅移出所在文件到 `Models/Api/`。

## 2. 目标结构

```
Services/Api/
  ApiEntityQueryService.cs     // GetList/GetById/GetMeta 的编排（读侧）
  ApiEntityWriteService.cs     // Create/Update/PartialUpdate/Delete 的编排（写侧：校验+hook+审计）
  ApiEntityActionService.cs    // InvokeAction 的解析/参数绑定/分发
  ApiEntityAccessGuard.cs      // ValidateApiAccess → 可复用的访问判定
  ApiDtoMapper.cs              // ToApiDto + ConvertValue（纯映射，static 或 service）
Models/Api/
  ApiContracts.cs              // ApiListResponse/ApiDto/ApiEntityMeta/... （从控制器文件移出）
Controllers/
  ApiEntityController.cs       // 仅保留 HTTP 层：绑定→调用 service→返回 IActionResult（目标 ~180 行）
```

## 3. 详细拆分映射

### 3.1 `ApiDtoMapper`（先做，最安全）
- 迁入 `ToApiDto`(454)、`ConvertValue`(472)，均为 `static`。改为 `internal static class ApiDtoMapper`。
- 控制器改调 `ApiDtoMapper.ToApiDto(...)`。此步零风险，可单独提交。

### 3.2 `ApiEntityAccessGuard`
- 迁入 `ValidateApiAccess`(55-88)。返回 `IActionResult?`（保留"null=放行"语义）或改为 `AccessDecision` 值对象由控制器翻译状态码。**首选保留 `IActionResult?` 语义**，改动最小。

### 3.3 `ApiEntityQueryService`（读侧）
- 迁入 `GetList`/`GetMeta`/`GetById` 的**编排体**（去掉 `[HttpGet]`/`return Ok()` 等 HTTP 外壳），依赖 `IDynamicCrudRepository`、`IEntityMetadataProvider`、`ProjectScope`。
- 方法返回**领域结果对象**（如 `ApiListResponse`、`ApiEntityMeta`、`ApiDto?`），由控制器决定状态码（404/200）。

### 3.4 `ApiEntityWriteService`（写侧）
- 迁入 `Create`/`Update`/`PartialUpdate`/`Delete` 编排：`DynamicEntityCommandService` + `DynamicEntityFormValidationService` + `IEntityHooksService` + `IAuditLogService`。
- 返回统一 `WriteResult { Success, Errors, Entity, NotFound }`，控制器映射为 200/201/400/404。
- **保留原有校验→hook→持久化→审计的执行顺序**（顺序敏感，逐行核对 214-378）。

### 3.5 `ApiEntityActionService`
- 迁入 `InvokeAction`(380-453) 的 action 查找、输入字段绑定、`IProjectActionRegistry` 分发逻辑。
- 控制器仅负责取路由参数、body 反序列化、包状态码。

### 3.6 `ApiEntityController`（瘦身后）
- 依赖数从 9 降到 ~4（`ApiEntityQueryService`/`ApiEntityWriteService`/`ApiEntityActionService`/`ApiEntityAccessGuard`）。
- 每个 action ≤ ~15 行：绑定 → guard → service → 映射状态码。

## 4. DI 注册
在 API 模块的 DI 扩展方法内新增 4 个 service（生命周期与其他 per-request service 一致，通常 `Scoped`）。

## 5. 测试策略

- 若已有 `ApiEntityController` 的集成/控制器测试，作为端到端护栏。
- 新增服务层单测：`ApiEntityWriteServiceTests`（校验失败→返回 Errors、NotFound 路径）、`ApiDtoMapperTests`（类型转换矩阵）。
- **重点回归**：状态码不变（201 vs 200、404、400 的触发条件与原控制器完全一致）。

## 6. 验收标准

- [ ] `ApiEntityController.cs` ≤ 200 行，依赖 ≤ 5
- [ ] 所有 action 的 HTTP 状态码、路由、返回体结构零变化
- [ ] 写侧执行顺序（校验→hook→持久化→审计）逐行保留
- [ ] build + test 全绿
