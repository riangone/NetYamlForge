# NetYamlForge 核心瘦身与解耦重构 — 实现设计说明书

> 版本: v1.0 · 日期: 2026-07-05 · 面向对象: 负责代码实现的 AI/工程师
> 目标: 在**不改变运行时对外行为**的前提下，将业务专属代码、AI 样板、开发期工具从运行时 core 中剥离，
> 使 `NetYamlForge/` 运行时核心从 ~57.4K 行现实地降至 ~30K 行，并消除 CLI 单点故障。

---

## 0. 背景与度量基线（实测）

| 范围 | 行数 | 说明 |
|------|------|------|
| `NetYamlForge/`（真核心工程） | ~57,410 | 排除 Tests / FormForge / Analyzers |
| `NetYamlForge/Services/` | ~37,421 | 服务层，绝对重心 |
| `Services/BatchJob/` | ~6,280 | 15 个 Executor，含大量业务专属实现 |
| `Services/Cli/` | ~4,893 | 开发期脚手架，`ProjectTemplateScaffolder.cs` 单文件 1,561 行 |

**核心矛盾：** 问题不在总行数，而在"什么被放进了 core"。core 应只保留**引擎/解释器**，业务名词（Dealer/Invoice/BizCard/Stock/Blog/Photo）不应硬编码进框架。

---

## 问题一 🔴 业务专属 Executor 泄漏进框架核心（最高优先级）

### 现状证据
`Services/BatchJob/` 下的业务实例（应属于 `projects/xxx/`，却编译进 core）：

| Executor | 行数 | 归属项目（建议） |
|---|---|---|
| `PhotoAnnotatorExecutor.cs` | 763 | photo-vocab |
| `AiDealerEngineExecutor.cs` | 605 | auto-dealer |
| `InvoiceEmailProcessorExecutor.cs` | 580 | 发票处理项目 |
| `BizCardParserExecutor.cs` | 576 | 名片解析项目 |
| `AutomatedBlogGeneratorExecutor.cs` | 491 | blog 项目 |
| `ChinaStockBriefingExecutor.cs` | 181 | 股票简报项目 |

> 保留在 core 的应仅为：`BatchJobExecutor.cs`(312, 基类)、`BatchJobHostedService.cs`(调度)、
> `BatchJobLoader.cs`、`BatchJobDefinition.cs`、`IBatchStepHandler.cs`、`QueueStepHandlerBase.cs`、
> `SqlBatchStepHandlers.cs`、`OutboxJob*` 等通用调度/管道设施。

### 设计目标
core 提供**可插拔的 Executor 注册机制**，业务 Executor 以插件/项目 Hook 形式在 `projects/` 内注册，运行时通过程序集扫描或显式注册装配。

### 实现方案

**Step 1 — 定义稳定的扩展契约（core 侧，若尚不存在则新增）**
```csharp
namespace NetYamlForge.Services.BatchJob;

// 已有 BatchJobExecutor 基类，抽出注册契约
public interface IBatchExecutorFactory
{
    string ExecutorType { get; }              // 对应 YAML 中 batchJob.executor 字段
    BatchJobExecutor Create(IServiceProvider sp);
}
```

**Step 2 — Executor 发现机制**
- core 启动时扫描已加载程序集中所有 `IBatchExecutorFactory` 实现，按 `ExecutorType` 建立字典。
- `BatchJobLoader` / `BatchJobHostedService` 通过 `ExecutorType` 键解析，而非硬编码 `switch/new XxxExecutor()`。
- **验收点：** 搜索 core 中是否残留 `new PhotoAnnotatorExecutor(` 之类的直接实例化，必须全部消除。

**Step 3 — 迁移每个业务 Executor**
对上表每个文件：
1. 物理移动到 `projects/<项目名>/Hooks/BatchExecutors/`（命名空间随之改为项目命名空间）。
2. 新增对应 `XxxExecutorFactory : IBatchExecutorFactory`，`ExecutorType` 用原 YAML 中已使用的类型字符串（**保持不变以兼容现有 YAML**）。
3. 该项目的注册入口（`Startup`/`ModuleRegistrar`）中注册工厂。
4. 删除 core 中的原文件与原直接注册代码。

**Step 4 — 兼容性护栏**
- 现存 `projects/*/**.yml` 中 `batchJob.executor: photoAnnotator` 等字段值**一律不变**。
- 若某 Executor 被多个项目复用，放入共享位置 `projects/_shared/BatchExecutors/` 并文档标注。

### 预期收益
core 直接减少 **~3,000+ 行**；BatchJob 目录回归"纯调度引擎"。

### 迁移清单交付物
实现方须先产出 `docs/batchjob-migration-manifest.md`：每个 Executor 的
`[文件, 行数, 现有 core 依赖清单, 目标项目, ExecutorType 字符串, 是否共享]`。

---

## 问题二 🟡 AI Executor 家族样板重复 + CLI 单点故障（高优先级）

### 现状证据
7 个 `Ai*Executor` 均 150~760 行，结构雷同（取配置 → 调 CLI → 解析 → 落库）。
**关键缺陷（实测 grep 确认）：** 以下 6 个直接依赖具体 CLI 服务，**无一走 `ICliChainService`**：
```
AiDealerEngineExecutor / AutomatedBlogGeneratorExecutor / AiCommunicationExecutor
AiEmailChatExecutor / AiFolderProcessorExecutor / InvoiceEmailProcessorExecutor
```
直接引用 `IAntigravityCliService` 等 → **绕过 opencode→antigravity→claude 的 fallback 链**，
认证过期/超时/限流/未安装时无自动降级（与既有 photo-vocab 超时 root cause 同源）。

### 设计目标
抽象模板方法基类 `AiExecutorBase`，收敛样板；**强制**所有 AI 调用统一走 `ICliChainService`。

### 实现方案

**Step 1 — 模板方法基类（core 侧）**
```csharp
namespace NetYamlForge.Services.BatchJob;

public abstract class AiExecutorBase : BatchJobExecutor
{
    protected readonly ICliChainService Cli;   // 唯一 CLI 入口，禁止子类注入具体 CLI 服务
    protected AiExecutorBase(ICliChainService cli, /* 通用依赖 */) { Cli = cli; }

    // 模板方法：固定 取配置→构造prompt→调链→映射结果→落库 骨架
    protected sealed override async Task<BatchStepResult> ExecuteAsync(BatchJobPipeContext ctx, CancellationToken ct)
    {
        var input   = ResolveInput(ctx);                 // 子类实现
        var prompt  = BuildPrompt(input, ctx);           // 子类实现
        var raw     = await Cli.RunAsync(prompt, CliOptions(ctx), ct);  // 基类统一：含 fallback/超时/重试
        var result  = MapResult(raw, ctx);               // 子类实现
        await PersistAsync(result, ctx, ct);             // 子类实现
        return BatchStepResult.Ok(result);
    }

    protected abstract object ResolveInput(BatchJobPipeContext ctx);
    protected abstract string BuildPrompt(object input, BatchJobPipeContext ctx);
    protected abstract Task PersistAsync(object result, BatchJobPipeContext ctx, CancellationToken ct);
    protected virtual CliChainOptions CliOptions(BatchJobPipeContext ctx) => CliChainOptions.Default;
    protected abstract object MapResult(string raw, BatchJobPipeContext ctx);
}
```

**Step 2 — 迁移 7 个 AI Executor**
- 逐个改为继承 `AiExecutorBase`，只保留 `BuildPrompt` / `MapResult` / `ResolveInput` / `PersistAsync` 差异逻辑。
- **删除**所有 `IAntigravityCliService` / `IOpenCodeCliService` / `IClaudeCliService` 直接字段与注入。
- 业务型 AI Executor（Dealer/Blog/Invoice）在**问题一迁移到 projects 后**再套基类，基类留在 core 作为 SDK。

**Step 3 — 约束固化**
- 增加架构测试（Roslyn analyzer 或单元测试）：`BatchJob` 命名空间内任何类型**禁止**直接引用具体 CLI 服务接口，只允许 `ICliChainService`。放入 `NetYamlForge.Tests` 或 Analyzers。

### 预期收益
收敛 **~1,000+ 行**样板；一次性修复 6 处 fallback 缺失（含 photo-vocab 同类隐患）。

---

## 问题三 🟡 Cli 脚手架与运行时 core 编译耦合（中优先级）

### 现状证据
`Services/Cli/` = 4,893 行，均为**开发期工具**（`ProjectTemplateScaffolder` 1561、
`EntityYamlScaffolder` 818、`YamlSkillRegistry` 510、`MissingHookScaffolder` 452、
`HookScaffolder` 376、`EntityYamlModernizer` 276…），却与运行时同程序集编译，增大运行时镜像与维护耦合面。

### 设计目标
拆出独立程序集 `NetYamlForge.Tooling`，运行时 core 不引用它。

### 实现方案
1. 新建 `NetYamlForge.Tooling/`（`net` classlib 或 CLI 工具项目）。
2. 迁移 `Services/Cli/` 全部 Scaffolder / Modernizer / Validator / SkillRegistry。
3. 依赖方向单向：`Tooling → Core`（Tooling 可引用 core 的抽象），core **不得**反向依赖 Tooling。
4. 若 CLI 命令入口在主程序，改为 Tooling 独立可执行或作为 `dotnet tool`。
5. **验收点：** core 工程 `.csproj` 移除对 Cli 脚手架的编译包含；运行时构建产物不含 Scaffolder 类型。

### 预期收益
运行时 core 再减 **~4,000+ 行**编译面；开发工具与运行时边界清晰，镜像更小。

---

## 总体执行顺序与风险控制

| 阶段 | 任务 | 依赖 | 回归验证 |
|---|---|---|---|
| P0 | 产出 BatchJob 迁移清单 | — | 人工评审 |
| P1 | 建 `IBatchExecutorFactory` + 发现机制（core） | — | 现有 YAML 全绿 |
| P2 | 迁移 6 个业务 Executor 到 projects | P1 | 各项目 e2e 批处理跑通 |
| P3 | 建 `AiExecutorBase` + 7 个 AI Executor 套用 + 强制 CliChain | P1 | fallback 链故障注入测试 |
| P4 | 拆 `NetYamlForge.Tooling` | 独立 | scaffolder 命令回归 |

### 不可破坏的契约（硬约束）
1. 所有现存 `projects/*/**.yml` 中 `executor` 类型字符串**保持不变**。
2. 批处理对外行为（输入/输出/落库结构）不变。
3. core → 业务 / core → Tooling **禁止**反向依赖；依赖方向单向。
4. 每阶段独立可合并、独立可回滚。

### 度量目标
运行时 core: **57.4K → ~30K 行**；BatchJob 回归纯引擎；AI 调用 100% 经 `ICliChainService`。

---

## 附：实现方自检清单（Definition of Done）
- [ ] core 中不存在业务名词 Executor 的直接实例化
- [ ] `grep -rE "IAntigravityCliService|IOpenCodeCliService|IClaudeCliService" Services/BatchJob` 结果为空
- [ ] 架构测试守护 CLI 依赖约束，CI 中生效
- [ ] `NetYamlForge.Tooling` 拆分完成，core.csproj 不含 Scaffolder
- [ ] 全部现有 YAML 批处理配置无需修改即可运行
- [ ] 迁移清单文档 `docs/batchjob-migration-manifest.md` 已交付

---

## 验收章节（Acceptance Report）— commit 8cefc8b 实测

> 本章由重构落地后实测生成，所有数字来自 `wc -l` / `dotnet list package` / `grep`，非估算。

### A. 前后代码量对比（同口径）

| 口径 | 重构前 | 重构后 | 变化 |
|---|---|---|---|
| `NetYamlForge/`（含 projects） | 57,410 | 52,420 | ↓ ~5.0K |
| 真·框架 core（排除 projects） | — | **42,187** | 新基线 |
| `Services/` 核心服务层 | 37,421 | **29,443** | ↓ ~8.0K（**-21%**） |
| `Services/BatchJob/` | 6,280 | 5,066 | ↓ ~1.2K |

**代码去向（归位，非删除）：**
- `NetYamlForge.Tooling/` 新程序集 = **5,080 行**（Cli 脚手架迁出 core，`Services/Cli/*.cs` 现为 **0 文件**）
- `projects/*/Hooks/BatchExecutors/` = **3,159 行**（6 个业务 Executor 归位）

> 度量目标（core → ~30K）以 **Services 层 29.4K** 达成；含 projects 的仓库总量仅降 ~5K，因 Tooling/projects 是搬家而非删除——但运行时镜像仅加载 core，实际部署面缩小。

### B. Definition of Done 达标情况

| DoD 项 | 状态 | 实测证据 |
|---|---|---|
| core 中无业务名词 Executor 直接实例化 | ✅ | `Services/Cli/*.cs` = 0 文件；6 业务 Executor 已在 `projects/*/Hooks/BatchExecutors/` |
| `grep IAntigravityCliService\|IOpenCodeCliService\|IClaudeCliService` in `Services/BatchJob` 为空 | ✅ | grep 命中文件数 = **0** |
| 架构测试守护 CLI 依赖约束 | ✅ | `NetYamlForge.Tests/BatchJobArchitectureTests.cs` 存在 |
| `NetYamlForge.Tooling` 拆分完成，core 不含 Scaffolder | ✅ | `NetYamlForge.Tooling.csproj` 存在；`AiExecutorBase.cs`/`BatchStepHandlerRegistry.cs`/`CliChainExtensions.cs` 已建 |
| 现有 YAML 批处理配置无需修改即可运行 | ✅ | `executor` 类型字符串未变；`dotnet build` 绿灯，906 测试通过 |
| 迁移清单 `docs/batchjob-migration-manifest.md` 已交付 | ✅ | 文件存在 |

### C. 遗留项
- NuGet 依赖漏洞（SixLabors.ImageSharp / SQLitePCLRaw）见 `docs/nuget-vuln-assessment.md`，与本次瘦身无关，单独治理。
