# AI 角色系统模拟实验：CLI 运行手册 (AI User Simulation Playbook)

> 版本：v1.0 · 2026-07-07  
> 适用对象：Antigravity CLI / Claude Code CLI / OpenCode CLI  
> 目标系统：NetYamlForge / projects/task-management  

本手册指导三个外部 AI CLI 扮演不同角色，使用不变量（Invariants）检测框架的潜在缺陷。

---

## 1. 快速入门与环境准备

我们提供了一个统一的执行代理工具 [run_simulation_step.py](file:///home/ubuntu/ws/NetYamlForge/scripts/experiments/run_simulation_step.py), 用于代表角色发送 API 请求，自动记录 JSONL 日志，并进行实时不变量校验。

### 1.1 实验角色与凭证

在进行测试时，请在命令中指定对应的 `--user` 参数。系统已在数据库中完成如下配置：

| CLI 工具 | 扮演角色 | 账号名 (User) | 模拟令牌 | 核心倾向与测试目的 |
| :--- | :--- | :--- | :--- | :--- |
| **Claude Code CLI** | **PM 项目经理 (pm)** | `taskmgr_manager` | `token_pm_user` | 批量创建任务、修改期限/优先级、频繁进行过滤和导出。测试边界值与组合查询。 |
| **Antigravity CLI** | **Dev 执行者 (dev)** | `taskmgr_worker1` | `token_dev_user` | 推进状态（如 `in_progress` -> `completed`）、调用 `reopen` 等行操作。测试状态机与审计一致性。 |
| **OpenCode CLI** | **Observer 旁观者 (obs)** | `taskmgr_viewer` | `token_obs_user` | 乱点、输入畸形参数、测试越权写入。测试系统的鲁棒性与权限边界。 |

---

## 2. 交互指令与操作指南

在模拟会话中，AI 角色应当循环执行以下步骤：
1. **观察**：查询当前任务列表以获取系统状态。
2. **决策**：根据角色倾向和当前状态，决定下一步的动作。
3. **执行**：使用 `run_simulation_step.py` 执行动作。
4. **验证**：检查脚本输出的 `INVARIANT VIOLATIONS`（不变量违规）。

### 2.1 常用命令示例

#### (1) PM 项目经理 (pm) - 查询列表与创建任务
```bash
# 查询当前所有任务
python3 scripts/experiments/run_simulation_step.py --user pm --method GET --path /api/task-management/task

# 创建新任务（注意：实验写操作建议带 "sim:" 前缀）
python3 scripts/experiments/run_simulation_step.py --user pm --method POST --path /api/task-management/task --data '{"Title": "sim: 编写系统设计文档", "AssignedTo": "taskmgr_worker1", "DueDate": "2026-07-15", "Priority": "high", "Status": "not_started"}'
```

#### (2) Dev 执行者 (dev) - 修改任务与状态跃迁
```bash
# 更新任务说明 (假设修改 ID 为 1 的任务)
python3 scripts/experiments/run_simulation_step.py --user dev --method PUT --path /api/task-management/task/1 --data '{"Title": "项目计划書の作成", "AssignedTo": "山田 太郎", "DueDate": "2026-04-10", "Priority": "high", "Status": "in_progress", "Notes": "添加实验备注：正在推进中"}'

# 触发自定义行操作：完成任务 (mark_completed)
python3 scripts/experiments/run_simulation_step.py --user dev --method POST --path /api/task-management/task/1/actions/mark_completed

# 触发自定义行操作：重新打开任务 (reopen，需要传 Reason)
python3 scripts/experiments/run_simulation_step.py --user dev --method POST --path /api/task-management/task/1/actions/reopen --data '{"Reason": "测试需要重新调整计划"}'
```

#### (3) Observer 旁观者 (obs) - 测试权限越界与异常输入
```bash
# 测试 Observer 是否能越权创建任务（预期应返回 403，若返回 201 则为 Bug）
python3 scripts/experiments/run_simulation_step.py --user obs --method POST --path /api/task-management/task --data '{"Title": "sim: 越权尝试", "AssignedTo": "taskmgr_worker2", "DueDate": "2026-07-20", "Priority": "low", "Status": "not_started"}'
```

---

## 3. 不变量（Invariants）与缺陷检测

`run_simulation_step.py` 内置了以下 5 项自动不变量校验。如果执行返回 **Exit Code 2**, 则说明触发了缺陷：

1. **[PROTOCOL_ERROR]**：服务器返回了 5xx 状态码或连接被拒绝。
2. **[AUTH_LEAK]**：只读的旁观者（obs）成功执行了写操作（POST/PUT/DELETE），说明权限边界失效。
3. **[VALIDATION_BYPASS]**：任务被允许保存一个过去的 `DueDate` 期限日，说明 `validate_due_date` 校验钩子未生效。
4. **[AUDIT_MISSING_UPDATED_AT]**：任务更新成功，但响应中 `UpdatedAt` 为空，说明审计时间戳机制或 `audit_log` 钩子有缺陷。
5. **[ACTION_INPUT_BYPASS]**：执行 `reopen` 操作时未传 `Reason` 或传空值，但接口却成功执行，说明动作输入校验被绕过。

---

## 4. 实验报告与闭环

当 AI CLI 发现不变量违规时，应当：
1. 记录违规详情，包含：
   - 触发角色（pm/dev/obs）
   - HTTP 请求（Method & Path）
   - 违规不变量类型
   - 响应载荷（Response Payload）
2. 在项目论坛或实验归档中记录该缺陷，并转交给修复人员或自动测试用例。
3. 所有交互日志已追加记录在：[logs/experiments/simulation.jsonl](file:///home/ubuntu/ws/NetYamlForge/logs/experiments/simulation.jsonl)

### 5.1 已修复的缺陷 `[AUTH_LEAK]`

**状态: ✅ 已修复 (2026-07-07)**

`Observer` 越权写入漏洞已在 `ApiEntityAccessGuard.cs` 中通过 RBAC 角色检查修复：
- 写操作现在要求用户拥有 `worker`/`manager`/`admin`/`operator` 之一角色
- `viewer` 角色会被正确地拒绝写操作 (HTTP 403)
- 实测验证通过：`token_obs_user` POST 创建任务返回 `403 Access Denied`

### 5.2 自动化持续测试

提供了 [run_simulation_loop.py](../../scripts/experiments/run_simulation_loop.py) 用于自动化持续运行测试：
```bash
# 无限循环（默认间隔 45s，随机抖动 ±30%）
API_BASE_URL=http://localhost:5009 python3 scripts/experiments/run_simulation_loop.py

# 限制次数
API_BASE_URL=http://localhost:5009 python3 scripts/experiments/run_simulation_loop.py --iterations 50 --interval 30

# 调整抖动幅度
API_BASE_URL=http://localhost:5009 python3 scripts/experiments/run_simulation_loop.py --interval 60 --jitter 0.5
```
- 自动轮换 PM 建单 / Dev 推进 / Obs 越权检测
- 随机抖动防止流量模式固定
- 每 10 轮输出一次成功率统计
