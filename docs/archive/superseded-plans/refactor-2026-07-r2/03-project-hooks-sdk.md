# R2-03 — 项目侧 Hook 代码 SDK 化 / 抽 BatchExecutorBase

> 范围: `NetYamlForge/projects/*/Hooks/**`（尤其 BatchExecutors）+ 新增框架侧共享基类/helper
> 类型: 结构重构（项目侧） · 风险: 中（逐项目独立，影响面隔离） · 依赖: 无
> **行为变化: 否** — 纯抽取共享样板，行为逐执行器保持等价。

## 1. 现状（实测）

框架**核心**已在第一轮瘦身，但真正的巨型文件在 **projects 侧**，且**未继承任何共享基类**：

| 文件 | 行数 |
|------|------|
| `projects/auto-dealer-demo/Hooks/AutoDealerHooks.cs` | 1031 |
| `projects/photo-vocab/Hooks/BatchExecutors/PhotoAnnotatorExecutor.cs` | 731 |
| `projects/auto-dealer-demo/Hooks/BatchExecutors/AiDealerEngineExecutor.cs` | 610 |
| `projects/biz-docs/Hooks/BatchExecutors/InvoiceEmailProcessorExecutor.cs` | 585 |
| `projects/biz-card/Hooks/BatchExecutors/BizCardParserExecutor.cs` | 551 |
| `projects/blog/Hooks/BatchExecutors/AutomatedBlogGeneratorExecutor.cs` | 498 |

框架侧**已有** `Services/BatchJob/AiBatchExecutorBase.cs` / `AiExecutorBase.cs` / `AiQueueStepHandlerBase.cs`，
但上述项目执行器**没有复用它们**，各自重复实现：超时控制、CLI fallback 链、错误处理、进度/状态写回、结果解析样板。

> 注意：`AiAnnotatorExecutor`（框架侧，photo-vocab 相关）第一轮记忆里提到 photo-vocab **尚未迁移到 `ICliChainService`**，存在单点故障。本项正好统一收口该问题。

## 2. 目标

抽出一层 **项目侧 SDK**（共享基类 + helper 包），让每个 BatchExecutor 只写"业务差异部分"，把样板下沉：
1. 统一 CLI 调用（**强制走 `ICliChainService`**，消灭直连 `IAntigravityCliService` 的单点故障）。
2. 统一超时 / 取消 / 重试 / fallback 策略。
3. 统一错误处理与状态写回（对接第一轮 06 结构化日志、R2-02 span）。
4. 统一 AI 结果解析（JSON 提取/校验的公共 helper）。

## 3. 设计

### 3.1 明确"框架 SDK"边界
在 `Services/BatchJob/` 下确立**面向项目**的抽象层（复用/扩展现有 `AiBatchExecutorBase`）：

```csharp
// 项目执行器统一继承此基类，只实现抽象钩子
public abstract class ProjectBatchExecutorBase : AiBatchExecutorBase
{
    // 由 SDK 提供：超时+取消+fallback 的统一 CLI 调用
    protected Task<CliResult> RunCliAsync(CliRequest req, CancellationToken ct);

    // 由 SDK 提供：AI 文本 → 强类型结果（JSON 提取 + schema 校验 + 容错）
    protected bool TryParseJson<T>(string raw, out T? result, out string? error);

    // 由 SDK 提供：统一进度/状态写回 + 结构化日志 + span
    protected Task ReportProgressAsync(BatchProgress p, CancellationToken ct);

    // —— 子类只实现这些业务差异 ——
    protected abstract Task<StepOutcome> ExecuteBusinessAsync(BatchStepContext ctx, CancellationToken ct);
}
```

### 3.2 共享 helper 包
`Services/BatchJob/Sdk/`（新目录）：
- `CliInvoker`：封装 `ICliChainService` 调用 + 超时 + fallback 链（**唯一**入口，禁止项目侧直连底层 CLI）。
- `AiResultParser`：AI 输出解析（去除 markdown 围栏、容错 JSON、可选 schema 校验）。
- `BatchTelemetryScope`：`using` 包住 span（R2-02）+ 结构化日志 scope（06），一次性接好可观测性。

### 3.3 单项目改造范式（以 `PhotoAnnotatorExecutor` 为样板）
1. 继承 `ProjectBatchExecutorBase`。
2. 删除本地的超时/fallback/错误处理/JSON 解析样板，改调基类方法。
3. 直连 CLI 的调用 → 改走 `RunCliAsync`（顺带修复 photo-vocab 单点故障）。
4. 只保留"这个执行器独有的业务逻辑"于 `ExecuteBusinessAsync`。
5. 目标：**每个执行器砍到 ~250 行以内**。

### 3.4 `AutoDealerHooks.cs`（1031 行，非 BatchExecutor）
这是普通 Hook 聚合文件，按职责用 `partial class` 或多文件拆分（对齐第一轮 03/04 的拆文件手法）：
- 按功能域分组（如 定价 / 库存 / 客户 / AI 引擎），一域一文件，命名空间不变，调用方零改动。

## 4. 落地顺序（分 PR，一项目一 PR）

1. **PR-1**：框架侧——`ProjectBatchExecutorBase` + `Sdk/` helper 包 + 单测（mock `ICliChainService`，断言超时/fallback/解析）。**不改任何项目**。
2. **PR-2**：`PhotoAnnotatorExecutor` 迁移（同时修 photo-vocab 单点故障）——作为样板 PR，其余项目照此模板。
3. **PR-3..N**：`AiDealerEngineExecutor` / `InvoiceEmailProcessorExecutor` / `BizCardParserExecutor` / `AutomatedBlogGeneratorExecutor` 各一 PR。
4. **PR-末**：`AutoDealerHooks.cs` 拆文件。

## 5. 边界与风险

- **行为等价性**：每个执行器迁移后，其产出（数据库写回、状态、AI 结果）必须与迁移前一致。**迁移 PR 必须带一条"golden"回归**（同输入→同输出）或至少人工核对一次真实批处理。
- **不做过度抽象**：只抽"确实重复 3+ 次"的样板；项目独有逻辑留在项目侧，避免基类膨胀成新的上帝类。
- **迁移与 R2-02 解耦**：`BatchTelemetryScope` 依赖 R2-02 的 `ForgeTelemetry`；若 R2-02 未落地，先让 scope 只接日志（06），span 留 TODO，不阻塞本项。

## 6. 验收标准

- [ ] `ProjectBatchExecutorBase` + `Sdk/` helper 有单测覆盖（超时/取消/fallback/JSON 解析容错）
- [ ] 至少 4 个项目执行器迁移到基类，每个 ≤ ~250 行
- [ ] 项目侧**无任何**直连底层 CLI（`IAntigravityCliService` 等）的调用，全部经 `ICliChainService`/`CliInvoker`
- [ ] photo-vocab 单点故障消除（走 CLI 链 fallback）
- [ ] `AutoDealerHooks.cs` 拆分后无单文件 > ~450 行，命名空间/公共签名不变
- [ ] 每个迁移 PR 附行为等价证据（golden 回归或人工核对记录）
- [ ] 现有测试全绿
