# AI 窗口系统（CustomerAI）- 详细实现计划

*基于 NetYamlForge 框架的自动车经销商 AI 客服系统建设方案*

---

## 📋 目录

1. [系统概述](#系统概述)
2. [需求分析](#需求分析)
3. [架构设计](#架构设计)
4. [技术栈选择](#技术栈选择)
5. [核心模块设计](#核心模块设计)
6. [集成点规划](#集成点规划)
7. [实现路线图](#实现路线图)
8. [数据模型设计](#数据模型设计)
9. [API 接口设计](#api-接口设计)
10. [开发清单](#开发清单)
11. [部署架构](#部署架构)

---

## 系统概述

### 名称与目标
**系统名:** CustomerAI（自动车经销商 AI 窗口系统）

**总体目标:**
- 通过 AI 技术实现 24/7 自动化客服
- 降低人工成本，提升客户体验
- 支持多渠道整合（Web、LINE、メール、チャット）
- 智能路由复杂问题给人工客服

### 核心价值主张

| 价值点 | 说明 |
|------|------|
| **24/7 可用性** | 营业时间外自动应答 |
| **成本降低** | 减少简单问询的人工处理 |
| **客户体验提升** | 即时响应、快速预约 |
| **数据积累** | 建立完整的客服对话历史 |
| **精准转接** | 需要时自动路由给合适的人员 |

### 用户角色

| 角色 | 交互方式 | 场景 |
|------|--------|------|
| **一般客户** | Web聊天框、LINE | 营业咨询、预约、售后查询 |
| **VIP客户** | 优先接入人工 | 需要专属服务 |
| **网站访客** | 网页聊天框 | 营销线索获取 |
| **客服代表** | 仪表板 + 消息队列 | 接管 AI 无法处理的对话 |
| **管理员** | 训练数据管理后台 | AI 模型优化 |

---

## 需求分析

### 功能需求

#### 1. AI 对话引擎

**自然语言理解 (NLU)**
- 识别客户意图（营业咨询、预约、查询、投诉）
- 提取关键信息（日期、车型、服务类型等）
- 上下文管理（维持多轮对话上下文）

**自动应答能力**
```
营业咨询 → FAQ 知识库
  ├─ "営業時間は？" → 返回营业时间
  ├─ "新車の価格は？" → 返回车型价格
  └─ "ローン条件は？" → 返回金融方案

預約受付 → 日程系统集成
  ├─ "来週のサービス予約をしたい" 
  │  → 检查可用时段 → 确认预约
  └─ "明日の点検をキャンセルしたい"
     → 验证客户 → 取消预约

顧客問い合わせ → 数据库查询
  ├─ "私の契約情報は？"
  │  → 认证客户 → 返回合同详情
  ├─ "ローン残額は？"
  │  → 查询财务系统 → 返回余额
  └─ "次の点検はいつですか？"
     → 查询服务历史 → 返回建议时间

投诉/升级 → 人工转接
  ├─ 负面情绪检测 → 立即转接
  ├─ 无法解答的问题 → 标记为待处理
  └─ 高优先级客户 → 优先队列
```

**情感分析**
- 检测客户负面情绪
- 自动升级为人工服务
- 记录满意度评分

#### 2. 多渠道集成

**Web 聊天框**
- 嵌入网站首页、产品页面
- 支持访客身份进入
- 离线消息留言功能

**LINE 集成**
- 与经销商 LINE 官方账号关联
- 支持 Rich Menu 快速操作
- 位置分享、图片上传

**Email 网关**
- 邮件智能分类
- 自动回复 + AI 生成回复建议
- 转接人工时自动抄送

**内部仪表板**
- 实时对话队列显示
- 客服代表人工接管界面
- 对话历史搜索与分析

#### 3. 业务数据集成

**客户信息查询**
- 基于客户 ID 查询合同、付款、车辆信息
- 安全认证（验证电话号码/邮箱）
- 隐私保护（非客户数据隐藏）

**预约系统集成**
- 实时读取可用时段
- AI 自动推荐最佳时间
- 确认预约直接入库

**服务历史查询**
- 过去服务记录
- 下次推荐服务时期
- 保修状态

**库存查询**
- 新车库存及价格
- 中古车库存搜索
- 特价车型推荐

#### 4. 智能转接

**判断规则**
```
┌─ 能解答？──NO──→ 转接人工
│
└─ YES ─→ 高优先级客户？──YES──→ 立即转接高级顾问
           │
           └─ NO ──→ 返回 AI 答案 ──→ 客户满意？
                         ↑            │
                         └─ NO ───────┘
```

**路由逻辑**
- **售前咨询** → 销售部门
- **服务预约** → 服务部门
- **投诉问题** → 质量部门
- **融资咨询** → 融资专员
- **售后查询** → 对应销售员

#### 5. 学习与改进

**对话记录分析**
- 持续记录所有对话
- 分析 AI 失败的案例
- 汇总未处理的问题类型

**知识库自更新**
- 员工可手动补充 FAQ
- 系统建议新 FAQ 条目
- 版本控制与 A/B 测试

**性能指标**
- 首次应答率（AI 直接解答的比例）
- 用户满意度（NPS）
- 平均处理时间
- 转接率

---

### 非功能需求

| 需求 | 目标值 |
|-----|--------|
| **可用性** | 99.9%（每月不超过 43 分钟停机时间） |
| **响应时间** | < 2秒（AI 生成回复） |
| **并发能力** | 支持 50+ 同时对话 |
| **数据安全** | 加密存储客户信息，遵守 GDPR/个人信息保护法 |
| **延迟** | 消息发送→接收 < 500ms |
| **伸缩性** | 能够水平扩展（支持多个 AI 实例） |

---

## 架构设计

### 系统全景图

```
┌─────────────────────────────────────────────────────────────┐
│                    用户交互层 (Frontend)                      │
├──────────────┬──────────────┬──────────────┬────────────────┤
│  Web 聊天框   │  LINE 官方    │  Email 网关   │  内部仪表板      │
│  (Iframe)    │  (Messaging)  │  (Pop3/IMAP)  │  (Dashboard)    │
└──────────────┼──────────────┼──────────────┼────────────────┘
               │              │              │
          ┌────▼──────────────▼──────────────▼────┐
          │     消息网关层 (Message Gateway)      │
          │  - 多渠道统一接入                      │
          │  - WebSocket 推送                     │
          │  - SignalR 实时连接                   │
          └────┬───────────────────────────────┘
               │
    ┌──────────▼──────────────────────────────┐
    │     AI 编排层 (AI Orchestration)       │
    ├─────────────────────────────────────────┤
    │ 1. 意图识别 (Intent Classification)    │
    │ 2. 实体提取 (NER)                      │
    │ 3. 对话管理 (Dialog Management)        │
    │ 4. 情感分析 (Sentiment Analysis)       │
    │ 5. 上下文维护 (Context Management)     │
    │ 6. 路由决策 (Routing Decision)         │
    └──────────────────────────────────────────┘
               │
    ┌──────────┴──────────┬──────────┬──────────┐
    │                     │          │          │
┌───▼────┐   ┌────────────▼──┐  ┌────▼────┐  ┌▼──────────────┐
│ FAQ    │   │ 预约系统      │  │客户数据  │  │ 转接到人工    │
│ 知识库  │   │ (Calendar)    │  │(Customer)│  │(Queue Manager)│
│        │   │              │  │ DB       │  │               │
└───┬────┘   └────┬─────────┘  └────┬────┘  └┬──────────────┘
    │             │                 │        │
    └─────────────┴─────────────────┴────────┘
               │
    ┌──────────▼──────────────────────────────┐
    │     集成服务层 (Integration Services)   │
    ├─────────────────────────────────────────┤
    │ - DynamicEntityService（CRUD 操作）     │
    │ - AuthService（客户认证）               │
    │ - ProjectScope（多租户隔离）           │
    │ - HookExecutionService（业务逻辑）     │
    │ - DocumentPdfService（文档生成）       │
    └──────────────────────────────────────────┘
               │
    ┌──────────▼──────────────────────────────┐
    │     数据持久层 (Data Layer)             │
    ├─────────────────────────────────────────┤
    │ - 对话历史表 (ConversationHistory)      │
    │ - 对话消息表 (Messages)                 │
    │ - 客户会话表 (Sessions)                 │
    │ - 意图分类表 (Intents)                  │
    │ - 转接记录表 (Handovers)                │
    │ - 知识库条目表 (KnowledgeBase)          │
    └──────────────────────────────────────────┘
               │
    ┌──────────▼──────────────────────────────┐
    │     外部 API (External Services)        │
    ├─────────────────────────────────────────┤
    │ - LLM API (OpenAI / Azure / Qwen)       │
    │ - LINE Messaging API                    │
    │ - 短信网关 API                          │
    │ - Slack 通知 API                        │
    │ - 分析平台 API (Analytics)              │
    └──────────────────────────────────────────┘
```

### 部署拓扑

```
Internet
   │
   ▼
┌─────────────┐
│ CDN/WAF     │  (DDoS 防护、缓存)
└─────────────┘
   │
   ▼
┌─────────────────────┐
│  API Gateway        │  (路由、认证、限流)
│  (Kong / NGINX)     │
└────┬────────────────┘
     │
┌────┴─────────────────────────────────────────┐
│                                              │
▼                                              ▼
┌──────────────────────┐           ┌──────────────────────┐
│ AI 编排服务 (副本)    │           │ 人工客服仪表板       │
│ ├─ .NET 8.0          │           │ ├─ React 前端        │
│ ├─ NetYamlForge      │           │ ├─ SignalR Hub       │
│ ├─ LLM 集成          │           │ └─ 队列消费者        │
│ └─ 对话管理          │           └──────────────────────┘
└──────────────────────┘

┌─────────────────────────────────────────┐
│        消息中间件 (RabbitMQ/Redis)      │
│  ├─ 对话事件队列                        │
│  ├─ 转接队列                            │
│  └─ 通知队列                            │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│      共享数据库 (PostgreSQL)            │
│  ├─ 对话历史                            │
│  ├─ 客户会话                            │
│  ├─ 知识库                              │
│  └─ 系统日志                            │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│      文件存储 (S3-like)                  │
│  ├─ 聊天记录导出                        │
│  ├─ 媒体文件 (图片、文件)               │
│  └─ 知识库文档                          │
└─────────────────────────────────────────┘
```

---

## 技术栈选择

### 后端

| 组件 | 选择 | 理由 |
|------|------|------|
| **框架** | NetYamlForge (.NET 10) | 现有框架，支持多租户、YAML 配置、Hook 系统 |
| **实时通信** | SignalR | .NET 原生，支持多传输（WebSocket/SSE/LongPolling） |
| **消息队列** | RabbitMQ | 高可用、消息持久化、路由灵活 |
| **缓存** | Redis | 对话上下文、会话缓存、限流计数器 |
| **LLM** | Qwen（阿里云）或 Claude | 中文优化、成本低、低延迟 |
| **数据库** | PostgreSQL | 支持 JSON 字段、全文搜索、ACID 事务 |
| **日志** | Serilog + ELK | 结构化日志、集中管理、可视化 |

### 前端

| 组件 | 选择 | 理由 |
|------|------|------|
| **聊天框** | React + Tailwind | 轻量、组件丰富、易集成 |
| **仪表板** | Vue 3 | 已有技能栈，更新快 |
| **WebSocket** | SignalR JS | 自动重连、消息确认 |
| **UI 组件库** | shadcn/ui | 现代化、无依赖 |

### 外部服务

| 服务 | 选择 | 用途 |
|------|------|------|
| **LLM** | Qwen 通义千问 | 中文理解、成本低 |
| **替代方案** | Claude / GPT-4 | 更高质量，成本高 |
| **LINE 集成** | LINE Messaging API | 日本市场渗透率高 |
| **短信** | Twilio / 国内短信网关 | 消息送达 |

---

## 核心模块设计

### 1. 对话管理模块 (ConversationManager)

**职责:**
- 创建/管理对话会话
- 维护上下文状态
- 消息序列化与反序列化

**关键类:**

```csharp
namespace NetYamlForge.Services.AI.CustomerAI
{
    /// <summary>
    /// 对话上下文 - 维持一个对话会话的所有信息
    /// </summary>
    public class ConversationContext
    {
        public string ConversationId { get; set; }
        public string UserId { get; set; }                    // 客户ID或访客ID
        public string Channel { get; set; }                   // "web" / "line" / "email"
        public string ProjectId { get; set; }                 // 租户ID
        
        public Stack<Message> MessageHistory { get; set; }    // 消息历史（最近20条）
        public Dictionary<string, object> Metadata { get; set; } // {"customer_name": "...", ...}
        
        public string? CurrentIntent { get; set; }            // 当前意图
        public double IntentConfidence { get; set; }          // 意图置信度
        
        public string? RouteToAgent { get; set; }             // 如果转接，目标是谁
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivity { get; set; }
        
        /// <summary>
        /// 检查是否超时（30 分钟无活动）
        /// </summary>
        public bool IsExpired => DateTime.UtcNow - LastActivity > TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// 消息对象
    /// </summary>
    public class Message
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ConversationId { get; set; }
        public string Sender { get; set; }                    // "user" / "ai" / "agent"
        public string Content { get; set; }
        public MessageType Type { get; set; }                 // 文本/图片/快速按钮
        
        public Dictionary<string, object>? Metadata { get; set; } // 意图、实体等
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool Delivered { get; set; }
        public bool Read { get; set; }
    }

    public enum MessageType
    {
        Text,
        Image,
        File,
        QuickReply,
        RichMenu,
        Button,
        Carousel
    }

    /// <summary>
    /// 对话管理器 - 核心服务
    /// </summary>
    public interface IConversationManager
    {
        // 会话管理
        Task<ConversationContext> StartConversationAsync(
            string userId, 
            string channel, 
            string projectId,
            CancellationToken ct = default);
        
        Task<ConversationContext?> GetConversationAsync(
            string conversationId, 
            CancellationToken ct = default);
        
        Task UpdateContextAsync(
            ConversationContext context, 
            CancellationToken ct = default);
        
        // 消息管理
        Task AddMessageAsync(
            string conversationId, 
            Message message, 
            CancellationToken ct = default);
        
        Task<List<Message>> GetMessageHistoryAsync(
            string conversationId, 
            int limit = 20, 
            CancellationToken ct = default);
        
        // 清理过期对话
        Task CleanExpiredConversationsAsync(CancellationToken ct = default);
    }

    public class ConversationManager : IConversationManager
    {
        private readonly IDbConnection _connection;
        private readonly IDistributedCache _cache;
        private readonly ProjectScope _projectScope;
        private readonly ILogger<ConversationManager> _logger;

        // 实现所有接口方法...
        // - 从 PostgreSQL 存储对话
        // - Redis 缓存热数据
        // - 支持多租户查询
    }
}
```

### 2. 意图识别模块 (IntentClassifier)

**职责:**
- 使用 LLM 或规则引擎识别用户意图
- 置信度计算
- 意图实体提取

**关键类:**

```csharp
namespace NetYamlForge.Services.AI.CustomerAI.IntentEngine
{
    /// <summary>
    /// 意图分类结果
    /// </summary>
    public class IntentResult
    {
        public string Intent { get; set; }                    // 意图标识
        public double Confidence { get; set; }                // 0.0 ~ 1.0
        
        public Dictionary<string, string> Entities { get; set; } // 提取的实体
        public List<string> Suggestions { get; set; }         // 建议的回复选项
    }

    /// <summary>
    /// 支持的意图枚举
    /// </summary>
    public enum IntentType
    {
        /// 营业咨询
        BusinessHours,
        VehiclePrice,
        LoanTerms,
        ServiceInfo,
        
        /// 预约相关
        ServiceAppointment,
        InspectionBooking,
        TestDrive,
        
        /// 查询相关
        ContractQuery,
        PaymentStatus,
        ServiceHistory,
        NextServiceDue,
        InventorySearch,
        
        /// 投诉/升级
        Complaint,
        Problem,
        Suggestion,
        
        /// 其他
        Greeting,
        Farewell,
        Unclear,
        Escalate
    }

    /// <summary>
    /// 意图分类器接口
    /// </summary>
    public interface IIntentClassifier
    {
        /// <summary>
        /// 分类单条消息的意图
        /// </summary>
        Task<IntentResult> ClassifyAsync(
            string userMessage,
            ConversationContext context,
            CancellationToken ct = default);
        
        /// <summary>
        /// 批量分类（用于统计分析）
        /// </summary>
        Task<List<IntentResult>> ClassifyBatchAsync(
            List<string> messages,
            CancellationToken ct = default);
    }

    /// <summary>
    /// 基于 LLM 的意图分类实现
    /// </summary>
    public class LlmIntentClassifier : IIntentClassifier
    {
        private readonly ILlmService _llmService;
        private readonly ILogger<LlmIntentClassifier> _logger;
        
        // 实现意图分类
        // - 构建 Prompt
        // - 调用 Qwen/Claude API
        // - 解析结果
        // - 缓存热门模式
    }

    /// <summary>
    /// 混合分类器 - 规则 + LLM
    /// </summary>
    public class HybridIntentClassifier : IIntentClassifier
    {
        private readonly IIntentClassifier _ruleClassifier;
        private readonly IIntentClassifier _llmClassifier;
        
        // 策略:
        // 1. 先尝试规则匹配（快速、准确）
        // 2. 规则命中率 < 0.7 时调用 LLM
        // 3. 合并两者的结果，取置信度高的
    }
}
```

### 3. 回复生成模块 (ResponseGenerator)

**职责:**
- 根据意图和对话历史生成回复
- 支持多种回复类型（文本、按钮、轮播等）

**关键类:**

```csharp
namespace NetYamlForge.Services.AI.CustomerAI.ResponseGeneration
{
    /// <summary>
    /// AI 生成的回复
    /// </summary>
    public class AiResponse
    {
        public string ResponseId { get; set; } = Guid.NewGuid().ToString();
        public string ConversationId { get; set; }
        
        /// 回复文本
        public string TextContent { get; set; }
        
        /// 回复类型
        public ResponseType ResponseType { get; set; }
        
        /// 如果是按钮/轮播，包含选项
        public List<ActionButton>? ActionButtons { get; set; }
        
        /// 置信度（用于决定是否需要人工审查）
        public double Confidence { get; set; }
        
        /// 如果置信度低，是否推荐转接
        public bool SuggestHandover { get; set; }
        
        /// 关联的知识库条目ID（用于追踪）
        public string? KnowledgeBaseId { get; set; }
        
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public enum ResponseType
    {
        Text,              // 纯文本
        TextWithButtons,   // 带按钮的文本
        Carousel,          // 轮播（多个卡片）
        QuickReply,        // 快速回复
        FormRequest,       // 表单（预约时）
        Document,          // 文档（合同预览）
        Error              // 错误提示
    }

    public class ActionButton
    {
        public string Label { get; set; }
        public string ActionType { get; set; }              // "url" / "postback" / "phone"
        public string ActionValue { get; set; }             // URL / 后续命令 / 电话号码
    }

    /// <summary>
    /// 回复生成器接口
    /// </summary>
    public interface IResponseGenerator
    {
        /// <summary>
        /// 生成回复
        /// </summary>
        Task<AiResponse> GenerateResponseAsync(
            IntentResult intent,
            ConversationContext context,
            CancellationToken ct = default);
    }

    public class LlmResponseGenerator : IResponseGenerator
    {
        private readonly ILlmService _llmService;
        private readonly IKnowledgeBaseService _kb;
        private readonly ILogger<LlmResponseGenerator> _logger;
        
        // 实现:
        // 1. 检查知识库是否有相关条目
        // 2. 构建 Prompt（包含上下文、意图、客户信息）
        // 3. 调用 LLM 生成回复
        // 4. 质量评分（长度、相关性、礼貌性）
        // 5. 如果质量不达标，返回预定义回复 + 转接建议
    }
}
```

### 4. 客户信息集成模块 (CustomerDataService)

**职责:**
- 根据客户 ID 或联系方式查询客户信息
- 验证客户身份
- 获取合同、支付、服务历史

**关键类:**

```csharp
namespace NetYamlForge.Services.AI.CustomerAI.Integration
{
    /// <summary>
    /// 客户档案（来自主系统）
    /// </summary>
    public class CustomerProfile
    {
        public string CustomerId { get; set; }
        public string Name { get; set; }
        public string PhoneNumber { get; set; }
        public string Email { get; set; }
        
        /// 所有拥有的车辆
        public List<VehicleInfo> Vehicles { get; set; }
        
        /// 最近的合同
        public List<ContractInfo> Contracts { get; set; }
        
        /// 最近的服务记录
        public List<ServiceRecord> ServiceRecords { get; set; }
        
        /// 等级（普通/VIP/VIP+）
        public string TierLevel { get; set; }
    }

    /// <summary>
    /// 客户数据服务接口
    /// </summary>
    public interface ICustomerDataService
    {
        /// <summary>
        /// 根据客户ID查询档案
        /// </summary>
        Task<CustomerProfile?> GetCustomerProfileAsync(
            string customerId,
            string projectId,
            CancellationToken ct = default);
        
        /// <summary>
        /// 查询客户最近的服务历史（用于上下文）
        /// </summary>
        Task<List<ServiceRecord>> GetRecentServiceHistoryAsync(
            string customerId,
            int limit = 5,
            CancellationToken ct = default);
        
        /// <summary>
        /// 查询合同信息
        /// </summary>
        Task<ContractInfo?> GetContractInfoAsync(
            string customerId,
            string contractId,
            CancellationToken ct = default);
        
        /// <summary>
        /// 验证客户身份（通过电话/邮箱）
        /// </summary>
        Task<bool> VerifyCustomerAsync(
            string identifier,
            string verificationCode,
            CancellationToken ct = default);
    }

    public class CustomerDataService : ICustomerDataService
    {
        private readonly DynamicCrudRepository _repository;
        private readonly ProjectScope _projectScope;
        private readonly IDistributedCache _cache;
        
        // 实现:
        // 1. 使用 DynamicCrudRepository 查询 Customer 实体
        // 2. 关联查询 Contracts、ServiceRecords
        // 3. 缓存热数据（24小时）
        // 4. 多租户隔离（ProjectScope）
        // 5. 敏感信息脱敏（显示末尾数字）
    }
}
```

### 5. 预约管理模块 (AppointmentService)

**职责:**
- 查询可用时段
- 创建/修改预约
- 预约提醒

**关键类:**

```csharp
namespace NetYamlForge.Services.AI.CustomerAI.Booking
{
    /// <summary>
    /// 预约请求
    /// </summary>
    public class AppointmentRequest
    {
        public string CustomerId { get; set; }
        public string ServiceType { get; set; }             // "inspection" / "repair" / "test_drive"
        public DateTime PreferredDateTime { get; set; }
        
        public string? VehicleId { get; set; }              // 选项
        public Dictionary<string, string>? Details { get; set; } // 其他详情
    }

    /// <summary>
    /// 可用时段
    /// </summary>
    public class AvailableSlot
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int AvailableCapacity { get; set; }          // 还有几个空位
        public string LocationId { get; set; }              // 门店 ID
    }

    /// <summary>
    /// 预约管理器
    /// </summary>
    public interface IAppointmentService
    {
        /// <summary>
        /// 查询可用时段
        /// </summary>
        Task<List<AvailableSlot>> GetAvailableSlotsAsync(
            DateTime startDate,
            DateTime endDate,
            string serviceType,
            string projectId,
            CancellationToken ct = default);
        
        /// <summary>
        /// 创建预约
        /// </summary>
        Task<bool> CreateAppointmentAsync(
            AppointmentRequest request,
            string projectId,
            CancellationToken ct = default);
        
        /// <summary>
        /// 查询预约确认号
        /// </summary>
        Task<string?> GetAppointmentConfirmationAsync(
            string appointmentId,
            CancellationToken ct = default);
    }

    public class AppointmentService : IAppointmentService
    {
        private readonly DynamicCrudRepository _repository;
        private readonly INotificationService _notificationService;
        
        // 实现:
        // 1. 查询 ServiceReceipt 表的可用时段
        // 2. 检查员工日程
        // 3. 创建预约记录
        // 4. 发送确认 Email/SMS
        // 5. 设置提醒任务
    }
}
```

### 6. 智能转接模块 (HandoverManager)

**职责:**
- 判断是否需要转接
- 路由到适当的客服代表
- 管理队列

**关键类:**

```csharp
namespace NetYamlForge.Services.AI.CustomerAI.Handover
{
    /// <summary>
    /// 转接原因
    /// </summary>
    public enum HandoverReason
    {
        LowConfidence,         // AI 置信度低
        NegativeSentiment,     // 客户不满意
        ComplexQuery,          // 问题过于复杂
        CustomerRequest,       // 客户主动请求
        SpecialHandling,       // 需要特殊处理（投诉等）
        VipCustomer,           // VIP 客户
        HighPriority           // 高优先级
    }

    /// <summary>
    /// 转接决策
    /// </summary>
    public class HandoverDecision
    {
        public bool ShouldHandover { get; set; }
        public HandoverReason? Reason { get; set; }
        public string? TargetDepartment { get; set; }       // "sales" / "service" / "quality"
        public string? PreferredAgentId { get; set; }       // 最合适的客服代表
        public string? Message { get; set; }                // 转接消息
    }

    /// <summary>
    /// 转接管理器
    /// </summary>
    public interface IHandoverManager
    {
        /// <summary>
        /// 决定是否转接
        /// </summary>
        Task<HandoverDecision> EvaluateHandoverAsync(
            ConversationContext context,
            IntentResult? lastIntent,
            CancellationToken ct = default);
        
        /// <summary>
        /// 执行转接
        /// </summary>
        Task<bool> HandoverToAgentAsync(
            ConversationContext context,
            HandoverDecision decision,
            CancellationToken ct = default);
        
        /// <summary>
        /// 获取等待中的对话队列
        /// </summary>
        Task<List<PendingHandover>> GetPendingHandoversAsync(
            string agentId,
            CancellationToken ct = default);
    }

    public class HandoverManager : IHandoverManager
    {
        private readonly IMessageQueueService _queue;
        private readonly ICustomerDataService _customerData;
        private readonly ISentimentAnalyzer _sentiment;
        
        // 实现转接逻辑
        // 1. 分析置信度、情感
        // 2. 确定目标部门
        // 3. 查找最优客服代表（负载、专长）
        // 4. 放入消息队列
        // 5. 通知客服代表（SignalR）
    }
}
```

---

## 集成点规划

### 与现有 NetYamlForge 框架的集成

```
AI Window System
│
├─> [ProjectManager]
│   └─ 多租户隔离、项目配置加载
│
├─> [ProjectScope]
│   └─ 每个请求切换到正确的项目数据库
│
├─> [DynamicCrudRepository]
│   └─ 查询/创建预约、合同、客户信息
│
├─> [HookExecutionService]
│   └─ 创建预约时触发自定义 Hook（发送确认邮件等）
│
├─> [DynamicEntityController]
│   └─ 提供 REST API 供前端调用
│
├─> [AuthService]
│   └─ 验证客户身份、权限检查
│
└─> [DocumentPdfService]
    └─ 生成合同预览 PDF 用于聊天展示
```

### 新增的数据表

在每个项目的 `entities/` 下新增以下 YAML 定义:

```yaml
# projects/auto-dealer-demo/entities/ai-conversation.yml
apiVersion: v1
kind: Entity
metadata:
  name: AIConversation
  displayName: AI 对话历史
  description: 存储所有 AI 客服对话记录
spec:
  columns:
    - name: conversation_id
      type: string
      required: true
      primaryKey: true
      description: 对话ID
    
    - name: user_id
      type: string
      required: true
      description: 用户/客户ID（可以是访客ID）
    
    - name: channel
      type: string
      required: true
      enum: ["web", "line", "email", "sms"]
      description: 来源渠道
    
    - name: project_id
      type: string
      required: true
      description: 项目ID（多租户）
    
    - name: status
      type: string
      required: true
      enum: ["active", "resolved", "escalated", "archived"]
      default: "active"
    
    - name: messages_json
      type: text
      description: 消息历史（JSON 数组）
    
    - name: last_intent
      type: string
      description: 最后的意图分类
    
    - name: satisfaction_score
      type: integer
      description: 满意度评分（1-5）
    
    - name: created_at
      type: datetime
      required: true
      autoValue: now()
    
    - name: updated_at
      type: datetime
      autoValue: now()

  forms:
    list:
      title: 对话历史列表
      columns: [conversation_id, user_id, channel, status, satisfaction_score, updated_at]
      filters:
        - field: channel
          type: select
        - field: status
          type: select
        - field: created_at
          type: date_range
    
    detail:
      title: 对话详情
      sections:
        - name: 基本信息
          fields: [conversation_id, user_id, channel, status, created_at, updated_at]
        - name: 内容
          fields: [messages_json, last_intent]
        - name: 评价
          fields: [satisfaction_score]
```

### YAML 配置示例

```yaml
# projects/auto-dealer-demo/config/ai-window.yml
apiVersion: v1
kind: AIWindow
metadata:
  name: customer-service
  displayName: 客户 AI 窗口

spec:
  # LLM 配置
  llm:
    provider: "qwen"  # or "openai", "azure"
    model: "qwen-turbo"
    apiKey: "${AI_LLM_API_KEY}"
    endpoint: "https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation"
    
    # 系统提示词
    systemPrompt: |
      你是一个专业的自动车经销商客服。
      你的职责是：
      1. 回答关于营业时间、车型、价格的问题
      2. 帮助客户预约服务
      3. 查询客户的合同和服务历史
      4. 如果无法解答，友好地转接给人工客服
      
      重要规则：
      - 始终保持专业和礼貌
      - 隐私优先：不要公开显示客户的财务信息
      - 如果客户不满意，主动提出转接人工
  
  # 渠道配置
  channels:
    - name: web
      enabled: true
      config:
        scriptUrl: "/js/ai-chat-widget.js"
        position: "bottom-right"
        width: "380px"
        height: "600px"
    
    - name: line
      enabled: true
      config:
        channelSecret: "${LINE_CHANNEL_SECRET}"
        accessToken: "${LINE_ACCESS_TOKEN}"
        webhookUrl: "https://yoursite.com/api/line/webhook"
    
    - name: email
      enabled: false
      config:
        popServer: "pop.gmail.com"
        smtpServer: "smtp.gmail.com"
  
  # 知识库配置
  knowledgeBase:
    - title: "営業時間"
      keywords: ["営業時間", "何時まで", "オープン"]
      answer: |
        月～金: 9:00～18:00
        土: 9:00～17:00
        日祝: 休業
    
    - title: "ローン条件"
      keywords: ["ローン", "金利", "返済"]
      answer: |
        弊社でご利用いただけるローン商品：
        - 金利: 2.9% ~ 4.9%
        - 返済期間: 12～84ヶ月
        詳細はお気軽にお問い合わせください。
  
  # 转接规则
  handoverRules:
    - trigger: "confidence < 0.6"
      department: "sales"
      message: "複雑なご質問ですので、専門の担当者にお繋ぎいたします。"
    
    - trigger: "sentiment == negative"
      department: "quality"
      priority: "high"
      message: "ご不便をおかけして申し訳ございません。品質部門にお繋ぎいたします。"
    
    - trigger: "customer_tier == vip"
      department: "sales"
      preferredAgents: ["agent_001", "agent_003"]
      message: "VIP 顧客様専任の担当者にお繋ぎいたします。"
  
  # 分析配置
  analytics:
    enabled: true
    trackingEvents:
      - "conversation_started"
      - "intent_classified"
      - "response_generated"
      - "handover_executed"
      - "feedback_submitted"
```

---

## 实现路线图

### Phase 1: 基础框架（2 周）

**目标:** 建立核心对话引擎和数据模型

- [ ] **Week 1**
  - [ ] 创建 AI Window 项目结构
  - [ ] 设计数据表（ConversationHistory, Messages, Sessions）
  - [ ] 实现 ConversationManager（存储/加载对话）
  - [ ] 创建基础 API 端点

- [ ] **Week 2**
  - [ ] 实现 LLM 集成（Qwen 或 Claude）
  - [ ] 构建意图分类器（规则 + 混合）
  - [ ] 实现消息持久化
  - [ ] 单元测试（覆盖率 > 80%）

**交付物:**
- `IConversationManager` 实现
- `IIntentClassifier` 实现（规则版本）
- `POST /api/ai/message` 端点（接收用户消息）
- 数据库迁移脚本

---

### Phase 2: 业务集成（2 周）

**目标:** 与现有系统集成，实现基础查询功能

- [ ] **Week 3**
  - [ ] 实现 ICustomerDataService（查询客户、合同、服务历史）
  - [ ] 创建认证和授权逻辑
  - [ ] 实现预约查询（IAppointmentService）

- [ ] **Week 4**
  - [ ] 实现预约创建逻辑
  - [ ] 集成 Email 通知（使用现有 DocumentPdfService 生成确认）
  - [ ] 建立转接规则引擎（IHandoverManager）
  - [ ] E2E 测试（完整对话流程）

**交付物:**
- `GET /api/ai/customer/{customerId}` 端点（查询客户信息）
- `GET /api/ai/appointments/available` 端点（查询可用时段）
- `POST /api/ai/appointments` 端点（创建预约）
- `POST /api/ai/handover` 端点（转接到人工）

---

### Phase 3: 多渠道集成（2 周）

**目标:** 支持 Web、LINE、Email 等渠道

- [ ] **Week 5**
  - [ ] 创建 Web 聊天框组件（React）
  - [ ] 实现 WebSocket/SignalR 连接
  - [ ] 构建 LINE 消息网关
  - [ ] 处理消息加密/解密

- [ ] **Week 6**
  - [ ] Email 网关集成（接收邮件 → AI 分类 → 生成回复建议）
  - [ ] 内部仪表板（展示待处理对话队列）
  - [ ] 多渠道流量路由测试
  - [ ] 性能测试（50+ 并发）

**交付物:**
- `<script>` 标签可嵌入的 Web 聊天框
- LINE Webhook 处理程序
- Email 消息消费者（后台服务）
- 客服仪表板 UI

---

### Phase 4: 智能优化（2 周）

**目标:** 提升 AI 质量，增加自动应答率

- [ ] **Week 7**
  - [ ] 实现情感分析（SentimentAnalyzer）
  - [ ] 构建反馈学习系统（用户满意/不满意标记）
  - [ ] 优化 LLM Prompt（Few-shot 学习）
  - [ ] 性能优化（缓存、批处理）

- [ ] **Week 8**
  - [ ] 分析仪表板（应答率、满意度、常见问题）
  - [ ] 知识库自更新机制
  - [ ] A/B 测试框架
  - [ ] 压力测试 & 优化

**交付物:**
- `ISentimentAnalyzer` 实现
- 分析仪表板（Grafana/自建）
- 知识库管理后台
- 性能基准报告

---

### Phase 5: 生产部署（1 周）

**目标:** 上线并持续改进

- [ ] **Week 9**
  - [ ] Kubernetes 部署配置
  - [ ] 监控告警设置
  - [ ] 灾备和自动故障恢复
  - [ ] 文档编写和培训
  - [ ] Beta 版本上线（限定用户）

**交付物:**
- Docker 镜像
- K8s 部署清单
- 监控仪表板（Prometheus + Grafana）
- 运维文档 & 故障排除指南

---

## 数据模型设计

### ER 图

```
┌─────────────────────────┐
│    AIConversation       │
├─────────────────────────┤
│ PK: conversation_id     │──────┐
│    user_id              │      │
│    channel              │      │
│    project_id           │      │
│    status               │      │
│    created_at           │      │
│    updated_at           │      │
└─────────────────────────┘      │
                                 │
                                 │ 1:N
                                 │
                    ┌────────────▼──────────────────┐
                    │      AIMessage               │
                    ├──────────────────────────────┤
                    │ PK: message_id               │
                    │    conversation_id (FK)      │
                    │    sender ("user"/"ai"/...)  │
                    │    content                   │
                    │    type (text/button/...)    │
                    │    metadata_json             │
                    │    created_at                │
                    └──────────────────────────────┘

┌─────────────────────────┐
│    AISession            │
├─────────────────────────┤
│ PK: session_id          │
│    user_id              │
│    channel              │
│    project_id           │
│    context_data_json    │
│    expires_at           │
│    created_at           │
└─────────────────────────┘

┌──────────────────────────┐
│    AIIntentLog           │
├──────────────────────────┤
│ PK: intent_log_id        │
│    conversation_id (FK)  │
│    intent_type           │
│    confidence            │
│    entities_json         │
│    created_at            │
└──────────────────────────┘

┌──────────────────────────┐
│    AIHandover            │
├──────────────────────────┤
│ PK: handover_id          │
│    conversation_id (FK)  │
│    reason                │
│    target_department     │
│    assigned_agent_id     │
│    status                │
│    created_at            │
│    handled_at            │
└──────────────────────────┘

┌──────────────────────────┐
│    AIFeedback            │
├──────────────────────────┤
│ PK: feedback_id          │
│    conversation_id (FK)  │
│    satisfaction_score    │
│    comment               │
│    resolved              │
│    created_at            │
└──────────────────────────┘
```

### 数据库初始化 SQL

```sql
-- 对话历史表
CREATE TABLE ai_conversation (
    conversation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(255) NOT NULL,
    channel VARCHAR(50) NOT NULL,           -- web, line, email
    project_id VARCHAR(100) NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'active',  -- active, resolved, escalated
    last_intent VARCHAR(100),
    satisfaction_score INT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (project_id) REFERENCES projects(id),
    INDEX idx_user_channel (user_id, channel),
    INDEX idx_project_status (project_id, status),
    INDEX idx_created_at (created_at DESC)
);

-- 消息表
CREATE TABLE ai_message (
    message_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL,
    sender VARCHAR(50) NOT NULL,            -- user, ai, agent
    content TEXT NOT NULL,
    type VARCHAR(50) NOT NULL DEFAULT 'text',  -- text, image, button, carousel
    metadata_json JSONB,
    delivered BOOLEAN DEFAULT FALSE,
    read BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (conversation_id) REFERENCES ai_conversation(conversation_id) ON DELETE CASCADE,
    INDEX idx_conversation (conversation_id),
    INDEX idx_created_at (created_at DESC)
);

-- 会话表（缓存用）
CREATE TABLE ai_session (
    session_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(255) NOT NULL,
    channel VARCHAR(50) NOT NULL,
    project_id VARCHAR(100) NOT NULL,
    context_data_json JSONB,                -- 对话上下文
    expires_at TIMESTAMP NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    INDEX idx_user_expires (user_id, expires_at),
    INDEX idx_project (project_id)
);

-- 意图日志表
CREATE TABLE ai_intent_log (
    intent_log_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL,
    intent_type VARCHAR(100) NOT NULL,
    confidence DECIMAL(3,2) NOT NULL,
    entities_json JSONB,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (conversation_id) REFERENCES ai_conversation(conversation_id) ON DELETE CASCADE,
    INDEX idx_conversation_intent (conversation_id, intent_type),
    INDEX idx_confidence (confidence DESC)
);

-- 转接日志表
CREATE TABLE ai_handover (
    handover_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL,
    reason VARCHAR(100) NOT NULL,           -- low_confidence, negative_sentiment, etc.
    target_department VARCHAR(100) NOT NULL,
    assigned_agent_id VARCHAR(255),
    status VARCHAR(50) NOT NULL DEFAULT 'pending',  -- pending, accepted, completed
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    handled_at TIMESTAMP,
    
    FOREIGN KEY (conversation_id) REFERENCES ai_conversation(conversation_id) ON DELETE CASCADE,
    INDEX idx_assigned_agent (assigned_agent_id, status),
    INDEX idx_created_at (created_at DESC)
);

-- 反馈表
CREATE TABLE ai_feedback (
    feedback_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL UNIQUE,
    satisfaction_score INT NOT NULL CHECK (satisfaction_score >= 1 AND satisfaction_score <= 5),
    comment TEXT,
    resolved BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (conversation_id) REFERENCES ai_conversation(conversation_id) ON DELETE CASCADE,
    INDEX idx_score (satisfaction_score),
    INDEX idx_resolved (resolved)
);

-- 知识库表
CREATE TABLE ai_knowledge_base (
    kb_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id VARCHAR(100) NOT NULL,
    title VARCHAR(255) NOT NULL,
    content TEXT NOT NULL,
    keywords VARCHAR(500),                  -- 逗号分隔
    category VARCHAR(100),
    priority INT DEFAULT 100,
    active BOOLEAN DEFAULT TRUE,
    version INT DEFAULT 1,
    created_by VARCHAR(255),
    updated_by VARCHAR(255),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (project_id) REFERENCES projects(id),
    INDEX idx_project_active (project_id, active),
    INDEX idx_category (category),
    FULLTEXT INDEX ft_keywords (keywords),
    FULLTEXT INDEX ft_title (title)
);
```

---

## API 接口设计

### REST 端点

#### 1. 发送消息

```http
POST /api/ai/conversations/{conversationId}/messages
Content-Type: application/json

{
  "content": "我想预约下周的服务",
  "timestamp": "2026-03-27T14:50:35Z"
}

Response 200:
{
  "messageId": "msg_123",
  "conversationId": "conv_456",
  "response": {
    "textContent": "好的，我为您查询一下下周的可用时段...",
    "responseType": "TextWithButtons",
    "actionButtons": [
      {
        "label": "查看可用时段",
        "actionType": "postback",
        "actionValue": "show_slots_next_week"
      },
      {
        "label": "转接人工客服",
        "actionType": "postback",
        "actionValue": "request_handover"
      }
    ],
    "confidence": 0.92
  },
  "timestamp": "2026-03-27T14:50:36Z"
}
```

#### 2. 创建预约

```http
POST /api/ai/appointments
Content-Type: application/json

{
  "conversationId": "conv_456",
  "serviceType": "inspection",
  "preferredDateTime": "2026-04-01T10:00:00+09:00",
  "vehicleId": "vehicle_789"
}

Response 201:
{
  "appointmentId": "appt_001",
  "confirmationNumber": "SVC202604010001",
  "status": "confirmed",
  "scheduledDateTime": "2026-04-01T10:00:00+09:00",
  "message": "您的预约已确认。我们将在预约前 24 小时发送提醒。"
}
```

#### 3. 获取客户信息

```http
GET /api/ai/customers/{customerId}?verified=true
Authorization: Bearer {token}

Response 200:
{
  "customerId": "cust_001",
  "name": "山田太郎",
  "phoneNumber": "090-XXXX-5678",   // 部分脱敏
  "email": "y***@example.com",
  "tier": "VIP",
  "vehicles": [
    {
      "vehicleId": "vehicle_789",
      "make": "Toyota",
      "model": "Corolla",
      "year": 2023,
      "mileage": 15000
    }
  ],
  "recentContracts": [...],
  "recentServices": [...]
}
```

#### 4. 转接到人工客服

```http
POST /api/ai/handover
Content-Type: application/json

{
  "conversationId": "conv_456",
  "reason": "CustomerRequest",
  "message": "客户请求转接人工"
}

Response 202:
{
  "handoverId": "handover_001",
  "status": "queued",
  "position": 3,
  "estimatedWaitTime": "5 minutes",
  "message": "感谢您的等待，我们正在将您转接给专业顾问..."
}
```

#### 5. 提交反馈

```http
POST /api/ai/conversations/{conversationId}/feedback
Content-Type: application/json

{
  "satisfactionScore": 4,
  "comment": "AI 非常有帮助，但在预约时有些卡顿"
}

Response 200:
{
  "feedbackId": "fb_001",
  "message": "感谢您的反馈！我们将不断改进服务。"
}
```

### WebSocket 事件

#### 客户端监听事件

```javascript
// 连接 SignalR Hub
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/ai-chat")
  .withAutomaticReconnect()
  .build();

// 监听新消息
connection.on("MessageReceived", (message) => {
  console.log(`AI: ${message.content}`);
});

// 监听转接事件
connection.on("HandoverInitiated", (data) => {
  console.log(`正在转接给: ${data.agentName}`);
});

// 监听输入提示
connection.on("TypingIndicator", (data) => {
  console.log("客服正在输入...");
});
```

#### 服务器发送事件

```csharp
// 通过 SignalR 向客户端发送消息
await _hubContext.Clients.Group(conversationId)
  .SendAsync("MessageReceived", new {
    sender = "ai",
    content = response.TextContent,
    timestamp = DateTime.UtcNow
  });

// 通知新消息到客服仪表板
await _hubContext.Clients.Group($"agent_{agentId}")
  .SendAsync("NewHandover", new {
    conversationId = handover.ConversationId,
    customerName = customer.Name,
    priority = handover.Priority
  });
```

---

## 开发清单

### 核心服务开发

- [ ] **ConversationManager**
  - [ ] `StartConversationAsync` - 创建新对话会话
  - [ ] `AddMessageAsync` - 添加用户/AI 消息
  - [ ] `GetConversationAsync` - 检索对话上下文
  - [ ] `UpdateContextAsync` - 更新上下文信息
  - [ ] 单元测试（>90% 覆盖率）

- [ ] **LLM 集成**
  - [ ] `ILlmService` 接口定义
  - [ ] Qwen 提供商实现
  - [ ] Claude 提供商实现（备选）
  - [ ] 流式响应处理
  - [ ] 错误重试机制

- [ ] **IntentClassifier**
  - [ ] 规则引擎（正则表达式匹配）
  - [ ] LLM 分类器
  - [ ] 混合分类器（规则 + LLM）
  - [ ] 实体提取
  - [ ] 缓存热门模式

- [ ] **CustomerDataService**
  - [ ] 从 DynamicCrudRepository 查询客户
  - [ ] 关联查询合同、服务记录
  - [ ] 身份验证（OTP）
  - [ ] 隐私脱敏

- [ ] **AppointmentService**
  - [ ] 查询可用时段
  - [ ] 创建预约
  - [ ] 取消预约
  - [ ] 发送确认通知

- [ ] **HandoverManager**
  - [ ] 评估转接必要性
  - [ ] 客服代表分配
  - [ ] 消息队列操作
  - [ ] SignalR 通知

### 控制器与 API

- [ ] **AIController**
  - [ ] `POST /api/ai/messages` - 处理用户消息
  - [ ] `GET /api/ai/conversations/{id}` - 获取对话
  - [ ] `POST /api/ai/appointments` - 创建预约
  - [ ] `GET /api/ai/customers/{id}` - 查询客户
  - [ ] `POST /api/ai/handover` - 转接请求
  - [ ] `POST /api/ai/feedback` - 提交反馈

- [ ] **WebSocket Hub**
  - [ ] `AIProgressHub` - 实时消息推送
  - [ ] 连接管理（认证、组管理）
  - [ ] 消息路由（单播、广播、组播）

### 前端组件

- [ ] **Web 聊天框**
  - [ ] React 组件 `AIChatWidget.tsx`
  - [ ] 消息列表渲染
  - [ ] 输入框与按钮
  - [ ] 快速回复显示
  - [ ] 文件上传支持
  - [ ] 响应式设计

- [ ] **客服仪表板**
  - [ ] 待处理队列显示
  - [ ] 对话窗口
  - [ ] 客户信息面板
  - [ ] 快速回复模板
  - [ ] 转接历史

### 渠道集成

- [ ] **LINE 集成**
  - [ ] Webhook 处理程序
  - [ ] 消息加密/解密
  - [ ] Rich Menu 配置
  - [ ] 位置分享支持

- [ ] **Email 网关**
  - [ ] 邮件接收（POP3/IMAP）
  - [ ] 自动分类
  - [ ] AI 生成回复建议
  - [ ] SMTP 发送

### 监控与分析

- [ ] **Metrics 收集**
  - [ ] 消息处理延迟
  - [ ] LLM API 调用成功率
  - [ ] 首次应答率
  - [ ] 转接率
  - [ ] 用户满意度

- [ ] **日志**
  - [ ] 结构化日志（Serilog）
  - [ ] 中央日志收集（ELK）
  - [ ] 审计日志（谁、做了什么、何时）

- [ ] **告警规则**
  - [ ] AI 响应时间 > 5秒
  - [ ] LLM API 错误率 > 1%
  - [ ] 转接队列深度 > 10
  - [ ] 系统可用性 < 99%

### 文档

- [ ] **API 文档** (Swagger/OpenAPI)
  - [ ] 端点描述
  - [ ] 请求/响应示例
  - [ ] 错误代码
  - [ ] 认证说明

- [ ] **架构文档**
  - [ ] 系统设计
  - [ ] 数据流图
  - [ ] 部署指南

- [ ] **运维文档**
  - [ ] 故障排除
  - [ ] 扩容步骤
  - [ ] 备份恢复

---

## 部署架构

### 容器化

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src
COPY ["NetYamlForge/NetYamlForge.csproj", "NetYamlForge/"]
RUN dotnet restore "NetYamlForge/NetYamlForge.csproj"

COPY . .
RUN dotnet build "NetYamlForge/NetYamlForge.csproj" -c Release -o /app/build

FROM builder AS publish
RUN dotnet publish "NetYamlForge/NetYamlForge.csproj" -c Release -o /app/publish

FROM runtime AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "NetYamlForge.dll"]
```

### Kubernetes 部署

```yaml
# k8s/ai-window-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ai-window-service
  namespace: netyamlforge
spec:
  replicas: 3
  selector:
    matchLabels:
      app: ai-window
  template:
    metadata:
      labels:
        app: ai-window
    spec:
      containers:
      - name: ai-window
        image: registry.yourcompany.com/ai-window:latest
        imagePullPolicy: Always
        ports:
        - containerPort: 5000
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: db-credentials
              key: connection-string
        - name: AI_LLM_API_KEY
          valueFrom:
            secretKeyRef:
              name: ai-credentials
              key: llm-api-key
        resources:
          requests:
            memory: "512Mi"
            cpu: "250m"
          limits:
            memory: "1Gi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health
            port: 5000
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /ready
            port: 5000
          initialDelaySeconds: 5
          periodSeconds: 5

---
apiVersion: v1
kind: Service
metadata:
  name: ai-window-service
  namespace: netyamlforge
spec:
  selector:
    app: ai-window
  type: ClusterIP
  ports:
  - protocol: TCP
    port: 80
    targetPort: 5000

---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: ai-window-hpa
  namespace: netyamlforge
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: ai-window-service
  minReplicas: 3
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

### 监控栈

```yaml
# docker-compose.yml (用于开发/演示环境)
version: '3.8'

services:
  postgres:
    image: postgres:15
    environment:
      POSTGRES_DB: ai_window
      POSTGRES_USER: user
      POSTGRES_PASSWORD: password
    volumes:
      - postgres_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data

  rabbitmq:
    image: rabbitmq:3.12-management
    environment:
      RABBITMQ_DEFAULT_USER: user
      RABBITMQ_DEFAULT_PASS: password
    ports:
      - "5672:5672"
      - "15672:15672"
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq

  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:8.0.0
    environment:
      - discovery.type=single-node
      - xpack.security.enabled=false
    ports:
      - "9200:9200"
    volumes:
      - elasticsearch_data:/usr/share/elasticsearch/data

  kibana:
    image: docker.elastic.co/kibana/kibana:8.0.0
    ports:
      - "5601:5601"
    depends_on:
      - elasticsearch

  prometheus:
    image: prom/prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus_data:/prometheus

  grafana:
    image: grafana/grafana
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
    volumes:
      - grafana_data:/var/lib/grafana

volumes:
  postgres_data:
  redis_data:
  rabbitmq_data:
  elasticsearch_data:
  prometheus_data:
  grafana_data:
```

---

## 成功指标

| KPI | 目标 | 测量方法 |
|-----|------|--------|
| **首次应答率** | > 70% | AI 直接解答的消息占比 |
| **用户满意度 (NPS)** | > 7/10 | 每次对话后的反馈调查 |
| **平均响应时间** | < 2秒 | API 延迟监控 |
| **系统可用性** | > 99.5% | 正常运行时间监控 |
| **自动转接率** | < 20% | 转接消息占比 |
| **对话完成率** | > 85% | 非中断对话占比 |
| **客服效率提升** | > 40% | 对比自动化前后 |

---

## 总结

这份实现计划为自动车经销商 AI 窗口系统提供了完整的蓝图：

✅ **利用现有框架:** NetYamlForge 的多租户、YAML 配置、Hook 系统可直接复用
✅ **阶段性交付:** 4 周完成核心 MVP，8 周实现全功能
✅ **生产就绪:** 包括容器化、Kubernetes 部署、监控告警
✅ **可扩展设计:** 支持多渠道、多 LLM、多项目

**下一步:**
1. 建立项目仓库和开发环境
2. 组建跨职能团队（3-4 名开发人员）
3. 设置 Sprint 计划和里程碑
4. 启动 Phase 1 开发

---

**文档版本:** 1.0
**最后更新:** 2026 年 3 月 27 日
**作者:** NetYamlForge AI Team
