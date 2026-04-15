# AI 窗口系统 - 快速开发入门指南

## 目录

1. [环境准备](#环境准备)
2. [项目结构](#项目结构)
3. [第一个 Hello World](#第一个-hello-world)
4. [本地开发](#本地开发)
5. [常见任务](#常见任务)
6. [故障排除](#故障排除)

---

## 环境准备

### 系统要求

- **操作系统**: Windows 11 / macOS / Linux (Ubuntu 22.04+)
- **.NET**: .NET 10 SDK 及以上
- **Docker**: Docker Desktop (推荐用于数据库)
- **Node.js**: 18+ (前端开发)

### 安装步骤

#### 1. 克隆仓库

```bash
git clone https://github.com/yourcompany/NetYamlForge.git
cd NetYamlForge
```

#### 2. 启动开发环境

```bash
# 使用 Docker Compose 启动依赖服务
docker-compose -f docker-compose.yml up -d

# 验证服务状态
docker ps
```

你应该看到:
- `postgres` (5432)
- `redis` (6379)
- `rabbitmq` (5672)

#### 3. 还原依赖包

```bash
dotnet restore
```

#### 4. 配置 appsettings

创建 `NetYamlForge/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ai_window;Username=postgres;Password=postgres;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Warning"
    }
  },
  "AI": {
    "LLM": {
      "Provider": "qwen",
      "ApiKey": "YOUR_QWEN_API_KEY"
    }
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
}
```

#### 5. 初始化数据库

```bash
# 应用迁移
dotnet ef database update --project NetYamlForge

# 创建示例数据
dotnet run --project NetYamlForge -- --init-project --project=auto-dealer-demo --display-name="Auto Dealer Demo" --db-type=sqlite
```

#### 6. 启动应用

```bash
dotnet run --project NetYamlForge

# 应用将在 http://localhost:5000 启动
```

---

## 项目结构

### 核心 AI 模块位置

```
NetYamlForge/
├── Services/
│   └── AI/
│       ├── BaseCLIService.cs              # CLI 基类
│       ├── ChatHistoryService.cs          # 对话历史
│       ├── ProgressTracker.cs             # 进度追踪
│       ├── TaskQueueService.cs            # 任务队列
│       ├── SkillLoader.cs                 # 技能加载
│       ├── ProcessExecutor.cs             # 进程执行
│       ├── Providers/                     # LLM 提供商实现
│       └── CustomerAI/                    # ⭐ 新增: 客户 AI 模块
│           ├── ConversationManager.cs
│           ├── IntentClassifier.cs
│           ├── ResponseGenerator.cs
│           ├── CustomerDataService.cs
│           ├── AppointmentService.cs
│           └── HandoverManager.cs
│
├── Controllers/
│   ├── AIController.cs                    # 现有 AI 控制器
│   └── Api/
│       └── AIWindowController.cs          # ⭐ 新增: AI 窗口 API
│
├── Models/
│   └── AI/
│       ├── TaskStatus.cs                  # 现有模型
│       └── CustomerAI/                    # ⭐ 新增: 客户 AI 模型
│           ├── ConversationContext.cs
│           ├── Message.cs
│           ├── IntentResult.cs
│           ├── AiResponse.cs
│           └── HandoverDecision.cs
│
├── Hubs/
│   ├── AIProgressHub.cs                   # 现有进度 Hub
│   └── AIChatHub.cs                       # ⭐ 新增: 实时聊天 Hub
│
├── projects/
│   └── auto-dealer-demo/
│       ├── config/
│       │   └── ai-window.yml              # ⭐ AI 窗口配置
│       ├── entities/
│       │   └── ai-conversation.yml        # ⭐ AI 对话数据表
│       └── Hooks/
│           └── AIWindowHooks.cs           # ⭐ AI 相关 Hooks
│
└── wwwroot/
    └── js/
        └── ai-chat-widget.js              # ⭐ Web 聊天框脚本
```

---

## 第一个 Hello World

### 任务: 构建一个简单的客户问候机器人

#### Step 1: 创建意图分类器

创建文件 `NetYamlForge/Services/AI/CustomerAI/SimpleIntentClassifier.cs`:

```csharp
using NetYamlForge.Models.AI.CustomerAI;

namespace NetYamlForge.Services.AI.CustomerAI
{
    /// <summary>
    /// 简单的规则基础意图分类器（Hello World 版本）
    /// </summary>
    public class SimpleIntentClassifier : IIntentClassifier
    {
        private readonly ILogger<SimpleIntentClassifier> _logger;

        public SimpleIntentClassifier(ILogger<SimpleIntentClassifier> logger)
        {
            _logger = logger;
        }

        public Task<IntentResult> ClassifyAsync(
            string userMessage,
            ConversationContext context,
            CancellationToken ct = default)
        {
            _logger.LogInformation($"Classifying message: {userMessage}");

            // 简单规则匹配
            string intent = "unknown";
            double confidence = 0.0;

            if (userMessage.Contains("こんにちは") || userMessage.Contains("hello"))
            {
                intent = "greeting";
                confidence = 0.95;
            }
            else if (userMessage.Contains("営業時間") || userMessage.Contains("hours"))
            {
                intent = "business_hours";
                confidence = 0.9;
            }
            else if (userMessage.Contains("予約") || userMessage.Contains("appointment"))
            {
                intent = "book_appointment";
                confidence = 0.85;
            }

            return Task.FromResult(new IntentResult
            {
                Intent = intent,
                Confidence = confidence,
                Entities = new Dictionary<string, string>(),
                Suggestions = new List<string> { "了解更多", "预约服务", "转接人工" }
            });
        }

        public Task<List<IntentResult>> ClassifyBatchAsync(
            List<string> messages,
            CancellationToken ct = default)
        {
            var results = messages.Select(msg => 
                ClassifyAsync(msg, null, ct).Result
            ).ToList();
            
            return Task.FromResult(results);
        }
    }
}
```

#### Step 2: 创建简单的回复生成器

创建文件 `NetYamlForge/Services/AI/CustomerAI/SimpleResponseGenerator.cs`:

```csharp
namespace NetYamlForge.Services.AI.CustomerAI
{
    /// <summary>
    /// 简单的模板基础回复生成器
    /// </summary>
    public class SimpleResponseGenerator : IResponseGenerator
    {
        private readonly ILogger<SimpleResponseGenerator> _logger;

        public SimpleResponseGenerator(ILogger<SimpleResponseGenerator> logger)
        {
            _logger = logger;
        }

        public Task<AiResponse> GenerateResponseAsync(
            IntentResult intent,
            ConversationContext context,
            CancellationToken ct = default)
        {
            _logger.LogInformation($"Generating response for intent: {intent.Intent}");

            string textContent = intent.Intent switch
            {
                "greeting" => "👋 こんにちは！自動車ディーラーの AI アシスタントです。本日はどのようなご用件ですか？",
                "business_hours" => "📍 弊社の営業時間は以下の通りです：\n平日 9:00-18:00\n土曜 9:00-17:00\n日曜・祝日は休業です",
                "book_appointment" => "📅 予約をご希望ですか？サービス内容をお聞きします。",
                _ => "申し訳ございません。もう一度お聞きできますか？"
            };

            return Task.FromResult(new AiResponse
            {
                ResponseId = Guid.NewGuid().ToString(),
                TextContent = textContent,
                ResponseType = ResponseType.Text,
                Confidence = intent.Confidence,
                SuggestHandover = intent.Confidence < 0.5,
                GeneratedAt = DateTime.UtcNow
            });
        }
    }
}
```

#### Step 3: 创建对话管理器

创建文件 `NetYamlForge/Services/AI/CustomerAI/InMemoryConversationManager.cs`:

```csharp
using System.Collections.Concurrent;

namespace NetYamlForge.Services.AI.CustomerAI
{
    /// <summary>
    /// 基于内存的对话管理器（用于开发/演示）
    /// 生产环境应使用数据库持久化版本
    /// </summary>
    public class InMemoryConversationManager : IConversationManager
    {
        private readonly ConcurrentDictionary<string, ConversationContext> _conversations;
        private readonly ILogger<InMemoryConversationManager> _logger;

        public InMemoryConversationManager(ILogger<InMemoryConversationManager> logger)
        {
            _conversations = new ConcurrentDictionary<string, ConversationContext>();
            _logger = logger;
        }

        public Task<ConversationContext> StartConversationAsync(
            string userId,
            string channel,
            string projectId,
            CancellationToken ct = default)
        {
            var conversationId = Guid.NewGuid().ToString();
            
            var context = new ConversationContext
            {
                ConversationId = conversationId,
                UserId = userId,
                Channel = channel,
                ProjectId = projectId,
                MessageHistory = new Stack<Message>(),
                Metadata = new Dictionary<string, object>(),
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow
            };

            _conversations.TryAdd(conversationId, context);
            _logger.LogInformation($"Started conversation {conversationId} for user {userId}");

            return Task.FromResult(context);
        }

        public Task<ConversationContext?> GetConversationAsync(
            string conversationId,
            CancellationToken ct = default)
        {
            _conversations.TryGetValue(conversationId, out var context);
            return Task.FromResult(context);
        }

        public Task UpdateContextAsync(
            ConversationContext context,
            CancellationToken ct = default)
        {
            context.LastActivity = DateTime.UtcNow;
            _conversations.AddOrUpdate(context.ConversationId, context, (_, _) => context);
            return Task.CompletedTask;
        }

        public Task AddMessageAsync(
            string conversationId,
            Message message,
            CancellationToken ct = default)
        {
            if (_conversations.TryGetValue(conversationId, out var context))
            {
                context.MessageHistory.Push(message);
                context.LastActivity = DateTime.UtcNow;
                _logger.LogDebug($"Added message to conversation {conversationId}");
            }

            return Task.CompletedTask;
        }

        public Task<List<Message>> GetMessageHistoryAsync(
            string conversationId,
            int limit = 20,
            CancellationToken ct = default)
        {
            if (_conversations.TryGetValue(conversationId, out var context))
            {
                return Task.FromResult(context.MessageHistory.Take(limit).ToList());
            }

            return Task.FromResult(new List<Message>());
        }

        public Task CleanExpiredConversationsAsync(CancellationToken ct = default)
        {
            var expiredIds = _conversations
                .Where(kvp => kvp.Value.IsExpired)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in expiredIds)
            {
                _conversations.TryRemove(id, out _);
                _logger.LogInformation($"Removed expired conversation {id}");
            }

            return Task.CompletedTask;
        }
    }
}
```

#### Step 4: 注册依赖注入

在 `NetYamlForge/Program.cs` 中添加:

```csharp
// 添加 AI Window Services
services.AddScoped<IIntentClassifier, SimpleIntentClassifier>();
services.AddScoped<IResponseGenerator, SimpleResponseGenerator>();
services.AddScoped<IConversationManager, InMemoryConversationManager>();
```

#### Step 5: 创建 API 端点

创建文件 `NetYamlForge/Controllers/Api/AIWindowController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.AI.CustomerAI;
using NetYamlForge.Models.AI.CustomerAI;

namespace NetYamlForge.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIWindowController : ControllerBase
    {
        private readonly IConversationManager _conversationManager;
        private readonly IIntentClassifier _intentClassifier;
        private readonly IResponseGenerator _responseGenerator;
        private readonly ILogger<AIWindowController> _logger;

        public AIWindowController(
            IConversationManager conversationManager,
            IIntentClassifier intentClassifier,
            IResponseGenerator responseGenerator,
            ILogger<AIWindowController> logger)
        {
            _conversationManager = conversationManager;
            _intentClassifier = intentClassifier;
            _responseGenerator = responseGenerator;
            _logger = logger;
        }

        /// <summary>
        /// 启动新的对话
        /// </summary>
        [HttpPost("conversations")]
        public async Task<IActionResult> StartConversation(
            [FromBody] StartConversationRequest request)
        {
            var context = await _conversationManager.StartConversationAsync(
                request.UserId,
                request.Channel ?? "web",
                request.ProjectId ?? "auto-dealer-demo"
            );

            return Ok(new { conversationId = context.ConversationId });
        }

        /// <summary>
        /// 发送消息并获取 AI 回复
        /// </summary>
        [HttpPost("conversations/{conversationId}/messages")]
        public async Task<IActionResult> SendMessage(
            string conversationId,
            [FromBody] SendMessageRequest request)
        {
            _logger.LogInformation($"Received message for conversation {conversationId}: {request.Content}");

            // 获取对话上下文
            var context = await _conversationManager.GetConversationAsync(conversationId);
            if (context == null)
            {
                return NotFound("Conversation not found");
            }

            // 添加用户消息
            var userMessage = new Message
            {
                ConversationId = conversationId,
                Sender = "user",
                Content = request.Content,
                Type = MessageType.Text
            };
            await _conversationManager.AddMessageAsync(conversationId, userMessage);

            // 分类意图
            var intent = await _intentClassifier.ClassifyAsync(request.Content, context);
            context.CurrentIntent = intent.Intent;
            context.IntentConfidence = intent.Confidence;

            // 生成回复
            var response = await _responseGenerator.GenerateResponseAsync(intent, context);

            // 添加 AI 消息
            var aiMessage = new Message
            {
                ConversationId = conversationId,
                Sender = "ai",
                Content = response.TextContent,
                Type = response.ResponseType,
                Metadata = new Dictionary<string, object>
                {
                    { "confidence", response.Confidence },
                    { "intent", intent.Intent }
                }
            };
            await _conversationManager.AddMessageAsync(conversationId, aiMessage);

            // 更新上下文
            await _conversationManager.UpdateContextAsync(context);

            return Ok(new
            {
                messageId = response.ResponseId,
                content = response.TextContent,
                confidence = response.Confidence,
                suggestHandover = response.SuggestHandover
            });
        }

        public class StartConversationRequest
        {
            public string UserId { get; set; }
            public string Channel { get; set; }
            public string ProjectId { get; set; }
        }

        public class SendMessageRequest
        {
            public string Content { get; set; }
        }
    }
}
```

#### Step 6: 测试 API

```bash
# 启动应用
dotnet run --project NetYamlForge

# 新建对话
curl -X POST http://localhost:5000/api/aiwindow/conversations \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "user_001",
    "channel": "web",
    "projectId": "auto-dealer-demo"
  }'

# 返回: {"conversationId":"xxxx-xxxx-xxxx"}

# 发送消息
curl -X POST http://localhost:5000/api/aiwindow/conversations/xxxx-xxxx-xxxx/messages \
  -H "Content-Type: application/json" \
  -d '{
    "content": "こんにちは"
  }'

# 返回: {"messageId":"...","content":"👋 こんにちは！...","confidence":0.95}
```

✅ **恭喜！** 你已经创建了第一个工作的 AI 客户服务机器人！

---

## 本地开发

### 调试技巧

#### 1. 启用详细日志

在 `appsettings.Development.json` 中:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "NetYamlForge.Services.AI": "Debug"
    }
  }
}
```

#### 2. 使用 VS Code 调试

创建 `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": ".NET Core Launch (web)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/bin/Debug/net10.0/NetYamlForge.dll",
      "args": [],
      "cwd": "${workspaceFolder}",
      "stopAtEntry": false,
      "serverReadyAction": {
        "pattern": "\\bNow listening on:\\s+(https?://\\S+)",
        "uriFormat": "{0}",
        "action": "openExternally"
      }
    }
  ]
}
```

#### 3. 使用 Redis 查看缓存

```bash
redis-cli
> keys *
> get conversation:conv_123
```

### 单元测试

创建测试文件 `NetYamlForge.Tests/Services/AI/CustomerAI/SimpleIntentClassifierTests.cs`:

```csharp
using Xunit;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI.CustomerAI;

namespace NetYamlForge.Tests.Services.AI.CustomerAI
{
    public class SimpleIntentClassifierTests
    {
        private readonly ILogger<SimpleIntentClassifier> _logger;
        private readonly SimpleIntentClassifier _classifier;

        public SimpleIntentClassifierTests()
        {
            _logger = new XunitLoggerFactory().CreateLogger<SimpleIntentClassifier>();
            _classifier = new SimpleIntentClassifier(_logger);
        }

        [Fact]
        public async Task ClassifyAsync_Greeting_ReturnsGreetingIntent()
        {
            // Arrange
            var message = "こんにちは";

            // Act
            var result = await _classifier.ClassifyAsync(message, null);

            // Assert
            Assert.Equal("greeting", result.Intent);
            Assert.True(result.Confidence > 0.9);
        }

        [Fact]
        public async Task ClassifyAsync_UnknownMessage_ReturnsLowConfidence()
        {
            // Arrange
            var message = "xyzabc123";

            // Act
            var result = await _classifier.ClassifyAsync(message, null);

            // Assert
            Assert.Equal("unknown", result.Intent);
            Assert.True(result.Confidence < 0.1);
        }
    }
}
```

运行测试:

```bash
dotnet test --filter "SimpleIntentClassifierTests"
```

---

## 常见任务

### 任务 1: 添加新的意图

1. 在 `SimpleIntentClassifier.cs` 中添加规则
2. 在 `SimpleResponseGenerator.cs` 中添加模板
3. 编写单元测试
4. 测试 API

### 任务 2: 集成真实 LLM（Qwen）

```csharp
// 创建 Qwen 分类器
public class QwenIntentClassifier : IIntentClassifier
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public async Task<IntentResult> ClassifyAsync(
        string userMessage,
        ConversationContext context,
        CancellationToken ct = default)
    {
        var prompt = $@"
用户消息: {userMessage}

请分类这条消息的意图，返回 JSON:
{{
  ""intent"": ""greeting|business_hours|book_appointment|...|unknown"",
  ""confidence"": 0.0-1.0,
  ""entities"": {{...}}
}}
";
        
        // 调用 Qwen API...
    }
}
```

### 任务 3: 连接真实的客户数据库

```csharp
public class DatabaseCustomerDataService : ICustomerDataService
{
    private readonly DynamicCrudRepository _repository;
    private readonly ProjectScope _projectScope;

    public async Task<CustomerProfile?> GetCustomerProfileAsync(
        string customerId,
        string projectId,
        CancellationToken ct = default)
    {
        // 使用 DynamicCrudRepository 查询客户表
        var customer = await _repository.GetByIdAsync("Customer", customerId, ct);
        
        if (customer == null)
            return null;

        return new CustomerProfile
        {
            CustomerId = customerId,
            Name = customer["name"]?.ToString(),
            PhoneNumber = customer["phone"]?.ToString(),
            // ... 映射其他字段
        };
    }
}
```

---

## 故障排除

### Q: 数据库连接失败

**A:** 检查 Docker 容器是否运行:

```bash
docker ps
docker logs postgres
```

### Q: LLM API 超时

**A:** 增加超时时间:

```json
{
  "AI": {
    "LLM": {
      "Timeout": 60000
    }
  }
}
```

### Q: Redis 缓存清空不工作

**A:** 手动清理:

```bash
redis-cli FLUSHALL
```

### Q: 对话历史消失

**A:** 使用内存管理器时，重启应用会清空。使用数据库版本以持久化数据。

---

## 下一步

1. **阅读完整计划**: `AI-WINDOW-SYSTEM-IMPLEMENTATION-PLAN.md`
2. **查看配置示例**: `AI-WINDOW-CONFIG-EXAMPLE.yaml`
3. **实现阶段 1** (2 周):
   - [ ] 数据库模型
   - [ ] ConversationManager
   - [ ] LLM 集成
4. **实现阶段 2** (2 周):
   - [ ] 业务数据集成
   - [ ] 预约系统
   - [ ] 转接逻辑

---

**祝你编码愉快！** 🚀

有问题？在 GitHub Issues 中提出！
