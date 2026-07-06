# 06 — 异常处理可观测性统一约定

> 范围: `NetYamlForge/Services/**`（实测 169 处 catch，部分为裸 `catch` 无结构化日志）
> 类型: 增强（新增日志字段，不改控制流） · 风险: 低 · 依赖: 建议先于 01/02/05

## 1. 现状分析（实测）

- Services 下 **169 处 catch**，核心路径无"空吞异常"，但**存在裸 `catch`**（无 `ex`、无日志），例如：
  - `Services/HomePageConfigProvider.cs:71` — `catch`（无捕获变量）
  - `Services/PdfFontLoader.cs` — 89/124/153/223/266/301 多处裸 `catch`
- 已带日志的 catch **字段不统一**：有的记 `ex.Message`，有的记完整 `ex`，缺少 `projectId / entity / hook / correlationId` 等定位维度。
- 对低代码框架，排障关键是能回答："**哪个项目 / 哪个 YAML 实体 / 哪个 Hook** 出的错"。当前难以从日志直接定位。

## 2. 目标：统一 catch→日志约定 + 关联 ID

### 2.1 引入 `LogScope` 关联上下文
新增 `Services/Diagnostics/ForgeLogContext.cs`：
```csharp
public static class ForgeLog {
    // 统一 EventId 段位，便于按类别检索
    public static readonly EventId HookFailure   = new(4100, "HookFailure");
    public static readonly EventId CompileFailure= new(4101, "CompileFailure");
    public static readonly EventId EntityIoFailure = new(4102, "EntityIoFailure");
    // ...

    // 统一结构化字段的 scope
    public static IDisposable? BeginScope(this ILogger logger,
        string? projectId = null, string? entity = null, string? hook = null,
        string? correlationId = null)
        => logger.BeginScope(new Dictionary<string, object?> {
            ["ProjectId"] = projectId,
            ["Entity"] = entity,
            ["Hook"] = hook,
            ["CorrelationId"] = correlationId ?? Activity.Current?.TraceId.ToString(),
        });
}
```
> 复用现有 `RequestTraceMiddleware`（测试存在 `RequestTraceMiddlewareTests`）产生的 trace/correlation id，而非另造一套。核对其把 correlation id 放在哪（`HttpContext.Items` / `Activity`），统一取用。

### 2.2 统一 catch 写法约定（写入团队规范）
```csharp
try { ... }
catch (Exception ex) {
    // 1) 必须带捕获变量 ex（禁止裸 catch，除非注释说明"必须吞并原因"）
    // 2) 必须结构化日志，带定位维度
    _logger.LogError(ForgeLog.HookFailure, ex,
        "Hook execution failed for {Hook} on {Entity} in {ProjectId}",
        hookName, entityName, projectId);
    // 3) 明确控制流：rethrow / 返回失败结果 / 降级——三选一，不得静默继续
    throw; // 或 return CommandResult.Fail(...)
}
```

### 2.3 裸 catch 处置（逐个决策，不批量改控制流）
| 场景 | 处置 |
|------|------|
| 可忽略的清理/尽力而为（如字体加载 fallback） | 保留吞并，但**补 `catch (Exception ex)` + `_logger.LogDebug/LogWarning`** 并加注释说明为何可忽略 |
| 配置/加载失败被静默 | 升级为 `LogWarning`/`LogError` + 结构化字段 |
| 核心路径 | 必须 rethrow 或返回明确失败结果 |

## 3. 落地范围与顺序（分批，避免巨型 PR）

1. **PR-1**：新增 `ForgeLog` helper + EventId 目录 + 规范文档，**不改任何 catch**。
2. **PR-2**：修所有**裸 catch**（`grep -rnE "catch\s*(\{|$)" Services --include=*.cs` 逐个过），补捕获变量 + 日志 + 注释。
3. **PR-3**：给核心链路（Hook 执行、编译、实体 IO）的 catch 接入 `BeginScope` 与统一 EventId。非核心链路可后续增量接入。

> **不要求**一次性改完 169 处。优先"裸 catch + 核心链路"，其余作为约定，新代码遵守即可。

## 4. Analyzer 护栏（可选，强烈建议）

项目已有 `NetYamlForge.Analyzers`。新增一条 Roslyn analyzer 规则：
- `NYF-EXC-001`：Services 命名空间内的 `catch` 块若**无捕获变量且无日志调用**→ Warning。
- 使 CI 拦截未来回归（把"约定"变"强制"）。

## 5. 验收标准

- [ ] `Services/**` 无裸 `catch`（有意吞并者带 `ex` + 日志 + 注释）
- [ ] 核心链路（Hook/编译/实体 IO）异常日志含 `ProjectId/Entity/Hook/CorrelationId`
- [ ] `ForgeLog` EventId 目录建立，日志可按类别检索
- [ ] （可选）Analyzer NYF-EXC-001 上线，CI 生效
- [ ] 控制流零变化（除裸 catch 补日志外），现有测试全绿
