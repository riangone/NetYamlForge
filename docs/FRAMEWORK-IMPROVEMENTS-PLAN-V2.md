# NetYamlForge 框架改进计划 V2

> 作成日: 2026-06-03  
> 审计分支: `nyf`  
> 审计范围: `Services/AI/`, `Services/BatchJob/`, `Services/Connection/`

---

## 问题总览

| # | 问题 | 优先级 | 状态 |
|---|------|--------|------|
| 1 | FSM 状态纯内存存储，服务重启丢失 | 高 | **待实现** |
| 2 | AiToolOrchestrator Tool 执行逻辑空实现 | 高 | **待实现** |
| 3 | BatchJobExecutor switch 硬编码，IBatchStepHandler 未接入 | 中高 | **待实现** |
| 4 | `Services/Ai/` 目录名与命名空间 `Services.AI` 不一致 | 低 | **待实现** |
| 5 | 自定义 ConnectionPool 与驱动原生连接池重叠 | 中 | **待实现** |
| 6 | `using Stateless.Graph` 存在但 API 调用已注释，编译警告 | 低 | **待实现** |
| 7 | AI 场景（FSM/Slot/Tool权限）配置未 YAML 驱动 | 架构 | **待实现** |

---

## Issue 1: FSM 状态持久化

### 现状
`SlotFillingManager._fsmStates` 是 `ConcurrentDictionary<string, AppointmentStateMachine>`，纯内存。

```csharp
// Services/AI/SlotFillingManager.cs:140
private readonly ConcurrentDictionary<string, AppointmentStateMachine> _fsmStates = new();

// GetCurrentFsmStateAsync: 纯内存读取，不查 DB
public Task<string?> GetCurrentFsmStateAsync(string conversationId)
{
    var fsm = GetOrCreateFsm(conversationId); // always creates new if not in memory
    return Task.FromResult<string?>(fsm.CurrentState.ToString());
}
```

### DB 列已存在
`ai_conversations` 表已有以下列（通过 DDL 确认）：
- `current_state TEXT DEFAULT 'init'`  
- `low_confidence_count INTEGER DEFAULT 0`
- `context_data TEXT`（slot_sessions 已存）

### 解决方案

**修改 `GetOrCreateFsm` 改为 `GetOrRestoreFsmAsync`**：
```csharp
private async Task<AppointmentStateMachine> GetOrRestoreFsmAsync(string conversationId, string? projectId)
{
    if (_fsmStates.TryGetValue(conversationId, out var fsm))
        return fsm;

    // 从 DB 恢复状态
    var row = await LoadFsmRowAsync(conversationId, projectId);
    var restoredState = ParseState(row?.CurrentState) ?? AppointmentStateMachine.State.Init;
    var newFsm = new AppointmentStateMachine(conversationId, restoredState);
    _fsmStates.TryAdd(conversationId, newFsm);
    return newFsm;
}
```

**修改 `UpdateFsmStateAsync`** 在内存更新后同步写 DB：
```csharp
await WithDbAsync(projectId, async db =>
{
    await db.ExecuteAsync(@"
        UPDATE ai_conversations
        SET current_state = @State,
            low_confidence_count = @Count,
            updated_at = @Now
        WHERE conversation_id = @Id",
        new {
            State = fsm.CurrentState.ToString().ToLowerInvariant().Replace("collect", "collect_"),
            Count = fsm.LowConfidenceCount,
            Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Id = conversationId
        });
});
```

**修改 `AppointmentStateMachine` 构造函数** 支持从指定状态恢复：
```csharp
public AppointmentStateMachine(string conversationId, State initialState = State.Init)
{
    _machine = new StateMachine<State, Trigger>(initialState); // 支持恢复
    ConfigureTransitions();
}
```

### 需修改文件
- `Services/AI/SlotFillingManager.cs` — GetOrCreateFsm → GetOrRestoreFsmAsync, UpdateFsmStateAsync 写 DB
- `Services/AI/AppointmentStateMachine.cs` — 构造函数增加 initialState 参数

---

## Issue 2: AiToolOrchestrator Tool 执行逻辑

### 现状
```csharp
// AiToolOrchestrator.cs:150-153
// [4] 执行 Tool (TODO: 这里需要集成到实际的 Tool 执行逻辑)
result.IsSuccess = true;
result.Data = null; // TODO: 实际的 Tool 执行结果
```

`ToolDefinition` 已有 `ExecuteAsync Func<JsonNode, Task<ToolCallResult>>?`，但没有注册表。

### 解决方案

**新增 `IToolRegistry` 接口**：
```csharp
public interface IToolRegistry
{
    void Register(ToolDefinition tool);
    ToolDefinition? Get(string toolName);
    IReadOnlyCollection<ToolDefinition> GetAll();
}
```

**实现 `AutoDealerToolRegistry`** 注册 auto-dealer 场景的 Tool：
```csharp
public class AutoDealerToolRegistry : IToolRegistry
{
    // 注册 query_data → DynamicCrudRepository.GetAllAsync
    // 注册 create_appointment_request → service_appointments CREATE
}
```

**修改 `AiToolOrchestrator.ValidateAndExecuteToolAsync`**：
```csharp
// [4] 通过 IToolRegistry 查找并执行 Tool
var toolDef = _toolRegistry.Get(toolName);
if (toolDef?.ExecuteAsync != null)
{
    var toolResult = await toolDef.ExecuteAsync(toolCall);
    result.IsSuccess = toolResult.IsSuccess;
    result.Data = toolResult.Data;
    result.ErrorMessage = toolResult.ErrorMessage;
}
```

### 需新增/修改文件
- `Services/AI/ToolValidation/IToolRegistry.cs` — 新增接口
- `Services/AI/ToolValidation/InMemoryToolRegistry.cs` — 注册表实现
- `Services/AI/AiToolOrchestrator.cs` — 注入并调用 IToolRegistry

---

## Issue 3: BatchJobExecutor → IBatchStepHandler 重构

### 现状
`IBatchStepHandler` 接口已定义（`Services/BatchJob/IBatchStepHandler.cs`），但：
1. 现有 Executor 类（`AiDealerEngineExecutor`、`EmailFetchExecutor` 等）**未实现** `IBatchStepHandler`
2. `BatchJobExecutor` 仍使用 `switch(job.Type)` 硬编码路由
3. `IBatchStepHandler` 在整个代码库中**零使用**

### 解决方案

**Step A: 让各 Executor 实现 `IBatchStepHandler`**
```csharp
// AiDealerEngineExecutor.cs
public class AiDealerEngineExecutor : IBatchStepHandler
{
    public string StepType => "ai_dealer_engine";
    
    public Task ExecuteAsync(BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, BatchJobResult result, CancellationToken ct)
    {
        // 现有逻辑移入此处
    }
}
```

**Step B: 新增标准 SQL Handler（`SqlStepHandler`）**
```csharp
public class SqlToCsvHandler : IBatchStepHandler
{
    public string StepType => "sql_to_csv";
    // 从 BatchJobExecutor.ExecuteSqlToCsvAsync 迁移
}
```

**Step C: 替换 `BatchJobExecutor` 中的 switch**
```csharp
public class BatchJobExecutor : IBatchJobExecutor
{
    private readonly IReadOnlyDictionary<string, IBatchStepHandler> _handlers;
    
    public BatchJobExecutor(IEnumerable<IBatchStepHandler> handlers, ...)
    {
        _handlers = handlers.ToDictionary(h => h.StepType, StringComparer.OrdinalIgnoreCase);
    }
    
    public async Task<BatchJobResult> ExecuteAsync(BatchJobDefinition job, ...)
    {
        if (!_handlers.TryGetValue(job.Type, out var handler))
            throw new NotSupportedException($"Unsupported job type: {job.Type}");
        await handler.ExecuteAsync(job, projectName, db, tx, result, ct);
    }
}
```

**Step D: DI 注册**
```csharp
// Program.cs
services.AddScoped<IBatchStepHandler, SqlToCsvHandler>();
services.AddScoped<IBatchStepHandler, SqlCommandHandler>();
services.AddScoped<IBatchStepHandler, StoredProcedureHandler>();
services.AddScoped<IBatchStepHandler, AiDealerEngineExecutor>();
services.AddScoped<IBatchStepHandler, EmailFetchExecutor>();
// ...
```

### 需修改文件
- `Services/BatchJob/AiDealerEngineExecutor.cs` — 实现接口
- `Services/BatchJob/EmailFetchExecutor.cs` — 实现接口
- `Services/BatchJob/AiCommunicationExecutor.cs` — 实现接口
- `Services/BatchJob/ChinaStockBriefingExecutor.cs` — 实现接口
- `Services/BatchJob/InvoiceEmailProcessorExecutor.cs` — 实现接口
- `Services/BatchJob/AutomatedBlogGeneratorExecutor.cs` — 实现接口
- `Services/BatchJob/BatchJobExecutor.cs` — 用 handler 字典替换 switch
- `Services/BatchJob/SqlToCsvHandler.cs` — 新增（从 BatchJobExecutor 迁移）
- `Services/BatchJob/SqlCommandHandler.cs` — 新增
- `Services/BatchJob/StoredProcedureHandler.cs` — 新增

---

## Issue 4: Services/Ai/ 目录名与命名空间不一致

### 现状
- 目录：`Services/Ai/`（小写 i）
- 命名空间：`NetYamlForge.Services.AI`（已修正为大写）
- .NET 不感知目录名（不影响编译），但造成视觉混乱

### 解决方案
```bash
git mv NetYamlForge/Services/Ai NetYamlForge/Services/AI_dir
# 重命名后再还原为 AI
```

### 需修改
- 移动 `Services/Ai/IGeminiCliService.cs` → `Services/AI/IGeminiCliService.cs`
- 移动 `Services/Ai/GeminiCliService.cs` → `Services/AI/GeminiCliService.cs`

---

## Issue 5: 双层连接池

### 现状
- `ConnectionPool.cs`：自定义连接池（MaxPoolSize/IdleTimeout/MaxLifetime）
- Npgsql/MySql.Data/SqlClient 均有成熟内置连接池
- `LazyDbConnection.cs`（新增）：惰性加载，解决 Sync-over-Async，但不解决双层池问题

### 解决方案
**方案 A（推荐）**: 禁用内置池（Pooling=false in connection strings），保留自定义池 + LazyDbConnection  
**方案 B**: 移除 `ConnectionPool.cs`，改为纯 LazyDbConnection + 驱动原生池

推荐方案 B：
```csharp
// ConnectionManager.cs - 修改 CreateConnectionAsync
// 不再持有连接对象，每次调用直接通过驱动原生池获取连接
public async Task<IDbConnection> CreateConnectionAsync(string? projectName, ...)
{
    var connStr = ResolveConnectionString(projectName);
    return new LazyDbConnection(() => CreateNativeConnection(connStr));
}
```

在连接字符串中确保 `Pooling=True`（默认已启用）。

### 需修改文件
- `Services/Connection/ConnectionManager.cs` — 移除对 `ConnectionPool` 的使用
- `Services/Connection/ConnectionPool.cs` — 标记为 Obsolete 或删除

---

## Issue 6: Stateless.Graph 编译警告

### 现状
```csharp
// AppointmentStateMachine.cs:2
using Stateless.Graph; // 未使用，编译器 CS8019 警告

// line 238-239
// TODO: 需要安装 Stateless.Graph 包
// return UmlDotGraph.Format(_machine.GetGraph());
return $"Current State: {_machine.State}";
```

Stateless 5.20.1 中 `GetInfo()` 返回 `StateMachineInfo`，`UmlDotGraph.Format()` 在同包中可用。

### 解决方案
```csharp
// 移除 using Stateless.Graph（若不使用）
// OR 实现为：
public string GenerateStateDiagram()
{
    var info = _machine.GetInfo();
    return UmlDotGraph.Format(info); // Stateless 5.x 正确 API
}
```

### 需修改文件
- `Services/AI/AppointmentStateMachine.cs` — 修复 using + 实现 GenerateStateDiagram

---

## Issue 7: AI 场景配置 YAML 驱动

### 现状
所有 AI 场景相关配置硬编码在 C# 中：
- `SlotFillingManager.cs:146-200` — 场景与槽位定义（test_drive、estimate、appointment_service...）
- `ToolCallValidator.cs:138-182` — Entity/Action 白名单
- `ToolCallValidator.cs:284-294` — 状态-Tool 映射（allowedTools by FSM state）
- `AppointmentStateMachine.cs` — FSM 状态与触发器定义

### 目标 YAML 格式
```yaml
# projects/auto-dealer-demo/ai/scenarios.yaml
scenarios:
  test_drive:
    description: "試乗予約"
    initial_state: Init
    slots:
      required:
        - name: vehicle_model
          prompt: "どの車種の試乗をご希望ですか？"
        - name: preferred_date
          prompt: "ご希望の日付を教えてください"
    fsm:
      states:
        - Init
        - CollectVehicle
        - Confirming
        - Booked
        - Escalate
      transitions:
        - from: Init
          trigger: VehicleProvided
          to: CollectVehicle
    tools:
      Init: [query_data]
      CollectVehicle: [query_data]
      Confirming: [create_appointment_request]
  
  allowed_entities:
    - vehicles
    - sales_leads
    - customers
```

### 实施路径
1. 新增 `Services/AI/AiScenarioConfig.cs` — 配置 POCO
2. 新增 `Services/AI/AiScenarioYamlLoader.cs` — YAML 解析与缓存
3. 修改 `SlotFillingManager` — 从 `AiScenarioYamlLoader` 加载场景
4. 修改 `ToolCallValidator` — 从 YAML 读取白名单与状态-Tool 映射

### 需新增文件
- `Services/AI/AiScenarioConfig.cs`
- `Services/AI/AiScenarioYamlLoader.cs`
- `projects/auto-dealer-demo/ai/scenarios.yaml`（示例配置）

---

## 实施顺序建议

```
第一轮（功能完整性）:
  Issue 6 → Issue 4 → Issue 1 → Issue 2

第二轮（架构重构）:
  Issue 3 → Issue 5

第三轮（演进升级）:
  Issue 7
```
