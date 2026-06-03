# NetYamlForge 框架改进实现计划

> 生成日期：2026-06-03  
> 负责人：Hyperion (架构师)  
> 状态：**待实现**

---

## 概览：7 个待修复问题

| # | 问题 | 优先级 | 预计工作量 |
|---|---|---|---|
| 1 | AI FSM 与 AiToolOrchestrator 硬编码耦合 | 高 | M |
| 2 | FSM 状态不持久化（服务重启丢失会话） | 高 | M |
| 3 | BatchJobExecutor 构造函数注入 8 个依赖 | 中 | M |
| 4 | 命名空间不一致 (Services.Ai vs Services.AI) | 中 | S |
| 5 | 热重载在生产环境被默认禁用 | 中 | S |
| 6 | 自定义连接池与驱动内置连接池重叠 | 低 | L |
| 7 | AI 场景配置未 YAML 驱动 | 低 | L |

---

## 问题 1：AI FSM/Orchestrator 硬编码耦合（高优先级）

### 现状

`AiToolOrchestrator.cs:51,64` 中 `ToolExecutionResult` 和 `SessionStateInfo` 直接引用 `AppointmentStateMachine.State`：

```csharp
// 当前代码 AiToolOrchestrator.cs
public class ToolExecutionResult
{
    public AppointmentStateMachine.State? FsmState { get; set; }  // ← 硬耦合
}

public class SessionStateInfo
{
    public AppointmentStateMachine.State FsmState { get; set; }   // ← 硬耦合
    public bool IsEscalated => FsmState == AppointmentStateMachine.State.Escalate; // ← 场景特定逻辑
}
```

### 问题

- 添加新场景（维修预约、金融贷款）需修改 `AiToolOrchestrator` 核心类
- 违反开闭原则（OCP）

### 解决方案

引入抽象 `IConversationFsm` 接口，Orchestrator 仅依赖接口而非具体 FSM：

```csharp
// 新文件：Services/AI/IConversationFsm.cs
namespace NetYamlForge.Services.AI;

public interface IConversationFsm
{
    string CurrentState { get; }
    bool IsTerminal { get; }
    bool IsEscalated { get; }
    IReadOnlySet<string> AllowedTools { get; }
    void FireTrigger(string trigger, double confidence = 1.0);
}

// 新文件：Services/AI/FsmRegistry.cs
public interface IFsmRegistry
{
    IConversationFsm GetOrCreate(string conversationId, string scenarioKey);
    void Remove(string conversationId);
}
```

修改 `ToolExecutionResult` 和 `SessionStateInfo`：

```csharp
// 修改后 AiToolOrchestrator.cs
public class ToolExecutionResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Data { get; set; }
    public string? ValidationFailedReason { get; set; }
    public string? FsmState { get; set; }        // string，不再是枚举
    public bool IsEscalated { get; set; }        // 从 FSM 接口读取
}

public class SessionStateInfo
{
    public string ConversationId { get; set; } = string.Empty;
    public string FsmState { get; set; } = string.Empty;          // string
    public bool IsEscalated { get; set; }
    public HashSet<string> AllowedTools { get; set; } = new();
    public Dictionary<string, string> CollectedSlots { get; set; } = new();
}
```

`AppointmentStateMachine` 实现 `IConversationFsm`：

```csharp
public class AppointmentStateMachine : IConversationFsm
{
    public string CurrentState => _machine.State.ToString();
    public bool IsTerminal => _machine.State is State.Booked or State.Cancelled;
    public bool IsEscalated => _machine.State == State.Escalate;
    public IReadOnlySet<string> AllowedTools => GetAllowedToolsForState(_machine.State);
    // ... 其余不变
}
```

### 需修改的文件

| 文件 | 操作 |
|---|---|
| `Services/AI/IConversationFsm.cs` | 新建 |
| `Services/AI/FsmRegistry.cs` | 新建 |
| `Services/AI/AiToolOrchestrator.cs` | 修改 FsmState 字段类型 |
| `Services/AI/AppointmentStateMachine.cs` | 实现 IConversationFsm |
| `Extensions/ServiceCollectionExtensions.cs` | 注册 IFsmRegistry |

---

## 问题 2：FSM 状态不持久化（高优先级）

### 现状

`AppointmentStateMachine.cs:49`：
```csharp
private int _lowConfidenceCount = 0; // 纯内存，重启即丢
```

`SlotFillingManager` 使用 SQLite 存储槽位值，但 FSM 状态（Init/CollectVehicle/Booked 等）**没有写入数据库**。

### 解决方案

在 `SlotFillingManager` 使用的 SQLite 表中新增 FSM 状态列：

```sql
-- 在 SlotFillingManager 初始化时执行
ALTER TABLE conversation_sessions ADD COLUMN IF NOT EXISTS fsm_state TEXT DEFAULT 'Init';
ALTER TABLE conversation_sessions ADD COLUMN IF NOT EXISTS low_confidence_count INTEGER DEFAULT 0;
ALTER TABLE conversation_sessions ADD COLUMN IF NOT EXISTS last_updated_utc TEXT;
```

修改 `SlotFillingManager` 接口：

```csharp
public interface ISlotFillingManager
{
    // 现有方法保持不变...
    
    // 新增：FSM 状态持久化
    Task SaveFsmStateAsync(string conversationId, string state, int lowConfidenceCount, CancellationToken ct = default);
    Task<(string state, int lowConfidenceCount)> LoadFsmStateAsync(string conversationId, CancellationToken ct = default);
}
```

修改 `AppointmentStateMachine` 构造函数以支持从持久化状态恢复：

```csharp
public class AppointmentStateMachine : IConversationFsm
{
    public static AppointmentStateMachine FromPersistedState(
        string conversationId,
        string persistedState,
        int lowConfidenceCount)
    {
        var state = Enum.Parse<State>(persistedState, ignoreCase: true);
        return new AppointmentStateMachine(conversationId, state, lowConfidenceCount);
    }

    private AppointmentStateMachine(string conversationId, State initialState, int lowConfidenceCount)
    {
        _conversationId = conversationId;
        _lowConfidenceCount = lowConfidenceCount;
        _machine = new StateMachine<State, Trigger>(initialState); // 从持久化状态恢复
        ConfigureTransitions();
    }
}
```

`FsmRegistry.GetOrCreate` 负责加载持久化状态：

```csharp
public class FsmRegistry : IFsmRegistry
{
    public async Task<IConversationFsm> GetOrCreateAsync(string conversationId, string scenarioKey)
    {
        if (_cache.TryGetValue(conversationId, out var cached)) return cached;
        
        // 从 DB 恢复
        var (state, count) = await _slotManager.LoadFsmStateAsync(conversationId);
        var fsm = AppointmentStateMachine.FromPersistedState(conversationId, state, count);
        _cache[conversationId] = fsm;
        return fsm;
    }
}
```

### 需修改的文件

| 文件 | 操作 |
|---|---|
| `Services/AI/SlotFillingManager.cs` | 新增 SaveFsmStateAsync / LoadFsmStateAsync |
| `Services/AI/AppointmentStateMachine.cs` | 新增 FromPersistedState 工厂方法 |
| `Services/AI/FsmRegistry.cs` | 新建（实现持久化加载逻辑） |

---

## 问题 3：BatchJobExecutor 构造函数注入 8 个依赖（中优先级）

### 现状

`BatchJobExecutor.cs:40-58`：
```csharp
public BatchJobExecutor(
    IDbConnectionFactory dbConnectionFactory,
    HookExecutionService hookExecutionService,
    IChinaStockService chinaStockService,          // 特定领域
    IEmailServiceFactory emailFactory,
    IDocumentPdfService docPdf,
    IGeminiCliService geminiCli,
    ILogger<BatchJobExecutor> logger,
    ILoggerFactory loggerFactory)
```

**问题**：
- 每新增任务类型需修改主类
- 所有依赖提前实例化，即便某 Job 根本不用

### 解决方案：策略模式 + 延迟服务解析

定义批处理步骤执行器接口：

```csharp
// 新文件：Services/BatchJob/IBatchStepHandler.cs
public interface IBatchStepHandler
{
    string StepType { get; }  // "sql", "email", "pdf", "ai", "stock", "hook"
    Task<BatchStepResult> ExecuteAsync(BatchStepContext context, CancellationToken ct);
}

public record BatchStepContext(
    string ProjectName,
    Dictionary<string, object?> JobData,
    IDbConnection Db,
    IDbTransaction Transaction);
```

每种步骤类型独立实现：

```csharp
// Services/BatchJob/Steps/SqlBatchStep.cs
public class SqlBatchStep : IBatchStepHandler
{
    public string StepType => "sql";
    // 只注入 IDbConnectionFactory，无其他依赖
}

// Services/BatchJob/Steps/EmailBatchStep.cs
public class EmailBatchStep : IBatchStepHandler
{
    public string StepType => "email";
    // 只注入 IEmailServiceFactory
}

// Services/BatchJob/Steps/AiBatchStep.cs  
public class AiBatchStep : IBatchStepHandler
{
    public string StepType => "ai";
    // 只注入 IGeminiCliService
}
```

重构 `BatchJobExecutor` 只持有注册表：

```csharp
public class BatchJobExecutor : IBatchJobExecutor
{
    private readonly IReadOnlyDictionary<string, IBatchStepHandler> _handlers;
    private readonly HookExecutionService _hooks;
    private readonly ILogger<BatchJobExecutor> _logger;

    public BatchJobExecutor(
        IEnumerable<IBatchStepHandler> handlers,   // DI 自动收集所有实现
        HookExecutionService hooks,
        ILogger<BatchJobExecutor> logger)
    {
        _handlers = handlers.ToDictionary(h => h.StepType);
        _hooks = hooks;
        _logger = logger;
    }
}
```

### 需修改的文件

| 文件 | 操作 |
|---|---|
| `Services/BatchJob/IBatchStepHandler.cs` | 新建 |
| `Services/BatchJob/Steps/SqlBatchStep.cs` | 新建 |
| `Services/BatchJob/Steps/EmailBatchStep.cs` | 新建 |
| `Services/BatchJob/Steps/AiBatchStep.cs` | 新建 |
| `Services/BatchJob/Steps/PdfBatchStep.cs` | 新建 |
| `Services/BatchJob/Steps/StockBatchStep.cs` | 新建 |
| `Services/BatchJob/Steps/HookBatchStep.cs` | 新建 |
| `Services/BatchJob/BatchJobExecutor.cs` | 重构构造函数 |
| `Extensions/ServiceCollectionExtensions.cs` | 注册所有 IBatchStepHandler |

---

## 问题 4：命名空间不一致（中优先级）

### 现状

- 旧 AI 服务：`NetYamlForge.Services.Ai`（小写 i）→ `GeminiCliService.cs`
- 新 AI 服务：`NetYamlForge.Services.AI`（大写 I）→ `AppointmentStateMachine.cs` 等
- `BatchJobExecutor.cs:36`：`NetYamlForge.Services.Ai.IGeminiCliService` 混用

### 解决方案

**统一使用 `NetYamlForge.Services.AI`（大写 I）**，将旧的 `Services.Ai` 命名空间下的文件批量重命名：

受影响文件（需搜索确认）：
- `Services/Ai/GeminiCliService.cs` → namespace 改为 `NetYamlForge.Services.AI`
- `Services/Ai/IGeminiCliService.cs` → namespace 改为 `NetYamlForge.Services.AI`
- `Services/Ai/` 目录下所有 `.cs` 文件

操作步骤：
```bash
# 批量替换命名空间
find NetYamlForge/Services/Ai -name "*.cs" \
  -exec sed -i 's/namespace NetYamlForge\.Services\.Ai/namespace NetYamlForge.Services.AI/g' {} \;

find NetYamlForge -name "*.cs" \
  -exec sed -i 's/NetYamlForge\.Services\.Ai\./NetYamlForge.Services.AI./g' {} \;
```

### 需修改的文件

| 文件 | 操作 |
|---|---|
| `Services/Ai/*.cs` | 批量修改 namespace |
| 所有引用 `Services.Ai.` 的文件 | 批量替换 using 路径 |

---

## 问题 5：热重载在生产环境被默认禁用（中优先级）

### 现状

`YamlHotReloadService.cs:11`：
```csharp
public bool OnlyInDevelopment { get; set; } = true; // 默认仅开发环境
```

`YamlHotReloadService.cs:47-51`：
```csharp
if (_options.OnlyInDevelopment && !IsDevelopment())
{
    _logger.LogInformation("YAML ホットリロードは開発環境でのみ有効です");
    return Task.CompletedTask;
}
```

**问题**：生产环境修复 Hook Bug 必须重启服务，中断所有活跃连接。

### 解决方案

1. **将 `OnlyInDevelopment` 默认值改为 `false`**（允许生产环境热重载）
2. **添加 `WatchedPaths` 配置**，生产只监听 YAML，不监听 `.cs`（避免生产编译风险）
3. **添加变更记录日志**，便于审计生产环境的热重载事件

```csharp
public class HotReloadOptions
{
    public const string SectionName = "HotReload";
    public bool Enabled { get; set; } = true;
    public bool OnlyInDevelopment { get; set; } = false;      // ← 改为 false
    public bool EnableCsHotReload { get; set; } = false;      // ← 新增，C# 热重载需显式开启
    public int DebounceMs { get; set; } = 500;
}
```

在 `YamlHotReloadService.StartAsync` 中增加条件判断：

```csharp
public Task StartAsync(CancellationToken cancellationToken)
{
    if (!_options.Enabled) return Task.CompletedTask;
    if (_options.OnlyInDevelopment && !IsDevelopment()) return Task.CompletedTask;

    _logger.LogInformation("HotReload started. CsReload={CsEnabled}, IsDev={IsDev}",
        _options.EnableCsHotReload, IsDevelopment());

    foreach (var projectDir in GetProjectDirs())
    {
        _fileWatcher.StartWatching(projectDir, watchCsFiles: _options.EnableCsHotReload);
    }
    return Task.CompletedTask;
}
```

`appsettings.Production.json` 推荐配置：
```json
{
  "HotReload": {
    "Enabled": true,
    "OnlyInDevelopment": false,
    "EnableCsHotReload": false,
    "DebounceMs": 1000
  }
}
```

### 需修改的文件

| 文件 | 操作 |
|---|---|
| `Services/HotReload/YamlHotReloadService.cs` | 修改 OnlyInDevelopment 默认值，新增 EnableCsHotReload 逻辑 |
| `Services/HotReload/YamlFileWatcher.cs` | StartWatching 新增 watchCsFiles 参数 |
| `appsettings.json` | 新增 HotReload 配置节 |
| `appsettings.Production.json` | 新建（生产配置） |

---

## 问题 6：自定义连接池与驱动内置连接池重叠（低优先级）

### 现状

`ConnectionPool.cs` 实现了完整的自定义连接池（MaxPoolSize/IdleTimeout/MaxLifetime），但：
- Npgsql：内置连接池（`Pooling=true;Maximum Pool Size=100`）
- MySql.Data：内置连接池（`Pooling=True;Maximum Pool Size=100`）
- Microsoft.Data.SqlClient：内置连接池（默认启用）
- Microsoft.Data.Sqlite：轻量级，单文件，不需要连接池

**效果**：相当于连接池上再套一层连接池，增加状态管理复杂度。

### 解决方案

**方案 A（保守）**：禁用自定义池，仅保留统计监控层

```csharp
// 将 ConnectionPool 改为纯监控适配器
public class ConnectionPoolMonitor : IDbConnectionFactory
{
    private readonly ConnectionPoolStats _stats = new();
    private readonly IDbConnectionFactory _inner;

    public async Task<IDbConnection> CreateConnectionAsync(string? projectName, CancellationToken ct)
    {
        var conn = await _inner.CreateConnectionAsync(projectName, ct);
        Interlocked.Increment(ref _stats._totalCreated);
        // 包装返回，连接回收时记录统计
        return new TrackedConnection(conn, _stats);
    }
}
```

**方案 B（推进）**：将 `ConnectionPoolOptions` 映射到各驱动原生连接串参数

```csharp
// DbConnectionFactory.cs
private string BuildConnectionString(ProjectDbConfig config, ConnectionPoolOptions poolOpts)
{
    return config.DbType switch
    {
        "postgres" => $"{config.ConnectionString};Maximum Pool Size={poolOpts.MaxPoolSize};Connection Idle Lifetime={poolOpts.IdleTimeoutMs/1000}",
        "mysql"    => $"{config.ConnectionString};Maximum Pool Size={poolOpts.MaxPoolSize}",
        "sqlite"   => config.ConnectionString, // 无连接池需求
        _          => config.ConnectionString
    };
}
```

**推荐实施方案 B**，保留 `ConnectionPoolOptions` 配置语义，将参数下推到驱动层。

### 需修改的文件

| 文件 | 操作 |
|---|---|
| `Services/Connection/ConnectionPool.cs` | 重构为监控适配器或删除自定义池逻辑 |
| `Services/Connection/DbConnectionFactory.cs` | 新增 BuildConnectionString 方法 |
| `Extensions/ServiceCollectionExtensions.cs` | 更新注册逻辑 |

---

## 问题 7：AI 场景配置未 YAML 驱动（低优先级）

### 现状

框架核心设计哲学是"一切皆 YAML 配置"，但 AI 场景（试驾预约的状态机、槽位定义、提示词）全部硬编码在 C# 中：

- `AppointmentStateMachine.cs`：状态、触发器、转换规则硬编码
- `SlotFillingManager.cs`：槽位名称（vehicle/date/time/name/phone）硬编码
- `PromptVersionResolver.cs`：提示词文件路径约定硬编码

### 解决方案

引入 `skills/` 目录下的 `SKILL.md` + `fsm-config.yml`（已有 skills 目录）：

```yaml
# skills/auto-dealer/dealer-customer/fsm-config.yml
scenario: appointment
initial_state: Init
escalate_trigger: LowConfidence
escalate_threshold: 2

states:
  - name: Init
    allowed_tools: ["ask_vehicle_preference"]
  - name: CollectVehicle
    allowed_tools: ["set_vehicle", "ask_date"]
  - name: CollectDate
    allowed_tools: ["set_date", "ask_time"]
  # ...

slots:
  - name: vehicle
    required: true
    validator: "non-empty"
  - name: date
    required: true
    validator: "date-format:yyyy-MM-dd"
  - name: phone
    required: true
    validator: "jp-phone"

transitions:
  - from: Init
    trigger: VehicleProvided
    to: CollectDate
  - from: CollectDate
    trigger: DateProvided
    to: CollectTime
```

新增 YAML 驱动的 FSM 工厂：

```csharp
// Services/AI/YamlDrivenFsmFactory.cs
public class YamlDrivenFsmFactory : IFsmFactory
{
    public IConversationFsm Create(string conversationId, FsmConfig config)
    {
        var machine = new StateMachine<string, string>(config.InitialState);
        foreach (var t in config.Transitions)
        {
            machine.Configure(t.From).Permit(t.Trigger, t.To);
        }
        return new GenericFsm(machine, config);
    }
}
```

### 需修改的文件

| 文件 | 操作 |
|---|---|
| `Services/AI/FsmConfig.cs` | 新建（YAML 配置模型） |
| `Services/AI/YamlDrivenFsmFactory.cs` | 新建 |
| `Services/AI/GenericFsm.cs` | 新建（实现 IConversationFsm） |
| `skills/auto-dealer/dealer-customer/fsm-config.yml` | 新建 |

---

## 实施顺序建议

```
Phase 1（本周）：问题 4 → 问题 5 → 问题 1
  - 命名空间统一（无功能风险）
  - 热重载生产配置（配置级改动）
  - FSM 接口抽象（解耦核心 AI 层）

Phase 2（下周）：问题 2 → 问题 3
  - FSM 状态持久化（需要 DB schema 变更）
  - BatchJob 策略模式重构（较大代码改动）

Phase 3（后续）：问题 6 → 问题 7
  - 连接池重叠消除（需要仔细测试各 DB 驱动行为）
  - YAML 驱动 FSM（架构演进，影响面广）
```

---

## 测试验证要求

每个问题修复后，需验证：

1. **问题 1**：新建第二个 FSM 场景（如 MaintenanceStateMachine），确认无需修改 AiToolOrchestrator
2. **问题 2**：重启服务后，进行中的对话从上次槽位状态恢复
3. **问题 3**：新增一种 BatchStep 类型无需修改 BatchJobExecutor
4. **问题 4**：`dotnet build` 无命名空间警告，所有测试通过
5. **问题 5**：生产环境 YAML 修改后无需重启即可生效
6. **问题 6**：连接池监控统计数据正常，无双重连接池警告日志
7. **问题 7**：通过修改 `fsm-config.yml` 新增对话步骤，无需重新编译
