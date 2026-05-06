# AI 聊天功能分离方案

## 概述

将 NetYamlForge 框架中的 AI 聊天功能分离成独立的 .NET 项目（`NetYamlForge.AI`），同时维持现有功能不变。

---

## 可行性评估：✅ 可行（需要适配层）

### 优势
1. **清晰的接口边界**：`BaseChatService`、`ICLIService`、`ILlmProvider` 等接口已定义良好
2. **模块化设计**：AI 服务集中在 `Services/AI/` 目录
3. **依赖注入友好**：已通过 DI 容器管理服务生命周期

### 挑战
1. **对核心框架有依赖**：部分 AI 服务依赖 `ProjectScope`、`IDynamicCrudRepository`、`IEntityMetadataProvider` 等核心服务
2. **共享基础设施**：数据库连接、多租户、YAML 实体系统等被多个模块共享
3. **SQL 查询功能耦合**：`QueryExecutionService` 依赖动态实体系统

---

## 架构设计

### 方案选择

```
┌─────────────────────────────────────────────────────────┐
│                   主应用 (NetYamlForge)                   │
│                                                         │
│  - YAML 驱动 CRUD 系统                                    │
│  - 多租户管理 (ProjectScope)                              │
│  - 动态实体系统                                           │
│  - 钩子系统                                              │
│  - 页面生成                                               │
└──────────────────────┬──────────────────────────────────┘
                       │ HTTP API / gRPC
┌──────────────────────┴──────────────────────────────────┐
│              AI 聊天服务 (NetYamlForge.AI)                │
│                                                         │
│  - AI CLI 工具集成 (Claude/Qwen/Gemini/Ollama...)        │
│  - 对话管理 (Conversation/Message)                       │
│  - 意图分类 & 情感分析                                    │
│  - 聊天窗口 API (AIWindow)                                │
│  - SignalR Hub (实时通信)                                 │
│  - AI 仪表板                                             │
│  - 人工交接管理                                           │
│  - 预约管理 & Slot-filling                               │
│  - AI 查询 (NL → SQL)                                    │
└─────────────────────────────────────────────────────────┘
```

### 通信方式

| 选项 | 优点 | 缺点 |
|------|------|------|
| **HTTP REST API** | 简单、标准、易调试 | 性能略低 |
| **gRPC** | 高性能、强类型 | 配置复杂 |
| **类库引用** | 无网络开销 | 紧耦合 |

**推荐**：HTTP REST API + SignalR（维持现有实时通信能力）

---

## 模块划分

### 📦 需要提取到 NetYamlForge.AI 的模块

#### 1. 控制器 (Controllers) - 10 个
```
Controllers/
├── AIController.cs                          # AI CLI 工具聊天
├── AIDashboardController.cs                 # AI 仪表板
├── AIDebateController.cs                    # AI 辩论
├── AISettingsController.cs                  # AI 设置
├── Api/
│   ├── AIWindowController.cs                # 聊天窗口 API
│   ├── AIQueryController.cs                 # 自然语言查询
│   ├── AIAnalyticsController.cs             # 分析 API
│   ├── AIConversationDetailController.cs    # 对话详情
│   ├── AIDebateApiController.cs             # 辩论 API
│   ├── AIKnowledgeController.cs             # 知识库 API
│   ├── AIReportController.cs                # 报告 API
│   ├── JpiereChatController.cs              # JPiere 项目聊天
│   └── AutoDealerChatController.cs          # Auto Dealer 聊天
└── OperatorChatController.cs                # 操作员聊天
```

#### 2. 服务层 (Services/AI) - ~50 个
```
Services/AI/
├── BaseChatService.cs                       # ⚠️ 需要适配层
├── AutoDealerChatService.cs
├── JpiereChatService.cs
├── DirectAIProcessor.cs
├── AiToolOrchestrator.cs
├── AIProcessPoolManager.cs
├── PersistentAIProcess.cs
├── CLIServiceFactory.cs
├── ICLIService.cs
├── BaseCLIService.cs
├── PooledCLIService.cs
├── Providers/
│   ├── ClaudeCLIService.cs
│   ├── QwenCodeCLIService.cs
│   ├── CopilotCLIService.cs
│   ├── GeminiCLIService.cs
│   ├── CodexCLIService.cs
│   ├── OllamaCLIService.cs
│   ├── LMStudioCLIService.cs
│   ├── MockCLIService.cs
│   ├── ILlmProvider.cs
│   ├── CliFirstLlmProvider.cs
│   ├── HybridLlmProvider.cs
│   ├── OllamaProvider.cs
│   └── DashScopeApiProvider.cs
├── ConversationManager.cs                   # ⚠️ 需要适配层
├── ChatHistoryService.cs
├── TaskQueueService.cs
├── ProgressTracker.cs
├── HandoverManager.cs                       # ⚠️ 需要适配层
├── AppointmentService.cs
├── AppointmentStateMachine.cs
├── SlotFillingManager.cs                    # ⚠️ 需要适配层
├── HybridIntentClassifier.cs
├── CustomerDataService.cs                   # ⚠️ 需要适配层
├── KnowledgeBaseService.cs                  # ⚠️ 需要适配层
├── SentimentAnalyzer.cs
├── SkillLoader.cs
├── CliConfig.cs
├── QueryParserService.cs
├── QueryExecutionService.cs                 # ⚠️ 需要适配层
├── QueryResultFormatter.cs
├── QueryTemplateService.cs
├── AISettingsService.cs
├── AIReportPdfService.cs
├── AIDebateService.cs
├── AIDebateOrchestrator.cs
├── AIDebateDbService.cs
├── LlmResponseGenerator.cs
├── ProcessExecutor.cs
├── PromptVersionResolver.cs
├── SessionConfigSnapshot.cs
├── DaemonChatService.cs
├── DaemonChatServiceFactory.cs
├── DaemonProcessInstance.cs
├── DaemonMessageProtocol.cs
├── EmailChannelService.cs
├── ToolValidation/
│   └── ToolCallValidator.cs
└── Interfaces/
    ├── IConversationManager.cs
    ├── IIntentClassifier.cs
    ├── IHandoverManager.cs
    ├── ISentimentAnalyzer.cs
    ├── ICustomerDataService.cs
    ├── IAppointmentService.cs
    ├── IResponseGenerator.cs
    └── IOperatorChatService.cs
```

#### 3. 模型 (Models/AI) - ~20 个
```
Models/AI/
├── AIChatRequest.cs
├── AIChatResponse.cs
├── ChatMessage.cs
├── ChatRequests.cs
├── AITask.cs
├── Conversation.cs
├── Message.cs
├── CliToolInfo.cs
├── IntentResult.cs
├── HandoverRequest.cs
├── NaturalLanguageQuery.cs
├── ProgressUpdate.cs
├── TaskStatus.cs
├── UiComponents.cs
├── AIDebateModels.cs
├── AISettingsViewModel.cs
├── AiSkill.cs
├── AIWindowRequests.cs
└── CommandLog.cs
```

#### 4. SignalR Hubs - 3 个
```
Hubs/
├── AIChatHub.cs
├── AIDebateHub.cs
└── AIProgressHub.cs
```

#### 5. 视图 (Views) - ~10 个
```
Views/
├── AIDashboard/
│   ├── Index.cshtml
│   ├── ConversationDetail.cshtml
│   └── Handovers.cshtml
├── OperatorChat/
│   ├── Index.cshtml
│   ├── Detail.cshtml
│   └── _Messages.cshtml
├── Shared/
│   └── Components/
│       ├── AIAssistantPanel.cshtml
│       └── AIQueryChat/
│           ├── AIQueryChat.cshtml
│           └── AIQueryChatViewComponent.cs
└── Dashboard/
    └── AIQuery.cshtml
```

#### 6. 静态资源 (wwwroot) - ~8 个
```
wwwroot/
├── js/
│   ├── ai-chat-widget.js
│   ├── ai-chat-components.js
│   ├── ai-query-chat.js
│   ├── ai-debate.js
│   └── ai-debate-topic.js
└── css/
    ├── ai-chat-components.css
    ├── ai-query-chat.css
    └── ai-debate.css
```

---

### 🔗 需要适配的依赖项

#### 高优先级依赖（必须处理）

| 依赖项 | 来源模块 | 使用方 | 解决方案 |
|--------|---------|--------|---------|
| `ProjectScope` | `Services/Project` | ConversationManager, HandoverManager 等 | **创建接口适配** `IAIProjectContext` |
| `IDbConnectionFactory` | `Services/BatchJob` | 多个 AI 服务 | **传递连接字符串**或使用独立工厂 |
| `IDynamicCrudRepository` | `Services/DynamicEntity` | QueryExecutionService | **创建简化接口** `IAIQueryExecutor` |
| `IEntityMetadataProvider` | `Services/Project` | QueryExecutionService | **通过 API 获取元数据** |
| `SqlSafetyGuard` | `Services` | QueryExecutionService, ToolCallValidator | **复制到 AI 项目** |
| `ProjectInfo` | `Services/Project` | 多个 AI 服务 | **创建简化 DTO** |
| `IDbConnection` (Dapper) | NuGet | BaseChatService, ConversationManager | **NuGet 依赖** ✅ |

#### 中优先级依赖（可选优化）

| 依赖项 | 来源模块 | 使用方 | 解决方案 |
|--------|---------|--------|---------|
| `Services.BatchJob` | 批处理系统 | SlotFillingManager 等 | **移除依赖**或使用回调 |
| `Services.Page` | 页面系统 | 少数服务 | **移除依赖** |
| `YamlSchemaValidator` | YAML 验证 | 配置加载 | **复制必要逻辑** |

---

## 实施步骤

### Phase 1: 创建基础项目结构（1-2 天）

```bash
# 1. 创建新的类库项目
dotnet new classlib -n NetYamlForge.AI -f net10.0

# 2. 创建 Web 项目（如果需要独立运行）
dotnet new web -n NetYamlForge.AI.Web -f net10.0

# 3. 添加 NuGet 依赖
dotnet add NetYamlForge.AI package Dapper
dotnet add NetYamlForge.AI package Microsoft.AspNetCore.SignalR
dotnet add NetYamlForge.AI package Microsoft.Extensions.Logging
dotnet add NetYamlForge.AI package System.Text.Json
dotnet add NetYamlForge.AI package Stateless  # 状态机

# 4. 添加项目引用（开发阶段）
dotnet add NetYamlForge.AI.Web reference NetYamlForge.AI
```

### Phase 2: 创建接口适配层（2-3 天）

在 `NetYamlForge.AI` 中创建接口，解耦对主框架的依赖：

```csharp
// NetYamlForge.AI/Infrastructure/IAIProjectContext.cs
namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// AI 聊天服务的项目上下文接口（适配层）
/// </summary>
public interface IAIProjectContext
{
    string ProjectName { get; }
    string GetDatabasePath();
    IDbConnection CreateConnection();
}

// NetYamlForge.AI/Infrastructure/IAIQueryExecutor.cs
namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// AI 查询执行接口（适配层）
/// </summary>
public interface IAIQueryExecutor
{
    Task<QueryResult> ExecuteQueryAsync(string entity, QueryParameters parameters);
    Task<IEnumerable<EntityMetadata>> GetEntityMetadataAsync();
}
```

在主项目中实现这些接口：

```csharp
// NetYamlForge/Services/AI/Adapters/MainAppProjectContext.cs
namespace NetYamlForge.Services.AI.Adapters;

public class MainAppProjectContext : IAIProjectContext
{
    private readonly ProjectScope _projectScope;
    private readonly IDbConnectionFactory _dbFactory;
    
    public string ProjectName => _projectScope.Current.Name;
    
    public IDbConnection CreateConnection() => _dbFactory.CreateConnection();
    
    // ... 其他实现
}
```

### Phase 3: 迁移核心服务（3-5 天）

#### 3.1 复制文件并调整命名空间

```bash
# 目录结构
NetYamlForge.AI/
├── Controllers/          # 从主项目复制
├── Services/             # 从主项目复制 Services/AI/
├── Models/               # 从主项目复制 Models/AI/
├── Hubs/                 # 从主项目复制
├── Infrastructure/       # 新增：适配层接口
├── AIConfiguration.cs    # AI 配置类
└── AIServiceCollectionExtensions.cs  # DI 注册扩展
```

#### 3.2 修改命名空间

```csharp
// 之前
namespace NetYamlForge.Services.AI;

// 之后
namespace NetYamlForge.AI.Services;
```

#### 3.3 替换依赖项

```csharp
// 之前（在主项目中）
public class ConversationManager : IConversationManager
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly ProjectScope _projectScope;
}

// 之后（在 AI 项目中）
public class ConversationManager : IConversationManager
{
    private readonly IAIProjectContext _projectContext;
    
    private IDbConnection CreateConnection() => 
        _projectContext.CreateConnection();
}
```

### Phase 4: 创建 DI 注册扩展（1-2 天）

```csharp
// NetYamlForge.AI/AIServiceCollectionExtensions.cs
namespace NetYamlForge.AI;

public static class AIServiceCollectionExtensions
{
    public static IServiceCollection AddNetYamlForgeAI(
        this IServiceCollection services,
        Action<AIOptions> configureOptions)
    {
        // 配置
        services.Configure(configureOptions);
        
        // 基础设施
        services.AddScoped<IAIProjectContext, DefaultAIProjectContext>();
        services.AddSingleton<CLIServiceFactory>();
        services.AddSingleton<TaskQueueService>();
        
        // CLI 提供商
        services.AddSingleton<ICLIService, ClaudeCLIService>();
        services.AddSingleton<ICLIService, QwenCodeCLIService>();
        // ... 其他提供商
        
        // 核心服务
        services.AddScoped<IConversationManager, ConversationManager>();
        services.AddScoped<IHandoverManager, HandoverManager>();
        services.AddScoped<IDirectAIProcessor, DirectAIProcessor>();
        
        // 控制器
        services.AddControllers()
            .AddApplicationPart(typeof(AIController).Assembly);
            
        return services;
    }
}
```

### Phase 5: 修改主项目集成（1-2 天）

#### 5.1 添加项目引用

```xml
<!-- NetYamlForge/NetYamlForge.csproj -->
<ItemGroup>
  <ProjectReference Include="..\NetYamlForge.AI\NetYamlForge.AI.csproj" />
</ItemGroup>
```

#### 5.2 注册 AI 服务

```csharp
// NetYamlForge/Program.cs
builder.Services.AddNetYamlForgeAI(options =>
{
    options.CliConfig = builder.Configuration.GetSection("AICli").Get<CliConfig>();
    options.AiWindow = builder.Configuration.GetSection("AiWindow").Get<AiWindowConfig>();
});
```

#### 5.3 实现适配接口

```csharp
// NetYamlForge/Services/AI/Adapters/MainAppAIProjectContext.cs
builder.Services.AddScoped<IAIProjectContext, MainAppAIProjectContext>();
builder.Services.AddScoped<IAIQueryExecutor, AIQueryExecutorAdapter>();
```

### Phase 6: 测试与验证（2-3 天）

```bash
# 1. 构建 AI 项目
dotnet build NetYamlForge.AI

# 2. 运行 AI 项目测试
dotnet test NetYamlForge.AI.Tests

# 3. 集成测试
dotnet test NetYamlForge.Tests --filter "FullyQualifiedName~AI"

# 4. 端到端测试
dotnet run --project NetYamlForge
```

---

## 数据库设计

### 选项 A：共享数据库（推荐）

AI 服务继续使用主项目的数据库连接，表前缀区分：

```sql
-- AI 聊天相关表
ai_conversations
ai_messages
ai_handovers
ai_appointments
ai_knowledge_base
ai_chat_history

-- 主应用表
entities
pages
hooks
...
```

### 选项 B：独立数据库

AI 服务使用独立的 SQLite/PostgreSQL 数据库：

```json
{
  "AI": {
    "DatabaseProvider": "sqlite",
    "ConnectionString": "Data Source=ai-chat.db"
  }
}
```

**推荐选项 A**：简化管理，保持事务一致性

---

## 配置管理

### AI 项目独立配置

```csharp
// NetYamlForge.AI/AIOptions.cs
namespace NetYamlForge.AI;

public class AIOptions
{
    public CliConfig CliConfig { get; set; } = new();
    public AiWindowConfig AiWindow { get; set; } = new();
    public string SkillsDirectory { get; set; } = "skills/";
    public int TaskTimeoutSeconds { get; set; } = 1800;
    public int MaxConcurrentTasks { get; set; } = 2;
}
```

### 主项目配置（保持不变）

```json
// appsettings.json（主项目）
{
  "AICli": {
    "DefaultTool": "qwen",
    "TaskTimeoutSeconds": 1800,
    "MaxConcurrentTasks": 2,
    "Claude": { "Path": "claude" },
    "QwenCode": { "Path": "qwen" }
  },
  "AiWindow": {
    "CliFirst": true,
    "ProviderPriority": ["qwen", "claude"]
  }
}
```

---

## 部署方案

### 方案 1：嵌入式部署（推荐初期使用）

AI 项目作为类库被主项目引用，一起部署：

```
NetYamlForge.exe
├── NetYamlForge.AI.dll
├── NetYamlForge.Analyzers.dll
└── wwwroot/
```

**优点**：
- 部署简单
- 无网络开销
- 调试方便

**缺点**：
- 未能独立扩展

### 方案 2：微服务部署（推荐生产环境）

AI 服务独立进程运行，通过 HTTP/gRPC 通信：

```
┌─────────────┐      HTTP API       ┌──────────────┐
│  NetYaml    │ ◄─────────────────► │  AI Service  │
│   Forge     │                     │   (独立)      │
│  (主应用)    │                     │              │
└─────────────┘                     └──────────────┘
```

**优点**：
- 独立扩展
- 故障隔离
- 可单独更新

**缺点**：
- 部署复杂
- 需要服务发现
- 网络延迟

---

## 风险评估

### 🔴 高风险

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| 接口适配不完整 | AI 功能异常 | 全面的集成测试 |
| 数据库迁移问题 | 数据丢失 | 备份 + 迁移脚本 |
| 性能下降 | 响应时间增加 | 性能基准测试 |

### 🟡 中风险

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| 配置同步问题 | 功能异常 | 配置验证启动检查 |
| 版本兼容性 | 构建失败 | 统一 .NET 版本 |
| SignalR 路由 | 实时通信失败 | 路由表更新 |

### 🟢 低风险

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| 命名空间冲突 | 编译警告 | 全面重命名 |
| 文档过时 | 维护困难 | 同步更新文档 |

---

## 时间估算

| 阶段 | 时间 | 说明 |
|------|------|------|
| Phase 1: 项目结构 | 1-2 天 | 创建项目、添加依赖 |
| Phase 2: 适配层 | 2-3 天 | 接口定义与实现 |
| Phase 3: 服务迁移 | 3-5 天 | 复制文件、调整代码 |
| Phase 4: DI 注册 | 1-2 天 | 扩展方法编写 |
| Phase 5: 集成 | 1-2 天 | 主项目适配 |
| Phase 6: 测试 | 2-3 天 | 单元+集成测试 |
| **总计** | **10-17 天** | 取决于复杂度 |

---

## 后续优化建议

### 短期（分离完成后）

1. **提取项目专用聊天服务**
   - `AutoDealerChatService` → 独立插件
   - `JpiereChatService` → 独立插件

2. **优化数据库访问**
   - 添加仓储层抽象
   - 实现连接池管理

3. **完善测试覆盖**
   - 单元测试 > 80%
   - 集成测试覆盖关键流程

### 中期（1-3 个月）

1. **API 版本化**
   - `/api/v1/ai/...`
   - 支持向后兼容

2. **缓存优化**
   - 缓存实体元数据
   - 缓存 LLM 响应

3. **监控与日志**
   - OpenTelemetry 集成
   - 性能指标收集

### 长期（3-6 个月）

1. **微服务化**
   - 独立部署 AI 服务
   - 容器化部署（Docker/K8s）

2. **多实例支持**
   - Redis 分布式缓存
   - 消息队列解耦

3. **功能增强**
   - 更多 AI 提供商
   - 更智能的意图识别
   - 知识库自动学习

---

## 结论

### ✅ 可以分离

AI 聊天功能具备良好的模块化设计，通过创建**接口适配层**可以成功解耦对主框架的依赖。

### 推荐策略

1. **初期**：使用**嵌入式部署**（类库引用），快速完成分离
2. **中期**：根据需要选择**独立进程部署**（HTTP API）
3. **长期**：考虑**微服务架构**，实现完全独立

### 关键成功因素

- ✅ 完善的接口适配层
- ✅ 全面的测试覆盖
- ✅ 渐进式迁移策略
- ✅ 保持配置兼容性

---

*文档创建时间：2026-04-11*
