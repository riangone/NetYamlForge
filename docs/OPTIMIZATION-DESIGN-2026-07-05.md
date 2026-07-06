# NetYamlForge 下一轮优化：详细设计与实施说明

> 版本：v1.0 ｜ 日期：2026-07-05 ｜ 作者：Architect
> 前置基线：`CORE-SLIMMING-REFACTOR-DESIGN.md`（core-slimming + 单一职责拆类，commit 8cefc8b）
> 状态：设计稿（待评审 → 分批实施）

## 0. 背景与目标

上一轮完成了「核心瘦身重构」（AI Executor 统一到 `ICliChainService`、6 个业务 Executor 外迁到 `projects/*/Hooks/BatchExecutors`）。本轮聚焦**剩余的架构耦合、健壮性隐患与工程整洁度**。

代码现状（已核对，非估算）：

| 指标 | 实测值 | 来源 |
|------|--------|------|
| `PageController.cs` 行数 | **1154 行** | `wc -l` |
| 混入框架控制器的项目专属动作 | `SwitchProvider`(L869)、`AnnotatePhoto`(L923)、`EmbedPhoto`(L974) | `grep` |
| `catch (Exception` 出现次数 / 涉及文件 | **139 处 / 65 文件**（Services+Controllers） | `grep` |
| 真实阻塞调用 | `Services/Project/ProjectHookLoader.cs:587 @lock.Wait()` | `grep` |
| `*Executor.cs` 数量 | 15+（框架 8 + 项目 7） | `find` |
| 单个 Executor 体量 | `AiAnnotatorExecutor` 570 行 | `wc -l` |

**总目标**：降低框架/项目耦合、消除重复、收敛异常契约、整洁化仓库，并在重构窗口补齐关键路径单测。

---

## 优化项 #1（架构，最高优先）：PageController 项目专属动作下沉

### 1.1 问题
`PageController` 承担了通用页面 CRUD（Index/Section/Insert/Update/Delete/View），却硬编码了 `AnnotatePhoto`、`EmbedPhoto`、`SwitchProvider` 等 **photo-vocab / provider 切换专属动作**。框架层依赖了项目语义，违背「框架/项目解耦」原则，且导致新项目每加一个业务动作都要改框架控制器。

### 1.2 设计：动态动作路由（Dynamic Action Routing）
引入统一的「页面动作」扩展点，让项目专属动作通过 Hook 注册，框架控制器只保留通用 CRUD + 一个动作分发入口。

```
[Route] /p/{project}/{pageName}/action/{actionName}
        → PageActionDispatcher.Dispatch(context)
        → IPageActionHandler (按 project+actionName 解析)
             ├─ 框架内置 handler（通用）
             └─ projects/photo-vocab/Hooks/PageActions/AnnotatePhotoAction.cs
```

**核心契约（新增）**
```csharp
public interface IPageActionHandler
{
    string ActionName { get; }              // "annotate-photo"
    string? Project { get; }                // null = 全局；否则限定项目
    Task<IActionResult> HandleAsync(PageActionContext ctx);
}

public sealed record PageActionContext(
    string Project, string PageName,
    IReadOnlyDictionary<string,string?> Query,
    IServiceProvider Services, ClaimsPrincipal User);
```

**分发器**：`PageActionDispatcher` 在启动时通过既有 `ProjectHookLoader` 扫描注册所有 `IPageActionHandler`，以 `(Project, ActionName)` 建索引；请求到达时按项目优先、全局兜底解析。

### 1.3 迁移映射
| 现状（PageController 方法） | 目标位置 |
|---|---|
| `AnnotatePhoto` (L923) | `projects/photo-vocab/Hooks/PageActions/AnnotatePhotoAction.cs` |
| `EmbedPhoto` (L974) | `projects/photo-vocab/Hooks/PageActions/EmbedPhotoAction.cs` |
| `SwitchProvider` (L869) | 框架级 `Services/Ai/PageActions/SwitchProviderAction.cs`（provider 切换是框架能力，保留但从控制器抽出） |

### 1.4 兼容策略
- 旧路由 `/{project}/{pageName}/AnnotatePhoto?photo_id=` 保留 6 个月：控制器旧方法改为**薄转发**到 dispatcher，标 `[Obsolete]`，日志打印弃用告警。
- 前端调用统一切到 `/action/{name}` 新路由。

### 1.5 验收
- `PageController.cs` 降到 ~800 行以内，`grep` 不再出现项目专属方法名。
- photo-vocab 标注/嵌入端到端可用（回归：走 `/verify`）。

---

## 优化项 #2（架构）：`AiBatchExecutorBase` 模板方法基类

### 2.1 问题
`AiAnnotatorExecutor`(570)、`PhotoAnnotatorExecutor`、`BizCardParserExecutor`、`AiDealerEngineExecutor`、`AiEmbeddingGeneratorExecutor` 等呈现同一骨架：
**取数据 → 构造 prompt → 调 CLI/AI 链（含超时+fallback）→ 解析结果 → 写回实体 → 更新批次状态 → 异常归一**。骨架重复数百行。

### 2.2 设计：模板方法 + 钩子
```csharp
public abstract class AiBatchExecutorBase<TInput, TResult> : IBatchJobExecutor
{
    protected readonly ICliChainService Cli;
    protected readonly ILogger Logger;

    // 模板方法：固化流程、超时、fallback、状态回写、异常归一
    public async Task<BatchItemResult> ExecuteItemAsync(BatchItemContext ctx, CancellationToken ct)
    {
        try
        {
            var input  = await LoadInputAsync(ctx, ct);
            var prompt = BuildPrompt(input);
            var raw    = await Cli.RunChainAsync(prompt, Options, ct); // 统一超时/fallback
            var result = ParseResult(raw);
            await PersistAsync(ctx, input, result, ct);
            return BatchItemResult.Ok();
        }
        catch (OperationCanceledException) { throw; }             // 不吞取消
        catch (AiChainExhaustedException ex) { return Fail(ctx, CommandErrorCodes.AiUnavailable, ex); }
        catch (Exception ex) { return Fail(ctx, CommandErrorCodes.ExecutorError, ex); } // 唯一兜底 + 日志
    }

    // 子类只实现"业务差异"
    protected abstract Task<TInput> LoadInputAsync(BatchItemContext ctx, CancellationToken ct);
    protected abstract string BuildPrompt(TInput input);
    protected abstract TResult ParseResult(string raw);
    protected abstract Task PersistAsync(BatchItemContext ctx, TInput input, TResult result, CancellationToken ct);
    protected virtual CliChainOptions Options => CliChainOptions.Default;
}
```

### 2.3 迁移顺序（低风险优先）
1. `AiEmbeddingGeneratorExecutor`（逻辑最简，作为试点）
2. `AiAnnotatorExecutor` → 派生 `AiBatchExecutorBase<AnnotateInput, AnnotateResult>`
3. `PhotoAnnotatorExecutor` / `BizCardParserExecutor` / `AiDealerEngineExecutor`

每个迁移**单独一个 commit + 单独回归**，保证可 revert。

### 2.4 验收
- 每个迁移后的 Executor 净删除 ≥ 40% 行数。
- 超时/fallback/状态回写行为与迁移前一致（对比日志与批次结果表）。

---

## 优化项 #3（健壮性）：收敛 139 处 `catch (Exception)`

### 3.1 问题
139 处宽泛捕获（65 文件），风险：**吞异常（无日志、无重抛）掩盖真实错误**、`OperationCanceledException` 被错误吞掉破坏取消语义。

### 3.2 分类处置准则
| 类别 | 判定 | 处理 |
|---|---|---|
| A. 静默吞掉 | catch 块空 / 只 return null | **必须修**：加日志 + 返回 `CommandResult.Fail(code)` 或重抛 |
| B. 吞掉取消 | 未先 catch `OperationCanceledException` | 在宽捕获前补 `catch (OperationCanceledException) { throw; }` |
| C. 边界兜底 | 顶层请求/批次边界 | 保留，但强制 `Logger.LogError(ex, ...)` + 归一到 `CommandErrorCodes` |
| D. 可窄化 | 实为 `IOException`/`JsonException` 等 | 缩小到具体异常类型 |

### 3.3 统一错误契约
复用现有 `CommandResult` + `CommandErrorCodes`。新增 lint 约束（`.editorconfig` 或 Roslyn analyzer）：`catch (Exception)` 块内若无 `Log*` 调用则告警。

### 3.4 执行方式
按文件批量审查，**每 PR 收敛一个子系统**（如先 BatchJob、再 Api、再 DynamicEntity），避免大 diff。

---

## 优化项 #4（健壮性）：消除阻塞调用

### 4.1 现状
真实阻塞点为 `Services/Project/ProjectHookLoader.cs:587 @lock.Wait()`（`SemaphoreSlim` 同步等待）。

### 4.2 处置
- 若调用链本身在异步上下文：改 `await @lock.WaitAsync(ct)`，并确保 `finally { @lock.Release(); }`。
- 若为启动期一次性同步初始化（非请求线程）：可保留，但补注释说明「仅启动期、非请求线程，无饥饿风险」。
- 复查 `.Result`/`.GetAwaiter().GetResult()` 是否还有隐藏点（本次 grep 未见于请求路径，但迁移中需持续守护）。

---

## 优化项 #5（工程整洁）：仓库运行时产物归置

### 5.1 现状
根目录混杂运行时产物：`system.db`、`chinook.db`、`netyamlforge.pid`、`startup.log`，以及 `bin/`(205M)、`obj/`(30M)、`logs/`(40M)、`projects/`(237M)。bin/obj/db 已确认**未被 git 跟踪** ✅，但物理混杂影响可维护性。

### 5.2 设计
```
var/                # 运行时可变数据（gitignore）
 ├─ data/           # system.db, chinook.db
 ├─ run/            # netyamlforge.pid
 └─ log/            # startup.log, logs/*
```
- 通过配置项（`appsettings.json` 的 `Paths:Data/Run/Log`）集中定义，代码读配置而非硬编码根路径。
- 更新 `.gitignore`、启动脚本、`Dockerfile`/compose 卷映射（交由 DevOps 角色复核）。

### 5.3 注意
- **不改 git 历史**；仅移动物理文件 + 改配置。
- 迁移需保证现有数据库文件平滑迁移（启动时兼容旧路径回退一次）。

---

## 优化项 #6（质量）：关键路径单测补齐

### 6.1 现状
核心约 5.3 万行 C#，仅约 69 个测试文件，安全/数据关键路径覆盖偏薄。

### 6.2 优先补测清单
| 目标 | 理由 | 用例要点 |
|---|---|---|
| `DynamicCrudRepository` | 数据主干 | CRUD、分页、事务、并发写 |
| `SqlExpressionParser` | 注入面 | 合法/非法表达式、边界、转义 |
| `SqlSafetyGuard` | 安全护栏 | 黑白名单、危险关键字拦截 |
| `AiBatchExecutorBase`（#2 产出） | 新基类 | 超时、fallback 耗尽、异常归一、状态回写 |
| `PageActionDispatcher`（#1 产出） | 新路由 | 项目优先解析、全局兜底、未知动作 404 |

目标：关键路径行覆盖率 ≥ 70%。

---

## 实施路线图（分批、可回归）

| 阶段 | 内容 | 风险 | 依赖 |
|---|---|---|---|
| **P1** | #2 Executor 基类抽取（试点 → 全量迁移） | 低 | — |
| **P2** | #1 PageController 动作下沉 + 动态路由 | 中 | 无（可与 P1 并行） |
| **P3** | #6 为 P1/P2 新构件补单测 | 低 | P1、P2 |
| **P4** | #3 分子系统收敛 catch(Exception) | 中 | P3（有测试托底） |
| **P5** | #4 阻塞调用消除 + #5 仓库整洁 | 低 | — |

**推荐起点**：P1（收益大、风险低、天然可回归）。

## 回滚与验证
- 每项独立 commit / PR，均可单独 revert。
- 每阶段完成后走 `/verify` 驱动实际业务流（photo-vocab 标注、批处理任务、通用 CRUD 页面）观察行为，而非仅跑单测。
- 变更涉及 Docker/卷映射（#5）时请 DevOps 角色复核。

---
*本文档为设计稿。落地某一项前，建议先就该项出「改动清单（文件级 diff 计划）」再动代码。*
