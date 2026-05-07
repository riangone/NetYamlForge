// ファイル概要：jpiere-cs 専用 AI チャットサービス（BaseChatService 統合版）
// 共通ロジックは BaseChatService に集約。このクラスは差分のみを実装します。

using System.Data;
using System.Text;
using Dapper;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI.Providers;

namespace NetYamlForge.Services.AI;

/// <summary>
/// jpiere-cs AI チャットサービス。
/// 役割ベースのプロンプト制御・エンティティアクセス制御を担当します。
/// </summary>
public class JpiereChatService : BaseChatService
{
    public JpiereChatService(
        IDbConnection db,
        CLIServiceFactory cliFactory,
        ILlmProvider llmProvider,
        SkillLoader skillLoader,
        ProjectScope projectScope,
        ILogger<JpiereChatService> logger,
        QueryParserService queryParser,
        QueryExecutionService queryExecutor,
        QueryResultFormatter queryFormatter,
        TaskQueueService taskQueue,
        ProgressTracker tracker,
        ChatHistoryService chatHistory,
        IConfiguration config)
        : base(db, cliFactory, llmProvider, skillLoader, projectScope, logger, queryParser, queryExecutor, queryFormatter,
               taskQueue, tracker, chatHistory, config, "jpiere-cs")
    {
    }

    // ─────────────────────────────────────────────────────────
    // BaseChatService abstract 実装
    // ─────────────────────────────────────────────────────────

    protected override string BuildSystemPrompt(string context, string? dbContextMarkdown = null)
    {
        _logger.LogInformation("[BuildSystemPrompt] 役割 {Role} のプロンプト構築", context);

        var frameworkPrompt = _skillLoader.GetSystemPrompt();
        var rolePrompt = LoadRolePromptFromMd(context);

        var systemPrompt = frameworkPrompt + Environment.NewLine + Environment.NewLine + rolePrompt;

        systemPrompt = systemPrompt
            .Replace("{current_datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{business_hours}", _businessHours)
            .Replace("{project_name}", _projectName)
            .Replace("{user_role}", context);

        if (!string.IsNullOrWhiteSpace(dbContextMarkdown))
        {
            systemPrompt += Environment.NewLine + "## DB 検索結果（参考）" + Environment.NewLine + dbContextMarkdown;
        }

        _logger.LogInformation("[BuildSystemPrompt] 役割 {Role} プロンプト長: {Length} 文字", context, systemPrompt.Length);
        return systemPrompt;
    }

    protected override string GetWelcomeMessage(string? context) => context switch
    {
        "employee" => "こんにちは！JPiere の AI 業務アシスタントです。📋\n契約・見積・TODO の照会など、業務全般を支援します！",
        "contract_manager" => "こんにちは！JPiere 契約担当 AI アシスタントです。💼\n契約・見積・請求の作成・分析をお手伝いします！",
        "accountant" => "こんにちは！JPiere 会計担当 AI アシスタントです。💰\n仕訳・会計・入金・支払の管理を支援します！",
        "purchaser" => "こんにちは！JPiere 購買担当 AI アシスタントです。📦\n発注・受入・AP請求・支払のフローを支援します！",
        "approver" => "こんにちは！JPiere 承認 AI アシスタントです。✅\n承認ワークフローの確認・処理を支援します！",
        "admin" => "こんにちは！JPiere 管理者 AI アシスタントです。⚙️\nシステム全体的管理・設定変更を支援します！",
        _ => "こんにちは！JPiere AI アシスタントです。🤖\n業務のご相談は何でもどうぞ！"
    };

    protected override List<string> GetQuickReplies(string context, string intent) => context switch
    {
        "employee" => new List<string> { "契約を確認", "TODOを確認", "見積を確認" },
        "contract_manager" => new List<string> { "今月の契約", "未請求一覧", "期限切れ契約" },
        "accountant" => new List<string> { "仕訳を確認", "未収一覧", "月次損益" },
        "purchaser" => new List<string> { "発注状況", "未受入一覧", "仕入先別購買" },
        "approver" => new List<string> { "承認待ち", "承認統計", "却下案件" },
        "admin" => new List<string> { "システム状況", "ユーザー統計", "エラーログ" },
        _ => new List<string> { "契約を検索", "TODOを確認", "お問い合わせ" }
    };

    // ─────────────────────────────────────────────────────────
    // DB 操作 override（jpiere スキーマ: sent_at カラム）
    // ─────────────────────────────────────────────────────────

    protected override async Task SaveMessageAsync(
        string messageId, string conversationId, string sender, string content,
        string timestamp, string? intent = null, double confidence = 0.9, double sentiment = 0,
        List<NetYamlForge.Models.AI.UiComponent>? components = null)
    {
        // jpiere-cs スキーマでは components_json をサポート
        string? componentsJson = null;
        if (components?.Count > 0)
        {
            try
            {
                componentsJson = System.Text.Json.JsonSerializer.Serialize(components, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
            }
            catch (Exception)
            {
                // ログ警告は省略
            }
        }

        await _db.ExecuteAsync(@"
INSERT INTO ai_messages
  (message_id, conversation_id, sender, content, intent, confidence_score, sentiment_score, components_json, sent_at, created_at)
VALUES
  (@MessageId, @ConversationId, @Sender, @Content, @Intent, @Confidence, @Sentiment, @ComponentsJson, @Timestamp, @Timestamp)",
            new
            {
                MessageId = messageId, ConversationId = conversationId, Sender = sender,
                Content = content, Intent = intent ?? "general",
                Confidence = confidence, Sentiment = sentiment,
                ComponentsJson = (object?)componentsJson ?? DBNull.Value,
                Timestamp = timestamp
            });
    }

    protected override async Task<IEnumerable<(string Role, string Content)>> GetRecentMessagesAsync(
        string conversationId, int count)
    {
        return await _db.QueryAsync<(string, string)>(@"
SELECT sender, content FROM ai_messages
WHERE conversation_id = @Id
ORDER BY sent_at DESC
LIMIT @Count",
            new { Id = conversationId, Count = count });
    }

    // ─────────────────────────────────────────────────────────
    // ロールベースアクセス制御 override
    // ─────────────────────────────────────────────────────────

    protected override async Task<(string ResultText, List<Dictionary<string, string>>? DataRows, string Intent, string? NavUrl, string? NavLabel)>
        ExecuteQueryDataToolAsync(ParsedQueryParams queryParams, string? userMessage = null, string? context = null)
    {
        if (!IsEntityAccessible(queryParams.Entity, context))
        {
            return ($"エンティティ '{queryParams.Entity}' へのアクセス権限がありません。", null, "access_denied", null, null);
        }
        return await base.ExecuteQueryDataToolAsync(queryParams, userMessage, context);
    }

    private static bool IsEntityAccessible(string entity, string? userRole)
    {
        if (string.IsNullOrEmpty(userRole)) return false;

        var accessMatrix = new Dictionary<string, HashSet<string>>
        {
            ["employee"] = new HashSet<string> { "contracts", "estimations", "bills", "todos", "business_partners", "products" },
            ["contract_manager"] = new HashSet<string> { "contracts", "contract_lines", "estimations", "estimation_lines", "bills", "bill_lines", "recognitions", "business_partners", "products", "todos" },
            ["accountant"] = new HashSet<string> { "journals", "journal_lines", "accounts", "bills", "payments", "recognitions", "business_partners", "contracts" },
            ["purchaser"] = new HashSet<string> { "purchase_orders", "purchase_order_lines", "purchase_receipts", "ap_invoices", "payments", "business_partners", "products", "stock_moves" },
            ["approver"] = new HashSet<string> { "approval_requests", "approval_steps", "purchase_orders", "contracts", "todos" },
            ["admin"] = new HashSet<string> { "contracts", "estimations", "bills", "journals", "accounts", "purchase_orders", "approval_requests", "todos", "business_partners", "products", "users", "roles" }
        };

        return accessMatrix.TryGetValue(userRole, out var allowedEntities) && allowedEntities.Contains(entity);
    }

    // ─────────────────────────────────────────────────────────
    // セッション管理
    // ─────────────────────────────────────────────────────────

    public async Task<ChatSessionResult> StartSessionAsync(
        string channel = "web", string? guestSessionId = null, string? userId = null, string? userRole = null)
    {
        var conversationId = $"CONV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await _db.ExecuteAsync(@"
INSERT INTO ai_conversations
  (conversation_id, channel, status, started_at, created_at, updated_at, guest_session_id, user_id, user_role)
VALUES
  (@ConversationId, @Channel, 'active', @Now, @Now, @Now, @GuestSessionId, @UserId, @UserRole)",
            new
            {
                ConversationId = conversationId, Channel = channel, Now = now,
                GuestSessionId = (object?)guestSessionId ?? DBNull.Value,
                UserId = (object?)userId ?? DBNull.Value,
                UserRole = (object?)userRole ?? "employee"
            });

        return new ChatSessionResult
        {
            ConversationId = conversationId,
            WelcomeMessage = GetWelcomeMessage(userRole)
        };
    }

    // ─────────────────────────────────────────────────────────
    // メッセージ処理
    // ─────────────────────────────────────────────────────────

    public async Task<ChatMessageResult> SendMessageAsync(
        string conversationId, string userMessage, string userRole = "employee")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var (escalationIntent, needsHandover, priority) = DetectEscalation(userMessage);
        var sentimentScore = EstimateSentiment(userMessage);

        var history = await GetRecentMessagesAsync(conversationId, 10);
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "user", userMessage, now);

        if (needsHandover || sentimentScore < _escalationSentimentThreshold)
            return await HandleEscalationAsync(conversationId, userMessage, escalationIntent, priority, sentimentScore, now, userRole, sw);

        var (responseText, resolvedIntent, dataRows, navUrl, navLabel) =
            await GenerateAiResponseAsync(userMessage, userRole, history);

        // ✅ AI 回复的消息时间戳
        var aiResponseTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, aiResponseTime, resolvedIntent, 0.9, sentimentScore);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET last_intent = @Intent, last_confidence = 0.9, sentiment_score = @Sentiment, updated_at = @Now
WHERE conversation_id = @Id",
            new { Intent = resolvedIntent, Sentiment = sentimentScore, Now = now, Id = conversationId });

        await _chatHistory.SaveMessageAsync(_projectName, userMessage, "user",
            provider: _defaultProvider, chatContext: $"jpiere-{userRole}", projectName: _projectName);
        await _chatHistory.SaveMessageAsync(_projectName, responseText, "assistant",
            provider: _defaultProvider, chatContext: $"jpiere-{userRole}", projectName: _projectName);

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = responseText,
            Intent = resolvedIntent,
            SuggestHandover = false,
            QuickReplies = GetQuickReplies(userRole, resolvedIntent),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
            DataRows = dataRows,
            NavigationUrl = navUrl,
            NavigationLabel = navLabel,
            AiProvider = _defaultProvider,  // ✅ AI 提供商标识
            MessageTimestamp = aiResponseTime  // ✅ 详细时间戳
        };
    }

    // ─────────────────────────────────────────────────────────
    // エスカレーション処理（jpiere 固有）
    // ─────────────────────────────────────────────────────────

    private async Task<ChatMessageResult> HandleEscalationAsync(
        string conversationId, string userMessage,
        string intent, string priority, double sentimentScore,
        string now, string userRole, System.Diagnostics.Stopwatch sw)
    {
        var handoverId = $"HO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..28];
        var reason = intent == "complaint" ? "complaint"
            : (sentimentScore < _escalationSentimentThreshold ? "negative_sentiment" : "user_request");

        var dept = userRole switch
        {
            "employee" => "contract",
            "contract_manager" => "management",
            "accountant" => "accounting",
            "purchaser" => "purchasing",
            "approver" => "management",
            _ => "support"
        };

        await _db.ExecuteAsync(@"
INSERT INTO ai_handovers
  (handover_id, conversation_id, reason, priority, target_department, status, handover_notes, escalated_at, user_role)
VALUES
  (@HId, @CId, @Reason, @Priority, @Dept, 'pending', @Notes, @Now, @UserRole)",
            new
            {
                HId = handoverId, CId = conversationId, Reason = reason,
                Priority = priority, Dept = dept,
                Notes = $"ユーザーメッセージ：{userMessage[..Math.Min(200, userMessage.Length)]}",
                Now = now, UserRole = userRole
            });

        await _db.ExecuteAsync(@"
UPDATE ai_conversations SET status = 'escalated', updated_at = @Now WHERE conversation_id = @Id",
            new { Now = now, Id = conversationId });

        var escalationMsg = reason == "complaint"
            ? "ご不便をおかけして大変申し訳ございません。ただいま担当者にお繋ぎします。少々お待ちください。🙇"
            : "担当者にお繋ぎします。少々お待ちください。通常 5〜15 分以内に対応いたします。";

        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", escalationMsg, now, reason, 0.9, sentimentScore);

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = escalationMsg,
            Intent = reason,
            SuggestHandover = true,
            HandoverId = handoverId,
            QuickReplies = new List<string>(),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
            AiProvider = _defaultProvider,  // ✅ AI 提供商标识
            MessageTimestamp = now  // ✅ 详细时间戳
        };
    }

    // ─────────────────────────────────────────────────────────
    // メッセージ取得（Controller 用）
    // ─────────────────────────────────────────────────────────

    public async Task<IEnumerable<ConversationSummary>> GetUserRecentConversationsAsync(string userId, int limit = 10)
    {
        return await _db.QueryAsync<ConversationSummary>(@"
SELECT conversation_id AS ConversationId, channel AS Channel, status AS Status,
       started_at AS StartedAt, updated_at AS UpdatedAt
FROM ai_conversations
WHERE user_id = @UserId
ORDER BY updated_at DESC
LIMIT @Limit",
            new { UserId = userId, Limit = limit });
    }

    public async Task<List<Dictionary<string, object?>>> GetMessagesAsync(string conversationId)
    {
        var messages = await _db.QueryAsync(@"
SELECT message_id, sender, content, intent, confidence_score, sentiment_score, sent_at
FROM ai_messages
WHERE conversation_id = @Id
ORDER BY sent_at ASC",
            new { Id = conversationId });

        var result = new List<Dictionary<string, object?>>();
        foreach (var msg in messages)
        {
            result.Add(new Dictionary<string, object?>
            {
                ["messageId"] = msg.message_id,
                ["sender"] = msg.sender,
                ["content"] = msg.content,
                ["intent"] = msg.intent,
                ["confidenceScore"] = msg.confidence_score,
                ["sentimentScore"] = msg.sentiment_score,
                ["sentAt"] = msg.sent_at
            });
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────
    // ロールプロンプト読み込み
    // ─────────────────────────────────────────────────────────

    private string LoadRolePromptFromMd(string userRole)
    {
        var promptFile = userRole switch
        {
            "employee" => "_system-prompt-employee.md",
            "contract_manager" => "_system-prompt-contract-manager.md",
            "accountant" => "_system-prompt-accountant.md",
            "purchaser" => "_system-prompt-purchaser.md",
            "approver" => "_system-prompt-approver.md",
            "admin" => "_system-prompt-admin.md",
            _ => "_system-prompt-employee.md"
        };

        var filePath = Path.Combine(AppContext.BaseDirectory, "skills", "jpiere", promptFile);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("役割プロンプト {File} が見つかりません", filePath);
            return BuildFallbackRolePrompt(userRole);
        }

        var content = File.ReadAllText(filePath).Trim();
        if (content.StartsWith("---"))
        {
            var end = content.IndexOf("---", 3);
            if (end >= 0) content = content[(end + 3)..].Trim();
        }
        return content;
    }

    private string BuildFallbackRolePrompt(string userRole)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"あなたはJPiere契約サービスの{userRole} AI アシスタントです。");
        sb.AppendLine("業務を支援し、データ照会・分析・推奨アクションの提供を行います。");
        sb.AppendLine("権限外の操作には応じず、適切な担当者に引継ぎを提案してください。");
        sb.AppendLine();
        sb.AppendLine($"現在の日時：{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"営業時間：{_businessHours}");
        return sb.ToString();
    }
}
