# AI User Simulation — auto-dealer-demo（汽车销售子项目）

迁移自 `docs/experiments/ai-user-simulation.md`（task-management 原始实验）。框架层
（JSONL 审计日志、固定 api_token 鉴权、不变量检查）直接复用；Persona 心智、不变量
库、账号权限配置按本子项目重新设计。状态：P0（脚手架 + 首轮探测）已完成。

## 1. 前置配置

- 账号/角色：`scripts/experiments/setup-simulation-users-auto-dealer.sql`
  （已执行）。执行时发现并修正了 **ROLE_SOURCE_MISMATCH**：yamada/suzuki/
  takahashi 在 `app_user_project_role` 里此前是通用角色 `user`，与
  `employees.role`（sales_manager/sales_rep/operator）及 `project.yaml`
  的菜单角色声明不一致。
- 实体权限：`entities/sales_leads.yml` 已设置 `api: readwrite` + `isPublic: true`，
  以便 Persona 通过 REST 直接访问（注释中已标注：Guard 仅做实体级判定，
  无角色/所有者过滤——见下方 Finding 3）。
- 执行脚本：`scripts/experiments/run_simulation_step_auto_dealer.py`。
- Persona：mgr(yamada/sales_manager) / rep(suzuki/sales_rep) /
  op(takahashi/operator) / cust(sato/customer, CUST001)。

## 2. 本轮首次运行发现（2026-07-07）

### Finding 1 — HOT_RELOAD_CONTENTROOT_MISMATCH（部署脚手架缺陷）
`Services/HotReload/YamlHotReloadService.StartAsync` 用
`Directory.GetCurrentDirectory()` 定位 `projects/` 目录来启动文件监听，而
`ProjectManager`（正确用法）用的是 `IHostEnvironment.ContentRootPath`
（`Services/Project/ProjectManager.cs:48`）。当前部署里进程启动时的
CWD 与 `ASPNETCORE_CONTENTROOT` 指向的目录不一致，导致
`Directory.Exists(projectsDir)` 为 false，**热重载对本部署完全失效**——
`sales_leads.yml` 改完 `api: readwrite` 后，实测两分钟内多次请求仍返回
"API Access Disabled"，必须重启整个进程才生效。这不是个别 bug，而是
"用 CWD 而非 ContentRootPath 定位路径"这一模式在代码库里不一致使用的
体现（详见 Finding 2 是同一模式的另一处实例）。

### Finding 2 — SYSTEM_DB_PATH_DRIFT（同一根因，更高风险）
`Program.cs`（~L131-151）用同样的 `Directory.GetCurrentDirectory()` 计算
`var/data/system.db` 的实际路径。实测中，把进程启动 CWD 从仓库根目录改成
`ContentRootPath`（本想顺便修 Finding 1）后，鉴权立刻从 403 变成 401
"Invalid API Token"——因为进程转而读写了另一份、没有任何 Persona
token 的 `system.db`。排查发现磁盘上已经存在 **三份相互独立的
system.db**（仓库根目录 / `NetYamlForge/` / `NetYamlForge/var/data/`），
证明这种漂移在此环境里并非假设，而是已经真实发生过。**任何依赖 CWD
而非 ContentRootPath 解析路径的代码，都有静默切换到错误数据文件的风险**，
建议统一改为注入 `IHostEnvironment.ContentRootPath`。

（因为 Finding 1 的修复方式与 Finding 2 依赖的"正确" CWD 互相冲突，本轮
先以"整进程重启（沿用原 CWD）"代替热重载验证 YAML 改动，未改动生产代码。）

### Finding 3 — CROSS_CUSTOMER_LEAK（数据越权，严重）★
`cust` persona（sato，仅应拥有 `CUST001`/`CUST-001` 自己的数据）
`GET /sales_leads?pageSize=5` 返回 200，且数据包含 `CUST-002`～`CUST-005`
等其他顾客的完整线索记录（含预算、评分、AI 触达路径等）。根因与
`sales_leads.yml` 注释里预先写好的警告完全一致：`ApiEntityAccessGuard`
只做**实体级** `api: disabled/readonly/readwrite` 判定，**没有行级 /
所有者过滤**，只要实体被打开 `readwrite` + `isPublic: true`，任何认证用户
（含顾客角色）都能看到全表数据。这是模拟实验本轮的核心发现，性质与
task-management 实验中的 `AUTH_LEAK` 相同：**开放 REST 读写是"能通过
API 测通"的最小改动，但绕开了页面层本该有的行级权限**。

修复方向：给 `sales_leads`（以及未来任何面向 customer 角色开放的实体）
增加基于 `customer_id = current_user.customer_id` 的行级过滤钩子
（`beforeQuery`/查询级 WHERE 注入），或者在 `ApiEntityAccessGuard` 里
为 customer 类角色引入"仅本人数据"这一通用规则，而不是把行级隔离寄望于
每个实体各自实现。

### Finding 4 — ROLE_PERMISSION_VOCABULARY_MISMATCH（阻断读写模拟）★
`ApiEntityAccessGuard.ValidateApiAccess`（`Services/Api/ApiEntityAccessGuard.cs:104-108`）
把"允许写"的角色硬编码为 `worker/manager/admin/operator`——这是
task-management 项目的角色词汇表。auto-dealer-demo 用的角色是
`sales_manager`/`sales_rep`/`customer`，其中只有 `operator` 恰好命中。
实测：
- `suzuki`(sales_rep) PUT 自己的线索 → 403 Access Denied
- `yamada`(sales_manager) PUT 线索 → 403 Access Denied
两者都不匹配硬编码列表，尽管 `project.yaml` 的菜单声明
（`roles: [sales_rep, sales_manager]`，第 90 行）明确把 SalesLeads 页面
的读写权限授予了这两个角色——**UI 层权限模型与 REST API 权限模型使用
两套互不相通的角色词汇**，导致任何非 task-management 语义角色的写操作
在 API 层被无差别拒绝。这是"不变量库/权限判定不能硬编码单一项目语义"
在代码里的具体体现，直接印证了此前迁移评估里的预判。修复方向：把这份
角色白名单做成按项目可配置项（如从 `project.yaml` 的角色声明动态派生），
而不是写死在 Guard 类里。

## 3. 结论

首轮模拟已经产出 1 个严重数据泄露发现（Finding 3）+ 1 个阻断读写路径的
权限模型不一致（Finding 4）+ 2 个基础设施脚手架缺陷（Finding 1/2，
均源于同一处"CWD 代替 ContentRootPath"模式）。建议按优先级：先修
Finding 3（真实越权风险），再决定 Finding 4 是"运行期动态派生角色白名单"
还是"最小改动加两行角色名"，Finding 1/2 作为技术债记录、非阻断项。
