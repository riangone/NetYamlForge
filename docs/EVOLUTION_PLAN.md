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

## Phase 2 — 质量基线（进行中）

### 2.1 YAML→运行时 端到端集成测试（骨架已建）
- **现状**：`NetYamlForge.Tests/Integration/` 已有 `NetYamlForgeWebApplicationFactory`、`TestAuthHandler`、`YamlPipelineEndToEndTests`。
- **目标**：覆盖框架核心链路——加载示例项目 → 渲染实体列表 → 提交 Create/Update/Delete → 执行自定义 Action → 校验数据库结果。
- **改动点**：扩展 `YamlPipelineEndToEndTests`；必要时在测试夹具中用临时目录生成最小 YAML 项目（实体 + action + hook），避免依赖 `projects/` 下的演示数据。
- **验收**：`dotnet test` 全绿；覆盖 List/Detail/Create/Update/Delete/InvokeAction 六条路径；测试不向仓库内的 `.db` 写入。

### 2.2 本地化回归测试（骨架已建）
- **现状**：`Localization/UserPreferredLanguageProviderTests.cs` 已存在（固化 cookie > claim 优先级，对应 fe1c1d8 修复）。
- **补充**：为 `LocalizationController` 的语言切换端点加集成测试（切换后响应 Set-Cookie、后续请求生效）。

### 2.3 空引用警告清零
- **现状**：`NoWarn` 已移除；需确认 Release build 无 warning 回归（CI 的 build 步骤可加 `-warnaserror:nullable` 分阶段收紧）。

## Phase 3 — 运行时健壮性

### 3.1 根治 SQLite 写锁（优先级最高）
- **背景**：`BatchJobExecutor` / `AiFolderProcessorExecutor` 曾因 SQLite Error 5 (database is locked) 失败，目前靠重试缓解。
- **目标**：连接层统一启用 WAL + busy_timeout，写路径串行化。
- **改动点**：
  - `Services/Connection/ConnectionManager.cs`：SQLite 连接打开时执行 `PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;`（仅对 SQLite 方言；WAL 对同一文件只需设置一次但幂等）。
  - 评估每租户引入单写者队列（`SemaphoreSlim` per database file）包裹写命令；读不受限。
  - 现有零散重试逻辑收敛到连接层一处。
- **验收**：新增并发写集成测试（两个执行器同时写同一租户库不再抛 Error 5）；现有测试全绿。

### 3.2 后台任务持久化（outbox）
- **背景**：BatchJob / AiFolderProcessor 进程内执行，重启即丢。
- **目标**:在 `system.db` 增加 `job_queue` 表（id、type、payload、status、attempts、scheduled_at、completed_at），执行器改为「入队 → 拉取 → 标记」模型，启动时恢复 `running` 状态的遗留任务。
- **验收**：杀进程重启后未完成任务被重新拾起；失败任务带退避重试与最大次数。

## Phase 4 — 战略演进（API-first + AI 原生）

### 4.1 实体 REST API + OpenAPI
- **目标**：基于已有 `EntityMetadata`，为每个租户项目的每个实体自动暴露 `/api/{project}/{entity}` 的 CRUD + 查询（复用 `DynamicEntityListQueryService` / `EntityCrudExecutionService`），并自动生成 OpenAPI 文档。
- **要点**：鉴权复用现有 cookie/claim 体系 + 新增 API token；YAML 中可按实体声明 `api: enabled/readonly/disabled`。
- **验收**：Swagger UI 能列出全部实体端点；集成测试覆盖一条完整 API CRUD。

### 4.2 内置 MCP Server
- **目标**：把每个租户的实体 CRUD、Action、查询暴露为 MCP 工具，使 AI 客户端（含 AiChatApp/Hyperion）协议级接入而非爬 HTML。
- **依赖**：4.1（复用同一服务层）；使用官方 C# MCP SDK（`ModelContextProtocol` NuGet 包），以 HTTP/SSE transport 挂在现有主机上。
- **验收**：MCP 客户端可 list tools 并完成一次实体创建。

### 4.3 YAML Schema 迁移系统
- **目标**：`DynamicEntityConfigDiffService` 进化为完整迁移管线：diff → 迁移计划（ALTER TABLE / 数据回填）→ 版本记录表 → 可回滚。
- **要点**：迁移计划先以 dry-run 输出 SQL 供确认；版本记录存各租户库内 `_nyf_migrations` 表。
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
