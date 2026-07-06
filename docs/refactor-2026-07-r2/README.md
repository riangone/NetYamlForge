# NetYamlForge 优化改造 — 第二轮设计文档集（2026-07 · R2）

> 版本: v1.0 · 日期: 2026-07-06 · 面向对象: 负责代码实现的 AI / 工程师
> 前置基线: 第一轮 `docs/refactor-2026-07/`（结构复杂度治理）已基本落地——巨型类拆分、控制器瘦身、异常可观测性（`ForgeLogContext` 已入库）、方言契约测试、文档治理。
> 本轮**换维度**：从"结构复杂度"转向 **配置护栏 → 可观测性 → 项目侧 SDK 化 → 性能基线 → 安全回归**。

---

## 0. 与第一轮的关系（先读）

第一轮做的是"行为不变的纯结构重构"。本轮**部分项会新增运行时行为**（启动 fail-fast、新增 metric/trace、新增基线测试），因此**每个文档单独声明其行为影响**，不再统一套用"行为不变"总则。

**共用总则（所有实现必须遵守）：**
1. **小步提交**：每个文档 = 一个独立 PR/提交，可单独回滚，禁止跨文档大合并。
2. **复用既有资产，不另造轮子**：本轮多数项目已有部分基础设施（见下表"既有资产"列），实现必须先接续既有代码，再增量扩展。
3. **测试同行**：每个 PR `dotnet build NetYamlForge.slnx` + `dotnet test NetYamlForge.Tests` 必须全绿；新增能力必须带测试。
4. **配置可关闭**：新增的启动校验、可观测性导出等，必须支持通过配置（`appsettings.json` / 环境变量）关闭或降级，避免影响现有部署。
5. **命名空间**沿用就近原则，不引入无谓的新层级。

## 1. 度量基线（实测 2026-07-06）

| 维度 | 事实 | 位置 |
|------|------|------|
| JSON Schema 文件 | 已入库 4 份（entities/pages/project/dashboard），共 ~165KB | `docs/framework/schemas/*.schema.json` |
| Schema 校验器 | **已存在** `YamlSchemaValidator`（基于 `JsonSchema.Net` 8.0.0），但**是否在启动强制执行待确认/接线** | `NetYamlForge/Services/YamlSchemaValidator.cs` |
| 启动校验器 | 已有 `YamlConfigStartupValidator`（类型/Hook 引用检查，`IHostedService`），**只 Warn 不 Fail** | `NetYamlForge/Services/Validation/` |
| 可观测性 | 已有 `ForgeLogContext` + CorrelationId；**无 OpenTelemetry / metrics / tracing** | `NetYamlForge/Services/Diagnostics/` |
| 框架侧批处理基类 | 已有 `AiBatchExecutorBase` / `AiExecutorBase` | `NetYamlForge/Services/BatchJob/` |
| 项目侧 Hook 巨型文件 | `AutoDealerHooks.cs` 1031 / `PhotoAnnotatorExecutor.cs` 731 / `AiDealerEngineExecutor.cs` 610 / `InvoiceEmailProcessorExecutor.cs` 585 / `BizCardParserExecutor.cs` 551 …（**未继承任何共享基类**） | `NetYamlForge/projects/*/Hooks/` |
| 性能基线 | **无 BenchmarkDotNet**，无 benchmark 工程 | — |
| Analyzer | 已有 `NetYamlForge.Analyzers`（`ForbiddenPatternAnalyzer`） | `NetYamlForge.Analyzers/` |
| CI | 单一工作流 `build-and-test.yml` | `.github/workflows/` |

## 2. 文档清单与优先级

| # | 文档 | 主题 | 类型 | 性价比 | 风险 | 行为变化 |
|---|------|------|------|--------|------|----------|
| R2-01 | [01-schema-startup-and-ci-gate.md](01-schema-startup-and-ci-gate.md) | Schema 启动 fail-fast + CI lint 门禁 | 护栏 | ★★★ | 中 | 是（启动可失败） |
| R2-02 | [02-observability-otel.md](02-observability-otel.md) | OpenTelemetry Metrics/Tracing | 可观测性 | ★★★ | 中 | 是（新增导出） |
| R2-03 | [03-project-hooks-sdk.md](03-project-hooks-sdk.md) | 项目侧 Hook SDK 化 / 抽 BatchExecutorBase | 结构重构 | ★★ | 中 | 否 |
| R2-04 | [04-performance-baseline.md](04-performance-baseline.md) | 编译访问器缓存 + BenchmarkDotNet 基线 | 性能 | ★★ | 中 | 局部（缓存） |
| R2-05 | [05-security-regression-suite.md](05-security-regression-suite.md) | SQL 注入 / Hook 沙箱逃逸回归套件 | 安全测试 | ★★★ | 低 | 否 |

## 3. 建议实施顺序

1. **R2-05（安全回归套件）**——纯新增测试，零运行时风险，先建立对抗性用例的安全网。
2. **R2-01（Schema 门禁）**——先默认 Warn 模式灰度，再切 Fail；CI lint 立即见效。
3. **R2-04（性能基线）**——先落 benchmark 工程拿到数据，再做缓存优化（数据驱动决策）。
4. **R2-03（项目侧 SDK 化）**——逐项目独立 PR，风险可控。
5. **R2-02（OpenTelemetry）**——基础设施改动面最大，放最后，且默认关闭导出。

## 4. 每个 PR 的统一验收清单（Definition of Done）

- [ ] `dotnet build NetYamlForge.slnx` 无新增警告
- [ ] `dotnet test NetYamlForge.Tests` 全绿（现有 + 本次新增）
- [ ] 新增运行时行为均可通过配置关闭/降级，默认值不破坏现有部署
- [ ] PR 描述引用对应设计文档编号（如 `R2-01`）
- [ ] 若新增依赖（OTel / BenchmarkDotNet），在 PR 描述列出包与版本并说明许可证
