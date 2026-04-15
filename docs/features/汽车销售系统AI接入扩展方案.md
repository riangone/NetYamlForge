# 汽车销售系统 AI 接入扩展方案

> **基于文档**: `docs/设计1.md` + 现有 `auto-dealer-demo` 项目  
> **创建日期**: 2026-04-09  
> **目标**: 将设计 1.md 的企业级架构理念落地到 NetYamlForge 现有框架

---

## 目录

- [一、现状分析](#一现状分析)
- [二、设计 1.md 要求 vs 现有实现的差距](#二设计-1md-要求-vs-现有实现的差距)
- [三、扩展方案（分三阶段）](#三扩展方案分三阶段)
  - [Phase 1：核心闭环强化（MVP → 生产就绪）](#phase-1核心闭环强化mvp--生产就绪)
  - [Phase 2：可观测性 & 增强](#phase-2可观测性--增强)
  - [Phase 3：智能化 & 生态](#phase-3智能化--生态)
- [四、扩展后的架构总览](#四扩展后的架构总览)
- [五、实施任务清单](#五实施任务清单)

---

## 一、现状分析

现有 `auto-dealer-demo` 项目已经具备**非常扎实的 AI 基础设施**：

| 已有能力 | 核心文件/服务 | 状态 |
|---------|-------------|------|
| 7 种 AI CLI 工具集成（Qwen/Claude/Gemini/Ollama/LM Studio/Copilot/Gemini） | `Services/AI/Providers/*.cs` | ✅ 已完成 |
| `ILlmProvider` 抽象层 + `CliFirstLlmProvider` 回退机制 | `Services/AI/Providers/ILlmProvider.cs` | ✅ 已完成 |
| `AutoDealerChatService`（1546 行）核心聊天服务 | `Services/AI/AutoDealerChatService.cs` | ✅ 已完成 |
| 意图分类器（规则 + LLM 混合） | `Services/AI/HybridIntentClassifier.cs` | ✅ 已完成 |
| 槽位填充管理器（多轮对话信息收集） | `Services/AI/SlotFillingManager.cs` (577 行) | ✅ 已完成 |
| 对话状态管理 | `Services/AI/ConversationManager.cs` (308 行) | ✅ 已完成 |
| 人工接管管理 | `Services/AI/HandoverManager.cs` | ✅ 已完成 |
| Tool Calling（`query_data` / `create_appointment_request`） | `Skills/auto-dealer/_tools-definition.md` | ✅ 已完成 |
| Prompt 模板系统（客户/员工双角色） | `Skills/auto-dealer/_system-prompt-*.md` | ✅ 已完成 |
| 情感分析 | `Services/AI/SentimentAnalyzer.cs` | ✅ 已完成 |
| 14 个实体 YAML + 15+ 自定义页面 | `projects/auto-dealer-demo/entities/*.yml` | ✅ 已完成 |
| AI CLI 控制器（任务队列/聊天历史） | `Controllers/AIController.cs` | ✅ 已完成 |
| 钩子系统（销售线索自动评分/时间戳） | `projects/auto-dealer-demo/hooks/` | ✅ 已完成 |

### 现有架构优势

```
appsettings.json (AICli / AiWindow 配置)
        │
        ▼
   CLIServiceFactory (工厂模式)
        │
        ├── QwenCodeCLIService
        ├── ClaudeCLIService
        ├── OllamaCLIService
        └── ... (7 种)
        │
        ▼
   AutoDealerChatService (核心编排)
        ├── HybridIntentClassifier (意图识别)
        ├── SlotFillingManager (槽位填充)
        ├── ConversationManager (对话管理)
        ├── QueryParser/Execution (数据查询)
        └── HandoverManager (人工接管)
```

---

## 二、设计 1.md 要求 vs 现有实现的差距

| 设计 1.md 要求 | 现有实现 | 差距评估 | 优先级 |
|----------------|---------|---------|--------|
| **有限状态机（FSM）** 管控对话流程，防止 AI 跑题或漏字段 | `SlotFillingManager` 有部分状态管理，但不是严格 FSM，无状态转换限制 | ⚠️ 需要强化 | 🔴 高 |
| **Redis 会话管理** + 滑动窗口上下文 | 现有 `ConversationManager` 仅基于 SQLite，无内存缓存层 | ⚠️ 缺 Redis 集成 | 🟡 中 |
| **Tool Calling 强校验**（JSON Schema + FluentValidation） | 现有 `query_data` JSON 解析较松散，无严格 Schema 校验 | ⚠️ 需要严格校验层 | 🔴 高 |
| **Polly 弹性容错**（重试、熔断、降级） | 现有仅 try-catch 基础错误处理 | ⚠️ 缺 Polly | 🟡 中 |
| **Outbox Pattern** 异步消息投递原子性 | 现有 `TaskQueueService` 但非 Outbox，无消息队列 | ⚠️ 需要消息队列 | 🟢 低 |
| **OpenTelemetry 全链路追踪** | 无分布式追踪 | ❌ 缺失 | 🟡 中 |
| **Microsoft.Extensions.AI 抽象层** | 自有 `ILlmProvider` 接口 | ⚠️ 可评估是否迁移 | 🟢 低 |
| **档期冲突检测**（并发预约控制） | `AppointmentService` 无冲突检测逻辑 | ❌ 缺失 | 🔴 高 |
| **Prompt 版本化管理** + 热更新 | 有 skills/*.md 文件但无版本控制/AB 测试 | ⚠️ 需要版本机制 | 🟡 中 |
| **PII 敏感数据脱敏** | 无自动脱敏逻辑 | ⚠️ 需要脱敏层 | 🟡 中 |
| **降级策略**（AI 宕机 → 表单引导） | 无降级路径 | ⚠️ 需要降级策略 | 🟡 中 |

---

## 三、扩展方案（分三阶段）

### Phase 1：核心闭环强化（MVP → 生产就绪）

> **目标**: 打通"对话 → 结构化 → 校验 → 入库"闭环，确保数据安全性和可靠性

#### 1.1 引入有限状态机（Stateless 库）

**安装**:

```bash
dotnet add package Stateless
```

**状态机定义**:

```
状态流转图（试驾预约场景）：

    INIT ──→ COLLECT_VEHICLE ──→ COLLECT_DATE ──→ COLLECT_TIME ──→ COLLECT_NAME ──→ COLLECT_PHONE
                                                                                                │
                                                                                                ▼
                          CANCELLED ←── BOOKED ←── CONFIRMING ←─────────────────────────────────┘
```

**实现方案**:

在 `SlotFillingManager` 上封装 `Stateless.StateMachine<TState, TTrigger>`：

```csharp
public enum AppointmentState
{
    Init,
    CollectVehicle,
    CollectDate,
    CollectTime,
    CollectName,
    CollectPhone,
    Confirming,
    Booked,
    Cancelled
}

public enum AppointmentTrigger
{
    VehicleProvided,
    DateProvided,
    TimeProvided,
    NameProvided,
    PhoneProvided,
    Confirmed,
    Cancelled,
    Timeout
}
```

**状态白名单 Tool 控制**:

| 当前状态 | 允许的 Tool | 禁止的 Tool |
|---------|------------|------------|
| `COLLECT_VEHICLE` | `query_data`（仅查询车辆） | `create_appointment_request` |
| `COLLECT_DATE` | - | `create_appointment_request` |
| `COLLECT_TIME` | - | `create_appointment_request` |
| `COLLECT_NAME` | - | `create_appointment_request` |
| `COLLECT_PHONE` | - | `create_appointment_request` |
| `CONFIRMING` | `create_appointment_request` | `query_data` |
| `BOOKED` | - | 全部 |

**状态持久化**:

在 `ai_conversations` 表新增字段：

```sql
ALTER TABLE ai_conversations ADD COLUMN current_state TEXT DEFAULT 'init';
ALTER TABLE ai_conversations ADD COLUMN collected_slots TEXT; -- JSON: {"vehicle":"RAV4","date":"2026-04-15",...}
```

#### 1.2 Tool Calling 强校验层

**新建目录**: `Services/AI/ToolValidation/`

| 文件 | 职责 |
|------|------|
| `ToolCallValidator.cs` | JSON Schema 校验 + 业务规则校验 |
| `ToolDefinition.cs` | 每个 Tool 的强类型定义 |
| `ToolCallResult.cs` | 校验结果（成功/失败/错误码） |
| `ToolSchemaBuilder.cs` | 从 YAML 配置动态生成 JSON Schema |

**校验流程**:

```
LLM 输出
    │
    ▼
[1] JSON 提取 ──失败──→ 返回 "格式错误，请重新输出"
    │ 成功
    ▼
[2] Schema 校验 ──失败──→ 返回 "缺少必填字段: phone"
    │ 成功
    ▼
[3] 业务规则校验 ──失败──→ 返回 "手机号格式不正确"
    │ 成功
    ▼
[4] 状态白名单检查 ──失败──→ 返回 "当前状态不允许此操作"
    │ 通过
    ▼
[5] 执行 Tool
```

**ToolDefinition 示例**:

```csharp
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonSchema ParameterSchema { get; set; } = null!;
    public HashSet<AppointmentState> AllowedStates { get; } = new();
    public Func<JsonNode, Task<ToolCallResult>> ExecuteAsync { get; set; } = null!;
}
```

**`query_data` 的 JSON Schema**:

```json
{
  "type": "object",
  "required": ["tool_call", "entity", "action"],
  "properties": {
    "tool_call": { "type": "string", "const": "query_data" },
    "entity": { "type": "string", "enum": ["vehicles", "sales_leads", "customers", "service_appointments"] },
    "action": { "type": "string", "enum": ["list", "count"] },
    "filters": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["field", "op", "value"],
        "properties": {
          "field": { "type": "string" },
          "op": { "type": "string", "enum": ["eq", "ne", "gt", "gte", "lt", "lte", "in", "contains", "startswith"] },
          "value": {}
        }
      }
    },
    "top": { "type": "integer", "minimum": 1, "maximum": 50 },
    "orderBy": {
      "type": "object",
      "properties": {
        "field": { "type": "string" },
        "dir": { "type": "string", "enum": ["asc", "desc"] }
      }
    }
  }
}
```

#### 1.3 Polly 弹性策略

**安装**:

```bash
dotnet add package Polly
dotnet add package Polly.Extensions.Http
```

**appsettings.json 配置**:

```json
"AIPolicy": {
  "RetryCount": 3,
  "TimeoutSeconds": 8,
  "CircuitBreakerThreshold": 5,
  "CircuitBreakerDurationSeconds": 60,
  "FallbackMessage": "ネットワークが混雑しています。少々お待ちください。"
}
```

**策略注册**:

在 `Program.cs` 中注册 Polly 策略：

```csharp
builder.Services.AddHttpClient<ILlmProvider, CliFirstLlmProvider>()
    .AddPolicyHandler(policyRegistry.Get<AsyncPolicy<HttpResponseMessage>>("ai-retry"))
    .AddPolicyHandler(policyRegistry.Get<AsyncPolicy<HttpResponseMessage>>("ai-circuit-breaker"))
    .AddPolicyHandler(policyRegistry.Get<AsyncPolicy<HttpResponseMessage>>("ai-timeout"));
```

**降级策略**:

当 AI 连续 2 次提取失败时：

1. 状态机标记 `ESCALATE`
2. 推送会话至人工坐席队列
3. 返回传统表单引导页面
4. WebSocket 转发人工坐席消息

#### 1.4 档期冲突检测

**修改**: `Services/AI/AppointmentService.cs`

新增方法：

```csharp
/// <summary>
/// 检查指定时间段是否可预约
/// </summary>
public async Task<SlotAvailability> CheckAvailabilityAsync(
    string appointmentType,
    DateTime preferredDate,
    string preferredTime,
    string? projectId = null)
{
    var project = ResolveProject(projectId);

    using var db = _dbConnectionFactory.CreateConnection(project);

    // 计算时间段（假设每个预约 1 小时）
    var start = preferredDate.Date + ParseTime(preferredTime);
    var end = start.AddHours(1);

    // 查询冲突预约
    var conflictCount = await db.QuerySingleAsync<int>(@"
        SELECT COUNT(*) FROM service_appointments
        WHERE appointment_type = @Type
          AND status NOT IN ('cancelled', 'no_show')
          AND preferred_date >= @Start
          AND preferred_date < @End",
        new { Type = appointmentType, Start = start, End = end });

    return new SlotAvailability
    {
        IsAvailable = conflictCount < _maxSlotsPerTime,
        ConflictCount = conflictCount,
        MaxSlots = _maxSlotsPerTime,
        AlternativeSlots = await FindAlternativeSlotsAsync(appointmentType, preferredDate, projectId)
    };
}

/// <summary>
/// 查找可替代的预约时间段
/// </summary>
private async Task<List<TimeSlot>> FindAlternativeSlotsAsync(
    string appointmentType,
    DateTime preferredDate,
    string? projectId = null)
{
    // 返回同一天的可用时间段（9:00-18:00，每小时一段）
    var slots = new List<TimeSlot>();
    for (int hour = 9; hour < 18; hour++)
    {
        var time = $"{hour:D2}:00";
        var availability = await CheckAvailabilityAsync(appointmentType, preferredDate, time, projectId);
        if (availability.IsAvailable)
        {
            slots.Add(new TimeSlot { Time = time, IsAvailable = true });
        }
    }
    return slots;
}
```

**配置**:

在 `appsettings.json` 添加：

```json
"AppointmentSlots": {
  "MaxSlotsPerTime": 2,
  "SlotDurationMinutes": 60,
  "BusinessHoursStart": 9,
  "BusinessHoursEnd": 18,
  "AdvanceBookingDays": 30
}
```

---

### Phase 2：可观测性 & 增强

> **目标**: 提升系统可观测性，支持 Prompt 版本管理，引入 Redis 缓存

#### 2.1 OpenTelemetry 集成

**安装**:

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Instrumentation.Runtime
dotnet add package OpenTelemetry.Exporter.Prometheus.HttpListeners
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

**Program.cs 配置**:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("NetYamlForge.AI")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("NetYamlForge.AI")
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());
```

**关键埋点**:

| 指标名 | 类型 | 说明 |
|-------|------|------|
| `ai.tool_call.duration` | Histogram | Tool 调用耗时分布 |
| `ai.tool_call.success_rate` | Counter | Tool 调用成功率 |
| `ai.state_transition.duration` | Histogram | 状态转换耗时 |
| `ai.fallback.rate` | Counter | 降级触发次数 |
| `ai.token.usage` | Counter | Token 消耗总量 |
| `ai.conversation.active_count` | ObservableGauge | 活跃会话数 |
| `ai.intent_classification.accuracy` | Counter | 意图分类准确度 |
| `ai.db_commit.success_rate` | Counter | DB 提交成功率 |

**示例埋点代码**:

```csharp
private static readonly Histogram<double> s_toolCallDuration =
    s_meter.CreateHistogram<double>("ai.tool_call.duration", unit: "ms");

public async Task<ToolCallResult> ExecuteToolAsync(JsonNode toolCall)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var result = await _tool.ExecuteAsync(toolCall);
        s_toolCallDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool", toolCall["tool_call"]!.ToString()));
        return result;
    }
    catch
    {
        s_toolCallDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool", toolCall["tool_call"]!.ToString()));
        throw;
    }
}
```

#### 2.2 Prompt 版本化管理

**目录结构**:

```
skills/auto-dealer/
├── v1/
│   ├── _system-prompt-customer.md
│   ├── _system-prompt-staff.md
│   └── _tools-definition.md
├── v2/
│   ├── _system-prompt-customer.md
│   ├── _system-prompt-staff.md
│   └── _tools-definition.md
└── current -> v1  (符号链接)
```

**ai-config.yaml 配置**:

```yaml
ai:
  prompt:
    version: v1
    allowHotReload: true
    reloadDebounceMs: 500
    abTest:
      enabled: false
      variantA: v1
      variantB: v2
      trafficSplit: 50  # 50% 流量到 v2
  promptCache:
    enabled: true
    ttlSeconds: 300
```

**版本切换流程**:

1. 修改 `ai-config.yaml` 中 `prompt.version` 值
2. `SkillLoader` 检测配置变更
3. 热重载机制加载新 Prompt 文件
4. 现有会话继续使用旧 Prompt，新会话使用新 Prompt（可选）
5. 记录版本变更日志到 `ai_conversations.prompt_version` 字段

**AB 测试实现**:

```csharp
public string ResolvePromptVersion(string sessionId)
{
    if (!_config.AbTest.Enabled)
        return _config.Prompt.Version;

    // 基于 SessionId 哈希分配
    var hash = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)), 0);
    var ratio = hash % 100;

    return ratio < _config.AbTest.TrafficSplit
        ? _config.AbTest.VariantB
        : _config.AbTest.VariantA;
}
```

#### 2.3 Redis 会话缓存

**安装**:

```bash
dotnet add package StackExchange.Redis
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
```

**appsettings.json 配置**:

```json
"Redis": {
  "ConnectionString": "localhost:6379",
  "SessionTtlHours": 24,
  "SlidingWindowMessages": 20,
  "KeyPrefix": "nyforge:auto-dealer:"
}
```

**ConversationManager 改造**:

双层架构：

```
[请求]
    │
    ▼
[Redis 缓存层] ──命中──→ 返回最近 20 条消息
    │ 未命中
    ▼
[SQLite 查询] ──→ 加载历史 ──→ 写入 Redis (TTL 24h)
    │
    ▼
[返回上下文]
```

**Redis Key 设计**:

| Key 模式 | 类型 | TTL | 说明 |
|---------|------|-----|------|
| `nyforge:auto-dealer:session:{conversationId}` | Hash | 24h | 会话元数据 |
| `nyforge:auto-dealer:messages:{conversationId}` | List | 24h | 最近 20 条消息 |
| `nyforge:auto-dealer:state:{conversationId}` | String | 24h | 当前 FSM 状态 |
| `nyforge:auto-dealer:slots:{conversationId}` | Hash | 24h | 已收集的槽位值 |
| `nyforge:auto-dealer:lock:appointment:{date}:{time}` | String | 5min | 预约分布式锁 |

---

### Phase 3：智能化 & 生态

> **目标**: 接入 RAG 知识库，客户意图预测，销售转化归因

#### 3.1 RAG 车型知识库

**架构**:

```
车型手册 PDF / 配置表
        │
        ▼
[文本提取] (PdfPig / iText)
        │
        ▼
[分块] (按章节/车型，每块约 1000 token)
        │
        ▼
[Embedding] (Ollama nomic-embed-text / OpenAI text-embedding-3-small)
        │
        ▼
[向量存储] (SQLite vec0 扩展 / Qdrant)
        │
        ▼
[用户提问] ──→ [Embedding] ──→ [向量检索 Top-K] ──→ [Prompt 注入] ──→ [LLM 回答]
```

**利用现有组件**:

- `ai_knowledge` 实体 → 知识库条目存储
- `KnowledgeBaseService` → 知识检索服务
- 在 `_system-prompt-customer.md` 中注入检索结果

**新增配置**:

```json
"RAG": {
  "Enabled": true,
  "EmbeddingModel": "nomic-embed-text",
  "EmbeddingProvider": "ollama",
  "VectorStore": "sqlite",
  "ChunkSize": 1000,
  "TopK": 3,
  "Sources": [
    "docs/vehicle-manuals/",
    "docs/pricing-guide.yaml",
    "docs/comparison-chart.json"
  ]
}
```

#### 3.2 客户意图预测

**数据源**:

| 来源 | 字段 |
|------|------|
| `ai_conversations` | 历史对话、意图标签 |
| `lead_activities` | 活动参与记录 |
| `ai_feedback` | 用户反馈评分 |
| `sales_leads` | 线索状态、评分 |

**预测流程**:

```
历史对话 + 线索数据
        │
        ▼
[特征提取]
  - 浏览车型数量
  - 关注价格区间
  - 试乘预约次数
  - 情感倾向（正面/负面）
  - 最近活跃时间
        │
        ▼
[LLM 分析 Prompt]
"根据以下客户画像，预测下一步需求：{features}"
        │
        ▼
[输出预测结果]
  - 最可能需求: 试乘 / 报价 / 对比 / 金融咨询
  - 置信度: 0.85
  - 推荐话术: "关于 XX 车型，您还有什么想了解的吗？"
```

**主动推送触发条件**:

| 条件 | 推送内容 |
|------|---------|
| 浏览 3 台以上同级别车 | 主动提供对比分析 |
| 多次询问价格 | 提供专属报价方案 |
| 试乘后 3 天未跟进 | 主动询问试驾感受 |
| 情感分析为负面 | 转接高级顾问 |

#### 3.3 销售转化归因

**扩展 `sales_leads` 实体 YAML**:

```yaml
columns:
  # ... 现有字段 ...

  # AI 归因字段
  - name: ai_first_touch_conversation_id
    type: TEXT
    nullable: true
    description: "首次触达对话 ID"

  - name: ai_last_touch_conversation_id
    type: TEXT
    nullable: true
    description: "最终触达对话 ID"

  - name: ai_touch_count
    type: INTEGER
    default: 0
    description: "AI 对话触达次数"

  - name: ai_conversion_path
    type: TEXT
    nullable: true
    description: "转化路径 JSON"
```

**归因数据模型**:

```json
{
  "first_touch": {
    "conversation_id": "conv_abc123",
    "timestamp": "2026-04-01T10:00:00Z",
    "intent": "vehicle_inquiry",
    "channel": "web"
  },
  "touches": [
    { "conversation_id": "conv_abc123", "intent": "vehicle_inquiry", "timestamp": "2026-04-01" },
    { "conversation_id": "conv_def456", "intent": "test_drive", "timestamp": "2026-04-03" },
    { "conversation_id": "conv_ghi789", "intent": "price_inquiry", "timestamp": "2026-04-05" }
  ],
  "conversion": {
    "timestamp": "2026-04-06T14:00:00Z",
    "touch_count": 3,
    "days_to_convert": 5
  }
}
```

---

## 四、扩展后的架构总览

```
[客户端] (Web / 小程序 / App)
    │ WebSocket / SSE 实时双工通信
    ▼
[AIController] ← JWT 认证 + 限流
    │
    ▼
[AI 编排层] (AutoDealerChatService + BaseChatService)
    │
    ├── [会话管理] ConversationManager
    │     ├── Redis 缓存层 (热数据，TTL 24h)
    │     └── SQLite 持久层 (冷数据归档)
    │
    ├── [状态机] Stateless FSM
    │     ├── 8 个状态 (INIT → BOOKED / CANCELLED)
    │     └── 状态白名单 Tool 控制
    │
    ├── [意图识别] HybridIntentClassifier
    │     ├── 规则匹配 (快速路径)
    │     └── LLM 分类 (高精度路径)
    │
    ├── [槽位填充] SlotFillingManager
    │     ├── 渐进式信息收集 (一次一问)
    │     └── 防脱线机制 (保持流程)
    │
    ├── [Tool 校验] ToolCallValidator
    │     ├── JSON Schema 校验
    │     ├── 业务规则校验 (FluentValidation)
    │     └── 状态白名单检查
    │
    ├── [弹性策略] Polly
    │     ├── 重试 (3 次)
    │     ├── 熔断 (5 次失败 / 60s)
    │     ├── 超时 (8s)
    │     └── 降级 (AI 不可用 → 表单引导)
    │
    ├── [情感分析] SentimentAnalyzer
    │     └── 负面 → 预警 / 转接
    │
    └── [人工接管] HandoverManager
          └── 推送到人工坐席队列
    │
    ▼
[LLM 抽象层] (ILlmProvider / CliFirstLlmProvider)
    ├── Qwen Code CLI
    ├── Claude Code CLI
    ├── Ollama HTTP API
    └── ... (7 种提供者)
    │
    ▼
[业务服务层]
    ├── AppointmentService (档期冲突检测)
    ├── CustomerDataService (客户数据查询)
    ├── QueryParser / QueryExecution (自然语言查询)
    ├── KnowledgeBaseService (RAG 检索)
    └── NotificationService (通知投递)
    │
    ▼
[数据层]
    ├── SQLite (主数据库，Dapper)
    ├── Redis (会话缓存 + 分布式锁)
    └── Outbox (消息队列，MassTransit/RabbitMQ)
    │
    ▼
[可观测性]
    ├── OpenTelemetry (Tracing + Metrics)
    ├── Prometheus (指标存储)
    └── Grafana (监控大盘)
```

---

## 五、实施任务清单

### Phase 1 任务（生产就绪）

| # | 任务 | 涉及文件 | 预估工作量 |
|---|------|---------|-----------|
| 1.1 | 安装 Stateless 库，定义状态/触发器枚举 | `*.csproj` | 0.5h |
| 1.2 | 新建 `AppointmentStateMachine.cs`，封装 FSM 逻辑（**含 ESCALATE 状态**） | 新建 | 2h |
| 1.3 | 修改 `SlotFillingManager`，集成 FSM | `SlotFillingManager.cs` | 2h |
| 1.4 | `ai_conversations` 表新增 `current_state` 和 `collected_slots` 字段 | 迁移脚本 | 0.5h |
| 1.5 | 新建 `ToolValidation/` 目录，实现 `ToolCallValidator`（**含 SqlSafetyGuard 集成**） | 新建 4 文件 | 3h |
| 1.6 | 在 `AutoDealerChatService` 中集成 Tool 校验链 | `AutoDealerChatService.cs` | 2h |
| 1.7 | 安装 Polly，注册重试/熔断/超时策略 | `Program.cs`, `appsettings.json` | 1h |
| 1.8 | 在 `AppointmentService` 中实现档期冲突检测 | `AppointmentService.cs` | 2h |
| 1.9 | 编写 FSM + ToolValidator 的单元测试（**含 SqlSafetyGuard 测试**） | `*Tests.cs` | 3h |
| 1.10 | 实现 AI 数据隐私钩子（**PII 脱敏 + 审计日志**） | `AiDataPrivacyHooks.cs` | 1.5h |

### Phase 2 任务（可观测性 & 会话隔离）

| # | 任务 | 涉及文件 | 预估工作量 |
|---|------|---------|-----------|
| 2.1 | 安装 OpenTelemetry 相关包 | `*.csproj` | 0.5h |
| 2.2 | `Program.cs` 配置 Tracing + Metrics | `Program.cs` | 1h |
| 2.3 | 在关键路径添加埋点（Tool 调用、状态转换） | 多个服务文件 | 2h |
| 2.4 | 建立 Redis 会话缓存层 | 新建 `RedisConversationCache.cs` | 2h |
| 2.5 | 改造 `ConversationManager` 为双层架构 | `ConversationManager.cs` | 2h |
| 2.6 | 建立 Prompt 版本目录结构 + 版本解析逻辑（**含会话隔离机制**） | `PromptVersionResolver.cs`, `ai-config.yaml` | 2h |
| 2.7 | 实现 Prompt AB 测试路由 + 会话级配置快照 | `SessionConfigSnapshot.cs` | 2h |
| 2.8 | 改造 `YamlHotReloadService` 支持会话隔离通知 | `YamlHotReloadService.cs` | 1.5h |

### Phase 3 任务（智能化）

| # | 任务 | 涉及文件 | 预估工作量 |
|---|------|---------|-----------|
| 3.1 | 扩展 `sales_leads.yml` 新增归因字段 | `entities/sales_leads.yml` | 0.5h |
| 3.2 | 实现 RAG 向检索管道 | 新建 `VectorSearchService.cs` | 4h |
| 3.3 | 客户意图预测 Prompt + 服务 | 新建 `IntentPredictionService.cs` | 3h |
| 3.4 | 销售转化归因数据收集 | 修改 `AutoDealerChatService.cs` | 2h |

---

## 六、风险与应对

| 风险 | 影响 | 应对措施 |
|------|------|---------|
| FSM 状态过于僵化，无法处理用户自由对话 | 用户体验差 | 设计"脱线"触发器，允许临时切回 `INIT` 回答通用问题后自动返回原状态 |
| Redis 依赖增加运维成本 | 部署复杂度上升 | Phase 1 不使用 Redis，Phase 2 作为可选优化，无 Redis 时降级为纯 SQLite |
| LLM 输出格式不稳定 | Tool 校验频繁失败 | 在 System Prompt 中强化格式约束 + 校验失败时让 AI 重新输出 |
| 档期冲突检测在高并发下失效 | 重复预约 | 使用 SQLite `INSERT OR IGNORE` + 唯一索引 `(appointment_type, preferred_date, preferred_time)` |
| OpenTelemetry 性能开销 | 响应延迟增加 | 使用异步导出 + 采样率控制（生产环境 10% 采样） |

---

## 七、参考文档

| 文档 | 说明 |
|------|------|
| `docs/设计1.md` | 企业级架构设计原版 |
| `docs/设计2.md` | 改进建议（架构诊断与优化方案） |
| `docs/汽车销售系统AI接入扩展方案-补充材料.md` | **补充材料**：钩子模板/ESCALATE状态/SqlSafetyGuard/热重载隔离 |
| `docs/quickstart-ja.md` | NetYamlForge 快速入门 |
| `docs/developer-tutorial-ja.md` | 开发者教程 |
| `docs/COMMON_HOOKS.md` | 通用钩子列表 |
| `docs/HOTRELOAD.md` | 热重载说明 |
| [Stateless 文档](https://github.com/dotnet-state-machine/stateless) | .NET 状态机库 |
| [Polly 文档](https://github.com/App-vNext/Polly) | 弹性策略库 |
| [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/) | 可观测性框架 |

---

## 八、补充材料索引

### ✍️ IEntityHook 实现模板

| 模板 | 位置 | 说明 |
|------|------|------|
| PII 自动脱敏钩子 | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#11-pii-自动脱敏钩子) | 手机号/邮箱/姓名掩码 |
| AI 对话审计日志钩子 | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#12-ai-对话审计日志钩子) | 操作记录到 audit_log 表 |
| AI 线索自动评分钩子 | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#13-ai-线索自动评分钩子) | 根据意图/情感调整评分 |

### 📐 FSM 状态流转图

| 内容 | 位置 | 说明 |
|------|------|------|
| 完整状态流转图（PlantUML） | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#21-完整状态流转图plantuml) | 含 ESCALATE 路径 |
| 状态机实现代码 | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#22-状态机实现代码含-escalate) | `AppointmentStateMachine.cs` |
| 状态持久化 SQL | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#23-状态持久化) | 数据库迁移脚本 |

### 🔒 SqlSafetyGuard 集成

| 内容 | 位置 | 说明 |
|------|------|------|
| ToolCallValidator 实现 | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#31-tool-调用中的-sqlsafetyguard-应用) | 三重安全网关 |
| DynamicEntityCommandService 集成 | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#32-dynamicentitycommandservice-中的-sqlsafetyguard-应用) | 安全 SQL 构建 |
| 集成测试用例 | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#33-sqlsafetyguard-测试用例) | SQL 注入防护验证 |

### 🔥 热重载会话隔离

| 内容 | 位置 | 说明 |
|------|------|------|
| PromptVersionResolver | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#42-实现方案) | 版本路由基于 SessionId 哈希 |
| SessionConfigSnapshot | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#43-会话级配置快照) | 会话级配置不变性保证 |
| YamlHotReloadService 改造 | [补充材料](./汽车销售系统AI接入扩展方案-补充材料.md#44-yamlhotreloadservice-改造支持会话隔离) | SignalR 配置更新通知 |

---

*文档版本: 1.1 | 创建: 2026-04-09 | 更新: 2026-04-09*  
*基于: docs/设计1.md + docs/设计2.md + auto-dealer-demo 项目现状 + 补充材料*
