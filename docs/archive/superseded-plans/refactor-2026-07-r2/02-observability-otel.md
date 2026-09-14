# R2-02 — OpenTelemetry Metrics / Tracing

> 范围: `Program.cs` / `ServiceCollectionExtensions` / DynamicEntity / BatchJob / Hook 加载(ALC) / AI-CLI 链路
> 类型: 可观测性增强 · 风险: 中（新增依赖 + 导出管道） · 依赖: 建议在第一轮 06（`ForgeLogContext`/CorrelationId）之后
> **行为变化: 是** — 新增 metrics/traces 采集与导出；**默认关闭 exporter**，仅在配置开启时激活。

## 1. 现状（实测）

- 已有 `Services/Diagnostics/ForgeLogContext.cs` + CorrelationId（第一轮 06 落地），日志侧已结构化。
- **无任何 OpenTelemetry 依赖**（`grep OpenTelemetry *.csproj` 为空），无 metrics、无分布式 trace。
- 排障目前只能靠日志 + CorrelationId 串联，缺少**时延分布 / 吞吐 / 队列深度**等量化指标。

## 2. 目标

用 OpenTelemetry 把已有的 CorrelationId 升级为标准 **trace**，并补齐关键 **metric**。核心原则：**接续现有 `Activity.Current` / CorrelationId，不另造上下文**。

关键遥测点（低代码框架排障最需要的）：

| 领域 | Span (Activity) | Metric |
|------|-----------------|--------|
| 动态实体查询 | `forge.entity.query`（tag: project, entity, dialect） | `forge.entity.query.duration`（Histogram, ms）、`forge.entity.query.rows`（Histogram） |
| Hook 编译/加载(ALC) | `forge.hook.compile` / `forge.hook.load` | `forge.hook.compile.duration`、`forge.hook.compile.errors`（Counter） |
| 批处理执行 | `forge.batch.execute`（tag: job, step） | `forge.batch.queue.depth`（ObservableGauge）、`forge.batch.step.duration`、`forge.batch.failures`（Counter） |
| AI / CLI 链路 | `forge.ai.cli`（tag: provider, model, fallbackUsed） | `forge.ai.cli.duration`、`forge.ai.cli.fallback`（Counter）、`forge.ai.tokens`（Counter） |

## 3. 设计

### 3.1 依赖（版本以实现时最新稳定为准）
```
OpenTelemetry.Extensions.Hosting
OpenTelemetry.Instrumentation.AspNetCore
OpenTelemetry.Instrumentation.Http
OpenTelemetry.Exporter.OpenTelemetryProtocol   // OTLP
OpenTelemetry.Exporter.Console                  // 本地调试用
```
> 只引入 OTLP + Console exporter，避免绑死某家后端（Jaeger/Prometheus 由 collector 侧转换）。

### 3.2 统一插桩常量 `ForgeTelemetry`
新增 `Services/Diagnostics/ForgeTelemetry.cs`——**单一** `ActivitySource` 与 `Meter`，全框架复用：
```csharp
public static class ForgeTelemetry
{
    public const string ServiceName = "NetYamlForge";
    public static readonly ActivitySource Source = new(ServiceName, AssemblyVersion);
    public static readonly Meter Meter = new(ServiceName, AssemblyVersion);

    // Histograms / Counters 在此集中声明，禁止各处 new Meter
    public static readonly Histogram<double> EntityQueryDuration =
        Meter.CreateHistogram<double>("forge.entity.query.duration", unit: "ms");
    // ... 其余指标同上集中声明
}
```
> 规则：**全框架只有这一个 `ActivitySource` 和一个 `Meter`**。span/metric 通过 tag 区分领域，避免 source 爆炸。

### 3.3 配置（默认关闭导出）
```jsonc
"Forge": {
  "Telemetry": {
    "Enabled": false,               // 总开关，默认 false（不改现有部署）
    "Exporter": "Otlp",             // Otlp | Console | None
    "OtlpEndpoint": "http://localhost:4317",
    "SampleRatio": 1.0,             // trace 采样率
    "Metrics": true,
    "Tracing": true
  }
}
```
`Enabled=false` 时：**完全不注册 OTel provider**，`ActivitySource`/`Meter` 无监听者时开销近零（可安全保留插桩代码）。

### 3.4 注册（`ServiceCollectionExtensions`）
新增 `AddForgeTelemetry(this IServiceCollection, IConfiguration)`：
- 读 `Forge:Telemetry`；`Enabled=false` 直接 return。
- `AddOpenTelemetry().WithTracing(...).WithMetrics(...)`：
  - Tracing：`AddSource(ForgeTelemetry.ServiceName)` + ASP.NET Core + HttpClient instrumentation + 采样器。
  - Metrics：`AddMeter(ForgeTelemetry.ServiceName)` + runtime/aspnetcore instrumentation。
  - Exporter 按配置切换。
- Resource 属性：`service.name`, `service.version`, 部署环境。

### 3.5 插桩点接入（薄封装，勿侵入业务）
每个领域用 `using var act = ForgeTelemetry.Source.StartActivity("forge.entity.query");` 包裹，
并在既有 catch（第一轮 06 已结构化）里 `act?.SetStatus(ActivityStatusCode.Error)` + `RecordException`。
- **DynamicEntity**：在查询命令服务入口包 span，结束记录 duration + rows。
- **Hook 编译/ALC**：在 `ProjectHookLoader`（第一轮已拆分）编译入口包 span，失败计数。
- **BatchJob**：`BatchJobExecutor` 每 step 包 span；`forge.batch.queue.depth` 用 `ObservableGauge` 回调读队列长度。
- **AI-CLI**：在 CLI 链路服务（`ICliChainService`）入口包 span，`fallbackUsed` 作为 tag + counter。

> **CorrelationId 对齐**：第一轮把 correlation id 放在 `Activity.Current?.TraceId`（见 06 文档 `ForgeLog.BeginScope`）。OTel 接管后 `Activity.Current` 天然存在，日志 scope 与 trace **自动同源**，无需改日志侧。核对 `RequestTraceMiddleware` 是否覆盖/替换了 `Activity`，避免双写。

## 4. 落地顺序（分 PR）

1. **PR-1**：加依赖 + `ForgeTelemetry` 常量 + `AddForgeTelemetry`（默认关闭）。此时无任何 span，仅管道就绪，验证 `Enabled=false` 零影响。
2. **PR-2**：DynamicEntity + BatchJob 插桩（最高价值）。
3. **PR-3**：Hook 编译/ALC + AI-CLI 插桩。
4. **PR-4**：文档——如何用 Console exporter 本地看 trace、如何接 OTLP collector。

## 5. 边界与风险

- **性能开销**：`Enabled=false` 时几乎为零；开启后 histogram/tag 基数需控制——**tag 值禁止用高基数字段**（如 recordId、用户输入）；project/entity/dialect/provider 这类有限枚举才可做 tag。
- **不重复造 CorrelationId**：严禁新增第二套 trace id 体系，一律走 `Activity`。
- **敏感数据**：span/metric tag 中**不得**包含 PII、SQL 明文、AI prompt 全文（可截断/哈希）。

## 6. 验收标准

- [ ] `Forge:Telemetry:Enabled=false`（默认）时无 OTel provider 注册，基准无可测退化
- [ ] 开启 Console exporter 后可见 `forge.entity.query` / `forge.batch.execute` 等 span，且带 project/entity tag
- [ ] 四类核心 metric 可导出并有非零样本
- [ ] trace id 与第一轮日志 scope 的 CorrelationId 同源（同一请求可交叉检索）
- [ ] tag 基数审查通过（无高基数字段），无 PII/SQL 明文/prompt 全文
- [ ] 现有测试全绿
