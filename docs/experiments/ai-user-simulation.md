# 设计规划：多 AI 角色系统使用模拟实验（AI User Simulation）

> 版本 v0.1 · 2026-07-07 · 状态：设计中
> 目标读者：框架维护者 / 实验执行者

---

## 1. 实验目标与假设

### 1.1 目标
用 **3 个 AI** 分别扮演**不同类型的真实系统用户**，以**不定期、非脚本化**的方式模拟真实使用场景来操作一个子项目，从其真实交互行为中：

1. 发现功能性 Bug（报错、数据不一致、状态机漏洞、校验缺口）；
2. 发现体验/设计缺陷（流程不顺、字段缺失、动作语义不清）；
3. 沉淀为可回归验证的改进项，形成"发现→改进→回归"闭环。

### 1.2 核心假设
- 真实用户不会按测试脚本行动，其**行为的多样性与随机性**能触达单元测试覆盖不到的路径。
- 不同**角色视角**（管理者/执行者/旁观者）会对同一系统提出**互相冲突的期望**，冲突点往往是 Bug 与设计缺陷的高发区。

### 1.3 明确的非目标
- 不是压测（不追求 QPS），是**行为覆盖**。
- 不是替代单元测试，是**探索性测试的自动化**。

---

## 2. 实验对象选择

**选定：`projects/task-management`（任务管理）**

| 评估维度 | task-management | 说明 |
|---|---|---|
| 工作流丰富度 | ★★★★ | 建/派/评论/状态流转/导出 |
| 多角色天然性 | ★★★★★ | 管理者·执行者·旁观者角色清晰 |
| 状态机复杂度 | ★★★★ | `not_started→in_progress→completed/on_hold` + `reopen` |
| 副作用可观测 | ★★★★ | 钩子 `validate_due_date`、`audit_log` 便于取证 |
| 隔离安全性 | ★★★★★ | 独立 db，污染可回滚 |

**已确认的交互面（来自 `entities/task.yml` / `comment.yml`）：**
- 实体：`task`（Title/AssignedTo/DueDate/Priority/Status/Notes/CreatedBy）、`comment`（关联 `TaskId`）
- 行动作：`mark_completed`（confirm）、`reopen`（必填 `Reason`）
- 过滤/分页/CSV 导出 `filtered_csv`
- 钩子：`beforeCreate/Update: validate_due_date`、`afterCreate/Update: audit_log`

> 备选对象：`redmine-clone`（更复杂，issue/milestone/time_entry，可作第二阶段升级目标）；`diary-companion`（已知图片标注 Bug，适合做"已知缺陷复现"对照组）。

---

## 3. 三个 AI 角色（Persona）设计

每个 Persona = **独立系统账号（独立 UserId + 角色权限）** + **行为倾向 Prompt** + **专属场景权重**。账号隔离是取证与权限测试的前提。

### Persona A —「项目经理 PM」(Planner)
- 心智：关注全局、期限与优先级；频繁重排、催办、看报表。
- 高频动作：批量建任务、改 `Priority`/`DueDate`、按 `Status` 过滤、导出 CSV、催促（评论 @）。
- 擅长暴露：批量/分页边界、过滤组合、导出字段一致性、越权改他人任务。

### Persona B —「执行者 Dev」(Doer)
- 心智：只关心"我的任务"；推进状态、写进展、偶尔拖延后补。
- 高频动作：`AssignedTo=自己` 过滤、`in_progress→completed`、`mark_completed`、被打回后 `reopen`、补 `Notes`。
- 擅长暴露：状态机非法跃迁、并发更新、`reopen` 后审计/时间戳、必填校验绕过。

### Persona C —「旁观者 Stakeholder」(Observer)
- 心智：不拥有任务，只读+提问；行为"不守规矩"，爱点边缘功能。
- 高频动作：只读浏览、评论提问、尝试导出、尝试编辑非授权字段、翻到不存在的页/传畸形参数。
- 擅长暴露：权限边界、只读越权、异常输入健壮性、空态/错误提示质量。

> **角色冲突即高价值信号**：PM 改了 DueDate 而 Dev 正在操作同一任务 → 并发/审计；Observer 尝试导出 → 权限矩阵是否一致。

---

## 4. 交互方式：AI 如何"使用系统"

优先走 **声明式实体自动生成的 REST API**（稳定、可断言），必要场景补充**页面级驱动**验证 UI。

```
Orchestrator(调度器)
  └─ 每个 Persona 独立会话
       ├─ 认证：以各自账号登录取 token（测权限隔离）
       ├─ 观察：GET 列表/详情，读取当前系统状态
       ├─ 决策：LLM 依据角色倾向 + 当前状态，选择下一动作
       ├─ 执行：POST/PUT/action 端点 或 页面操作
       └─ 记录：请求/响应/状态快照 → 观测日志(JSONL)
```

- **决策非脚本化**：给 LLM 提供"当前可见状态 + 角色目标 + 可用动作清单"，由其自选动作与参数，保证行为多样性。
- **可用动作清单**从实体 YAML 动态派生（CRUD + actions + exports + filters），项目演进时自动同步。

---

## 5. 不定期调度机制

- **调度器**：cron/loop 定时唤醒，但**注入抖动**（jitter）——每次触发随机挑选 1~2 个 Persona、随机间隔（如 3–40 min），避免规律化。
- **Session（一次使用会话）**：1 个 Persona 连续执行 3–8 个动作，模拟"打开系统办一段事再离开"。
- **强度档位**：`smoke`（低频冒烟）/ `normal` / `chaos`（高随机+畸形输入）。默认 `normal`，夜间批量跑 `chaos`。

---

## 6. Bug 发现与判定机制

三层信号采集，避免只看 HTTP 200：

| 层级 | 采集内容 | 判为可疑的规则（示例） |
|---|---|---|
| 协议层 | 状态码/耗时/异常栈 | 5xx；本应 403 却 200；超时 |
| 语义层 | 请求意图 vs 响应结果 | 状态非法跃迁成功；必填项空值入库；导出列 ≠ 定义列 |
| 断言层 | 预置不变量(invariants) | `completed` 无 `UpdatedAt`；`reopen` 未写 Reason/审计；越权改他人 `CreatedBy` 成功 |

**不变量库（invariants）示例**（随实验补充）：
1. 任一状态变更必更新 `UpdatedAt` 且产生 `audit_log`。
2. `DueDate` 早于今天创建时 `validate_due_date` 必拦截（除非明确允许）。
3. Persona 无权修改 `AssignedTo ≠ self` 的任务（若业务如此定义）。
4. CSV 导出行数 = 当前过滤条件命中行数。

命中可疑规则 → 生成 **Finding**（含复现请求序列、状态快照、期望 vs 实际），由一个 **"评审 AI"**（可复用 Code Reviewer 角色）去重、定级（P0–P3）、判真伪。

---

## 7. 改进闭环

```
观测日志 → 可疑信号 → 评审AI去重定级 → Finding清单
   → 人工/AI确认 → 修 YAML/钩子/框架 → 写回归不变量 → 下一轮验证
```

- 每个确认的 Bug **必须**新增一条不变量或 `NetYamlForge.Tests` 用例，防回归。
- 改进项分两类落地：**项目层**（改 `task.yml` 配置/钩子）与**框架层**（`Services/*` 通用能力缺陷）。

---

## 8. 数据隔离与安全（重要）

- 专用实验数据库副本，实验前快照、后可一键回滚（沿用子项目独立 db 机制）。
- 3 个 Persona 账号仅限本实验项目，**不得触及其他租户/子项目数据**（遵守多租户隔离）。
- `chaos` 档的畸形输入仅打实验端点，隔离于生产路由。
- 所有写操作带实验标签（如 `CreatedBy` 前缀 `sim:`），便于清理与区分。

---

## 9. 指标（KPI）

| 指标 | 含义 |
|---|---|
| 动作覆盖率 | 已触达的 端点×参数组合 / 全量 |
| 状态机覆盖率 | 已触达的合法+非法跃迁 / 全量 |
| 有效 Finding 数 | 去重后确认为真的缺陷 |
| 误报率 | 评审判伪 / 总 Finding |
| 回归防护 | 已转化为不变量/用例的缺陷占比 |
| MTTD | 从缺陷引入到被模拟发现的平均轮次 |

---

## 10. 实施阶段

| 阶段 | 内容 | 产出 |
|---|---|---|
| P0 打地基 | 建 3 账号、动作清单派生器、观测日志(JSONL)、快照/回滚 | 可手动跑单会话 |
| P1 单角色 | 先只跑 Persona B，验证采集/判定链路 | 首批 Finding |
| P2 三角色并发 | 引入调度器+jitter+角色冲突场景 | 并发/权限类缺陷 |
| P3 闭环自动化 | 评审AI定级 + 回归不变量自动登记 | 稳定运行的实验管线 |
| P4 升级对象 | 迁移到 redmine-clone / 加 diary 对照组 | 跨项目通用缺陷 |

**建议起步**：先做 P0+P1（对象 = task-management，Persona = B/Dev），链路跑通再扩角色。

---

## 11. 已决决策与实验状态 (Resolved Decisions & Experiment Status)

### 11.1 已决决策
1. **执行体映射**：确定由三个外部 CLI 扮演不同角色，各分配专有测试令牌：
   - **Claude Code CLI** -> PM (`token_pm_user`)
   - **Antigravity CLI** -> Dev (`token_dev_user`)
   - **OpenCode CLI** -> Observer (`token_obs_user`)
2. **API 开启与可见度调整**：
   - 在 `task.yml` 和 `comment.yml` 中配置了 `api: readwrite`。
   - 为了允许非 Admin 用户通过 REST 读写，已将它们的 `isPublic` 设置为 `true`。
3. **数据库配置**：在运行库 `NetYamlForge/var/data/system.db` 中完成了测试账号令牌的更新（脚本详见 `scripts/experiments/setup-simulation-users.sql`）。
4. **统一运行工具**：提供了命令行代理脚本 `scripts/experiments/run_simulation_step.py`，负责发起请求、记录 JSONL 审计日志并进行 5 项不变量自动检测。

### 11.2 实验验证结果与缺陷发现 (P0 阶段达成)
在第一轮单步交互测试中，已成功捕获框架的一项重大缺陷：
- **缺陷**：**`[AUTH_LEAK]` 权限泄露**。Observer 角色 (`taskmgr_viewer`) 在调用 `POST /api/task-management/task` 时，理论上应被拒绝访问（403），但由于 `ApiEntityAccessGuard` 仅做实体级 API 权限判断，未对用户角色进行行级隔离或角色鉴权限制，Observer 成功越权写入数据（HTTP 201）。
- **取证日志**：记录于 `logs/experiments/simulation.jsonl` 中，由 `run_simulation_step.py` 自动报警并以 Exit Code 2 终止。

### 11.3 运行与操作指南
各 AI CLI 成员在参与模拟时，应严格阅读并执行：[ai-user-simulation-playbook.md](file:///home/ubuntu/ws/NetYamlForge/docs/experiments/ai-user-simulation-playbook.md)。

