# 子项目 AI 统一化方案

**日期**: 2026 年 4 月 1 日  
**状态**: 计划中

---

## 📋 目标

将子项目（auto-dealer-demo）的 AI 聊天功能与全局 AI 完全统一，**除 UI 外**的其他部分保持一致。

---

## 🔍 当前架构对比

### 全局 AI（框架开发）

```
AIController.cs
    ↓
CLIServiceFactory
    ↓
BaseCLIService (QwenCodeCLIService, ClaudeCLIService, etc.)
    ↓
SkillLoader.GetSystemPrompt() → skills/_system-prompt.md
```

**特点**:
- ✅ 统一的 CLI 调用逻辑
- ✅ 统一的提示词管理
- ✅ 支持多种 AI 提供商（Qwen/Claude/Gemini/Ollama）
- ✅ 流式响应支持
- ✅ 任务队列管理

---

### 子项目 AI（auto-dealer-demo）

```
AutoDealerChatController.cs
    ↓
AutoDealerChatService.cs
    ├── 独立调用 CLI (CallCliWithQueryToolAsync)
    ├── 独立调用 Claude API (CallClaudeWithQueryToolAsync)
    └── LoadSystemPromptFromMd() → skills/auto-dealer/_system-prompt-*.md
```

**特点**:
- ❌ 独立的 CLI 调用逻辑（与全局不一致）
- ❌ 独立的提示词加载（与全局不一致）
- ❌ 仅支持 Claude + CLI（不支持其他提供商）
- ❌ 无流式响应支持
- ❌ 无任务队列管理

---

## 🎯 统一方案

### 方案 A：完全统一（推荐）

**核心思路**: 子项目 AI 复用全局 AI 的 `BaseCLIService` + `SkillLoader`

```
AutoDealerChatController.cs
    ↓
AutoDealerChatService.cs (简化)
    ├── セッション管理（DB 操作）
    ├── クエリ実行（query_data ツール）
    └── ビジネスロジック（分析レポート生成）
    ↓
CLIServiceFactory (共通)
    ↓
BaseCLIService (共通)
    ↓
SkillLoader (共通) → skills/auto-dealer/_system-prompt-*.md
```

**修改内容**:

1. **删除独立 CLI 调用逻辑**
   - `CallCliWithQueryToolAsync()` → 删除
   - `CallClaudeWithQueryToolAsync()` → 删除
   - `TryCliProvidersInOrderAsync()` → 删除

2. **复用全局 CLI 服务**
   ```csharp
   // 修改前
   var response = await CallCliWithQueryToolAsync(message, ...);
   
   // 修改后
   var cliService = _cliFactory.GetService("qwen"); // または設定から取得
   var response = await cliService.ExecuteAsync(
       message,
       systemPromptOverride: BuildSystemPrompt(isStaff)
   );
   ```

3. **统一提示词加载**
   ```csharp
   // 修改前
   private string LoadSystemPromptFromMd(bool isStaff)
   {
       // 複数のディレクトリを検索...
   }
   
   // 修改后
   private string BuildSystemPrompt(bool isStaff)
   {
       // SkillLoader を使用（グローバル AI と共通）
       var basePrompt = _skillLoader.GetSystemPrompt();
       
       // 業務固有の指示を追加
       var autoDealerPrompt = LoadAutoDealerPrompt(isStaff);
       
       return $"{basePrompt}\n\n{autoDealerPrompt}";
   }
   ```

4. **保留业务固有逻辑**
   - ✅ セッション管理（`StartSessionAsync`, `SendMessageAsync`）
   - ✅ DB クエリ実行（`ExecuteQueryDataToolAsync`）
   - ✅ 分析レポート生成（`BusinessInsightService`）
   - ✅ エスカレーション処理（`HandleEscalationAsync`）

---

### 方案 B：部分统一（简化版）

**核心思路**: 仅统一提示词系统，CLI 调用逻辑保留

```
AutoDealerChatService.cs
    ├── 提示词：使用 SkillLoader（与全局一致）
    └── CLI 调用：保留独立逻辑（业务特殊性）
```

**修改内容**:

1. **统一提示词加载**
   ```csharp
   private string BuildSystemPrompt(bool isStaff)
   {
       // 使用 SkillLoader 获取全局提示词
       var frameworkPrompt = _skillLoader.GetSystemPrompt();
       
       // 读取业务固有提示词
       var autoDealerPrompt = LoadAutoDealerPrompt(isStaff);
       
       // 合并
       return $"{frameworkPrompt}\n\n{autoDealerPrompt}";
   }
   ```

2. **保留 CLI 调用逻辑**
   - 业务需要特殊的 `query_data` 工具调用
   - 需要 DB 结果后处理（分析レポート生成）
   - 暂时保留独立逻辑

---

## 📝 推荐方案

**采用方案 A（完全统一）**，理由：

1. ✅ 代码复用率高，维护成本低
2. ✅ 支持多种 AI 提供商（Qwen/Claude/Gemini/Ollama）
3. ✅ 流式响应支持（用户体验更好）
4. ✅ 任务队列管理（高并发场景更稳定）
5. ✅ 与全局 AI 一致，开发者学习成本低

**例外情况**:
- 业务特殊的 `query_data` 工具调用逻辑保留
- 业务固有的提示词文件保留（`skills/auto-dealer/`）

---

## 🔧 修改步骤

### 步骤 1: 修改 AutoDealerChatService 构造函数

```csharp
// 删除不需要的依赖
- private readonly IHttpClientFactory _httpClientFactory;
- private readonly CliConfig _cliConfig;
- private string ClaudeApiKey;
- private string ClaudeModel;
- private int ClaudeMaxTokens;

// 添加 SkillLoader
+ private readonly SkillLoader _skillLoader;

public AutoDealerChatService(
    IDbConnection db,
    CLIServiceFactory cliFactory,
    ProjectScope projectScope,
    SkillLoader skillLoader,  // 新增
    IConfiguration config,
    ILogger<AutoDealerChatService> logger,
    QueryParserService queryParser,
    QueryExecutionService queryExecutor,
    QueryResultFormatter queryFormatter,
    BusinessInsightService businessInsight)
{
    _db = db;
    _cliFactory = cliFactory;
    _projectScope = projectScope;
    _skillLoader = skillLoader;  // 新增
    _config = config;
    _logger = logger;
    _queryParser = queryParser;
    _queryExecutor = queryExecutor;
    _queryFormatter = queryFormatter;
    _businessInsight = businessInsight;
}
```

### 步骤 2: 统一提示词加载

```csharp
private string BuildSystemPrompt(bool isStaff, string? dbContextMarkdown = null)
{
    // 使用 SkillLoader 获取全局提示词（与全局 AI 一致）
    var frameworkPrompt = _skillLoader.GetSystemPrompt();
    
    // 读取业务固有提示词
    var autoDealerPrompt = LoadAutoDealerPromptFromMd(isStaff);
    
    // 合并提示词
    var systemPrompt = $"{frameworkPrompt}\n\n{autoDealerPrompt}";
    
    // プレースホルダーを置換
    systemPrompt = systemPrompt
        .Replace("{current_datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
        .Replace("{business_hours}", BusinessHours)
        .Replace("{dealer_name}", DealerName);
    
    // DB 検索結果がある場合は追加
    if (!string.IsNullOrWhiteSpace(dbContextMarkdown))
    {
        systemPrompt += Environment.NewLine + Environment.NewLine;
        systemPrompt += "## DB 検索結果（参考）" + Environment.NewLine;
        systemPrompt += dbContextMarkdown;
    }
    
    return systemPrompt;
}
```

### 步骤 3: 复用全局 CLI 服务

```csharp
private async Task<(string ResponseText, string Intent, ...)> GenerateAiResponseAsync(...)
{
    // 构建系统提示词
    var systemPrompt = BuildSystemPrompt(isStaff);
    
    // 获取 CLI 服务（与全局 AI 一致）
    var provider = GetConfiguredProvider(); // "qwen", "claude", etc.
    var cliService = _cliFactory.GetService(provider);
    
    // 执行 CLI（支持流式）
    var response = await cliService.ExecuteAsync(
        message,
        systemPromptOverride: systemPrompt,
        ct: CancellationToken.None
    );
    
    // 处理响应（业务逻辑：DB クエリ実行、分析レポート生成）
    return await ProcessResponseAsync(response, message, isStaff);
}
```

### 步骤 4: 删除冗余代码

删除以下独立实现：
- `CallCliWithQueryToolAsync()` → 复用 `BaseCLIService.ExecuteAsync()`
- `CallClaudeWithQueryToolAsync()` → 复用 `BaseCLIService.ExecuteAsync()`
- `TryCliProvidersInOrderAsync()` → 复用 `CLIServiceFactory`
- `AppendCliToolCallInstructions()` → 不需要（CLI 侧处理）
- `BuildFinalResponsePrompt()` → 不需要（CLI 侧处理）

保留以下业务逻辑：
- `ExecuteQueryDataToolAsync()` → 业务核心
- `GenerateInsightAsync()` → 业务核心
- `HandleEscalationAsync()` → 业务核心
- `StartSessionAsync()`, `SendMessageAsync()` → 会话管理

---

## 📁 修改文件清单

| 文件路径 | 操作 | 说明 |
|---------|------|------|
| `Services/AI/AutoDealerChatService.cs` | ✏️ 重构 | 删除独立 CLI 调用逻辑，复用全局服务 |
| `Services/AI/AutoDealerChatService.cs` | ✏️ 修改 | 统一提示词加载（使用 SkillLoader） |
| `Controllers/Api/AutoDealerChatController.cs` | ✅ 不变 | 仅 API 端点，无需修改 |

---

## 🧪 测试计划

### 功能测试

| 测试场景 | 预期结果 |
|---------|---------|
| 顾客查询「今日連絡すべき顧客は？」 | 分析レポート形式で返答 |
| 社員查询「VIP 顧客の数」 | 件数＋リスト形式で返答 |
| 顾客预约「試乗を予約したい」 | 予約作成ツールが実行される |
| 大数据量查询 | 流式响应正常显示 |

### 性能测试

| 指标 | 目标值 |
|------|--------|
| 平均响应时间 | <3 秒 |
| 流式响应首字时间 | <1 秒 |
| 并发处理数 | >10 会话 |

---

## ⚠️ 注意事项

### 提示词系统

- **全局 AI**: `skills/_system-prompt.md`（框架开发）
- **子项目 AI**: `skills/auto-dealer/_system-prompt-*.md`（业务查询）

两者**完全独立**，但加载逻辑统一使用 `SkillLoader`。

### CLI 工具

- **全局 AI**: 支持 `qwen`, `claude`, `gemini`, `ollama` 等
- **子项目 AI**: 同样支持（通过配置切换）

配置示例（`appsettings.json`）:
```json
{
  "AiWindow": {
    "ProviderPriority": ["qwen", "claude", "gemini"],
    "CliFirst": true,
    "CliTimeoutSeconds": 60
  }
}
```

---

## 📊 效果评估

| 指标 | 修改前 | 修改后 |
|------|--------|--------|
| 代码行数（AutoDealerChatService） | 1,467 行 | ~800 行 (-45%) |
| CLI 调用逻辑重复 | 有 | 无 |
| 提示词加载逻辑重复 | 有 | 无 |
| 支持 AI 提供商数 | 2 种 | 4 种 + |
| 流式响应支持 | 无 | 有 |
| 任务队列管理 | 无 | 有 |

---

*方案制定时间：2026 年 4 月 1 日*
