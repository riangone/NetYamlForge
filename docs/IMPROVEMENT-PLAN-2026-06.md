# NetYamlForge 改进实施计划（2026-06）

> 状态：Phase 1 由 AI 代理并行实施中；Phase 2 为已细化待实施的规格。
> 原则：所有改动不自动提交，统一留在工作区供人工 review 后随 `nyf` 分支提交。

## 背景

框架核心（YAML 驱动实体、Hooks、BatchJob、多租户、本地化）已基本成型，
当前短板集中在工程基线与运行时健壮性，中期方向是 API-first + AI 协议级接入。

经核查的真实现状（与早期口头评估的差异）：

- `.github/workflows/build-and-test.yml` **已存在**，但有缺陷（见 P1-2）。
- `.gitignore` **已覆盖**大部分运行时产物，但 18 个 `.db`/`system.db` 等文件
  在规则生效前已被跟踪，导致永远显示 modified。
- 测试覆盖比预想广（40+ 测试类），缺的是「YAML → 运行时」端到端链路。

---

## Phase 1：工程基线修复（本次由代理实施）

### P1-1 Git 卫生（Agent A）

**问题**：`git ls-files` 中有 18 个运行时文件被跟踪：
15 个 `projects/*/database|data/*.db`、`NetYamlForge/chinook.db`、
`NetYamlForge/system.db`、根 `system.db`、`data/netyamlforge.db`；
另有根目录一次性脚本（`fix.patch`、`fix_users.py`、`fix_user_auth.py`、
`fix_release.py`、`refresh_demo_dates.py`、`send_test_email.py`、
`seed_auto_dealer_demo*.py`）散落。

**任务**：
1. `git rm --cached`（保留磁盘文件）解除所有 db/wal/shm/log/pid 跟踪；
   先确认 `SystemDatabaseInitializer` / schema 同步能在 db 缺失时重建结构。
2. 补全 `.gitignore`（`*.db` 全局规则 + 必要的例外）。
3. 一次性脚本归入 `scripts/`（保留 seed 脚本，归档 fix 脚本），
   检查并更新文档/shell 中对这些路径的引用。

**验收**：`git status` 不再出现 db/log/pid 噪音；`dotnet build` 通过。

### P1-2 CI 修复（Agent B）

**问题**（`.github/workflows/build-and-test.yml`）：
1. 分支过滤只含 `main/develop/feature/**`，当前开发分支 `nyf` 推送从不触发 CI。
2. `dotnet format --verify` 不是合法参数（应为 `--verify-no-changes`），
   且 `--no-restore` 位置存疑。
3. `codecov-action` `fail-ci-if-error: true` 在无 token 的仓库会拖垮 CI。

**任务**：修正以上三点；本地以 `dotnet restore && dotnet build -c Release
&& dotnet test -c Release` 验证 workflow 等价命令真实通过。

**验收**：workflow 语法正确、分支覆盖 `nyf`、本地等价命令全绿。

### P1-3 运行时健壮性 + 测试固化（Agent C）

1. **SQLite 写锁**：审计 `Services/Connection/ConnectionManager.cs`、
   `Services/BatchJob/AiFolderProcessorExecutor.cs`、
   `Services/Project/ProjectManager.cs` 的 WAL/`busy_timeout` 设置，
   确保所有连接路径统一启用 WAL + busy_timeout（而非个别执行器自带重试）。
2. **本地化回归测试**：fe1c1d8 修复了 cookie 优先于 claim 的语言切换，
   为 `UserPreferredLanguageProvider` 补单元测试固化优先级
   （cookie > claim > Accept-Language）。
3. **NoWarn 评估**：csproj 压制了 CS8602/CS8620/CS8625。先去掉测量警告量：
   ≤60 条则直接修复并移除 NoWarn；否则保留 NoWarn、在计划中记录数量与
   分模块清零路线。

**验收**：`dotnet test` 全绿；新增本地化测试覆盖三级优先级。

### P1-4 端到端集成测试（Phase 1 收尾，可后续会话实施）

用 `WebApplicationFactory` 建一条最小链路测试：
加载一个示例项目 → GET 实体列表页 → POST 执行一个 Action → 断言 DB 变更。
作为后续所有渲染层/命令层改动的兜底。

---

## Phase 2：进化方向（规格已定，待后续会话实施）

### P2-1 API-first / Headless（优先级最高）

- 基于 `EntityMetadata` 为每个租户项目的实体自动生成 REST 端点：
  `GET/POST /api/{project}/{entity}`、`GET/PUT/DELETE /api/{project}/{entity}/{id}`、
  `POST /api/{project}/{entity}/{id}/actions/{actionKey}`。
- 复用现有 `EntityCrudExecutionService` / `DynamicEntityCommandService`，
  控制器只做协议转换，不复制业务逻辑。
- 自动生成 OpenAPI 文档（每租户一份 swagger.json），沿用现有
  `PagePermissionService` 鉴权。
- 验收：对任一示例项目，无需写代码即可通过 API 完成 CRUD + Action。

### P2-2 内置 MCP Server

- 在 P2-1 之上把实体 CRUD/Action/查询暴露为 MCP 工具
  （tool 命名：`{project}_{entity}_{operation}`）。
- 传输：先 stdio（本地 AI 客户端），后 Streamable HTTP（远程接入）。
- 与 AiChatApp/Hyperion 打通，替代 Chrome 扩展爬 HTML 的接入方式。

### P2-3 YAML 变更迁移系统

- 在 `DynamicEntityConfigDiffService` 之上：diff → 迁移计划
  （ALTER TABLE / 数据回填 / 破坏性变更警告）→ 版本记录表 → 回滚。
- 迁移历史存 system.db；破坏性变更默认 dry-run，需显式确认。

### P2-4 存储层升级路径

- SQLite 保留为开发/单机模式；PostgreSQL（Npgsql 已引入）成为
  多租户生产模式一等公民（database-per-tenant 起步）。
- 根治写锁问题；连接管理抽象已在 `ConnectionManager`，扩展方言即可。

---

## 实施顺序与依赖

```
P1-1 ─┐
P1-2 ─┼─ 并行（文件不相交）──→ 构建/测试全绿 ──→ P1-4 ──→ P2-1 ──→ P2-2
P1-3 ─┘                                              └──→ P2-3 ──→ P2-4
```

风险提示：P1-1 解除 db 跟踪后，新 clone 不含演示数据——结构由框架重建，
演示数据依赖 seed 脚本（目前仅 auto-dealer 有，其余项目按需补）。
