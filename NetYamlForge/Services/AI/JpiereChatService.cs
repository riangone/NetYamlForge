// ファイル概要：jpiere-cs 専用 AI チャットサービス（グローバル AI 統一版）
// グローバル AI と同じ BaseCLIService + SkillLoader を使用
// ビジネスロジック（セッション管理、DB クエリ実行、分析レポート生成）のみを実装

using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// jpiere-cs AI チャットサービス（グローバル AI 統一版）
/// セッション管理、DB クエリ実行、分析レポート生成を担当
/// </summary>
public class JpiereChatService
{
    private readonly IDbConnection _db;
    private readonly CLIServiceFactory _cliFactory;
    private readonly SkillLoader _skillLoader;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<JpiereChatService> _logger;
    private readonly QueryParserService _queryParser;
    private readonly QueryExecutionService _queryExecutor;
    private readonly QueryResultFormatter _queryFormatter;
    private readonly TaskQueueService _taskQueue;
    private readonly ProgressTracker _tracker;
    private readonly ChatHistoryService _chatHistory;

    // 設定値
    private readonly string _projectName;
    private readonly string _defaultProvider;
    private readonly string _businessHours;
    private readonly double _escalationSentimentThreshold;

    public JpiereChatService(
        IDbConnection db,
        CLIServiceFactory cliFactory,
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
    {
        _db = db;
        _cliFactory = cliFactory;
        _skillLoader = skillLoader;
        _projectScope = projectScope;
        _logger = logger;
        _queryParser = queryParser;
        _queryExecutor = queryExecutor;
        _queryFormatter = queryFormatter;
        _taskQueue = taskQueue;
        _tracker = tracker;
        _chatHistory = chatHistory;

        _projectName = _projectScope.IsSet ? _projectScope.Current.Name : "jpiere-cs";
        _defaultProvider = config["AiWindow:DefaultProvider"] ?? config["AICli:DefaultTool"] ?? "qwen";
        _businessHours = config["AiWindow:BusinessHours"] ?? "月〜金 9:00〜18:00";
        _escalationSentimentThreshold = double.Parse(config["AiWindow:EscalationSentimentThreshold"] ?? "-0.5");
    }

    // ─────────────────────────────────────────────────────────
    // セッション管理
    // ─────────────────────────────────────────────────────────

    public async Task<ChatSessionResult> StartSessionAsync(string channel = "web", string? guestSessionId = null, string? userId = null, string? userRole = null)
    {
        var conversationId = $"CONV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await _db.ExecuteAsync(@"
INSERT INTO ai_conversations
  (conversation_id, channel, status, started_at, created_at, updated_at, guest_session_id, user_id, user_role)
VALUES
  (@ConversationId, @Channel, 'active', @Now, @Now, @Now, @GuestSessionId, @UserId, @UserRole)",
            new {
                ConversationId = conversationId,
                Channel = channel,
                Now = now,
                GuestSessionId = (object?)guestSessionId ?? DBNull.Value,
                UserId = (object?)userId ?? DBNull.Value,
                UserRole = (object?)userRole ?? "employee"
            });

        var welcome = GetWelcomeMessage(userRole);

        return new ChatSessionResult { ConversationId = conversationId, WelcomeMessage = welcome };
    }

    // ─────────────────────────────────────────────────────────
    // メッセージ処理
    // ─────────────────────────────────────────────────────────

    public async Task<ChatMessageResult> SendMessageAsync(string conversationId, string userMessage, string userRole = "employee")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // 1. エスカレーション判定
        var (escalationIntent, needsHandover, priority) = DetectEscalation(userMessage);
        var sentimentScore = EstimateSentiment(userMessage);

        // 2. 履歴取得 + メッセージ保存
        var history = await GetRecentMessagesAsync(conversationId, 10);
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "user", userMessage, now);

        // 3. エスカレーション処理
        if (needsHandover || sentimentScore < _escalationSentimentThreshold)
            return await HandleEscalationAsync(conversationId, userMessage, escalationIntent, priority, sentimentScore, now, userRole, sw);

        // 4. AI 応答生成（グローバル AI と共通ロジック）
        var (responseText, resolvedIntent, dataRows, navUrl, navLabel) =
            await GenerateAiResponseAsync(userMessage, userRole, history);

        // 5. AI 応答を保存・会話更新
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, now, resolvedIntent, 0.9, sentimentScore);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET last_intent = @Intent, last_confidence = 0.9, sentiment_score = @Sentiment, updated_at = @Now
WHERE conversation_id = @Id",
            new { Intent = resolvedIntent, Sentiment = sentimentScore, Now = now, Id = conversationId });

        // 6. グローバル AI 履歴にも保存（別タブ・ブラウザ再起動対応）
        await _chatHistory.SaveMessageAsync(
            conversationId,
            userMessage,
            "user",
            provider: _defaultProvider,
            chatContext: $"jpiere-{userRole}",
            projectName: _projectName);
        await _chatHistory.SaveMessageAsync(
            conversationId,
            responseText,
            "assistant",
            provider: _defaultProvider,
            chatContext: $"jpiere-{userRole}",
            projectName: _projectName);

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = responseText,
            Intent = resolvedIntent,
            SuggestHandover = false,
            QuickReplies = GetRoleQuickReplies(userRole, resolvedIntent),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
            DataRows = dataRows,
            NavigationUrl = navUrl,
            NavigationLabel = navLabel
        };
    }

    // ─────────────────────────────────────────────────────────
    // AI 応答生成（グローバル AI と共通）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// グローバル AI と同じ CLI サービスを使用して応答を生成
    /// </summary>
    private async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        GenerateAiResponseAsync(string message, string userRole, IEnumerable<(string Role, string Content)> history)
    {
        // システムプロンプトを構築（役割別）
        var systemPrompt = BuildSystemPrompt(userRole);

        // CLI サービスを取得（グローバル AI と共通）
        ICLIService? cliService = null;
        try
        {
            cliService = _cliFactory.GetService(_defaultProvider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CLI サービス {Provider} の取得に失敗しました", _defaultProvider);
            return (GetTemplateResponse("error"), "error", null, null, null);
        }

        if (cliService == null)
        {
            _logger.LogWarning("CLI サービス {Provider} が見つかりません", _defaultProvider);
            return (GetTemplateResponse("general"), "general", null, null, null);
        }

        try
        {
            // プロンプトを構築（履歴 + 現在のメッセージ）
            var prompt = BuildPromptWithHistory(message, history, systemPrompt);

            _logger.LogInformation("AI 応答生成開始：provider={Provider}, role={Role}, messageLength={Length}",
                _defaultProvider, userRole, message?.Length ?? 0);

            // CLI を実行（非流式処理）
            var response = await ExecuteCliAsync(
                cliService,
                prompt,
                systemPrompt,
                _defaultProvider);

            _logger.LogInformation("AI 応答取得完了：responseLength={Length}", response?.Length ?? 0);

            // 応答を処理（業務ロジック：DB クエリ実行、分析レポート生成）
            return await ProcessAiResponseAsync(response, message, userRole);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 応答生成エラー：provider={Provider}, role={Role}, message={Message}",
                _defaultProvider, userRole, message);
            return (GetTemplateResponse("error"), "error", null, null, null);
        }
    }

    /// <summary>
    /// CLI を実行して AI 応答を取得
    /// </summary>
    private async Task<string> ExecuteCliAsync(
        ICLIService cliService,
        string prompt,
        string systemPrompt,
        string provider)
    {
        var workingDir = GetWorkingDirectory(_projectName);

        _logger.LogInformation("[JpiereChat] AI 実行開始：provider={Provider}, role={Role}", provider, "user");

        var response = await cliService.ExecuteAsync(
            prompt,
            workingDir,
            sessionId: null,
            allowedTools: null,
            systemPromptOverride: systemPrompt,
            CancellationToken.None);

        _logger.LogInformation("[JpiereChat] AI 応答取得完了：responseLength={Length}",
            response?.Length ?? 0);

        return response ?? string.Empty;
    }

    /// <summary>
    /// プロジェクトの作業ディレクトリを取得
    /// </summary>
    private string? GetWorkingDirectory(string? project)
    {
        if (string.IsNullOrEmpty(project))
        {
            return Directory.GetCurrentDirectory();
        }

        var projectPath = $"/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/{project}";
        return Directory.Exists(projectPath) ? projectPath : Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// 履歴付きプロンプトを構築
    /// </summary>
    private static string BuildPromptWithHistory(string message, IEnumerable<(string Role, string Content)> history, string systemPrompt)
    {
        var sb = new StringBuilder();

        sb.AppendLine(systemPrompt);
        sb.AppendLine();

        sb.AppendLine("【会話履歴】");
        foreach (var (role, content) in history.Reverse().Take(10))
        {
            sb.AppendLine($"{(role == "ai" ? "AI" : "ユーザー")}: {content}");
        }
        sb.AppendLine();

        sb.AppendLine("【現在のメッセージ】");
        sb.AppendLine(message);

        return sb.ToString();
    }

    /// <summary>
    /// AI 応答を処理（DB クエリ実行、分析レポート生成）
    /// </summary>
    private async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        ProcessAiResponseAsync(string response, string userMessage, string userRole)
    {
        var queryData = TryParseQueryDataToolCall(response);

        if (queryData != null)
        {
            var (resultText, dataRows, intent, navUrl, navLabel) =
                await ExecuteQueryDataToolAsync(queryData, userMessage, userRole);

            return (resultText, intent, dataRows, navUrl, navLabel);
        }

        return (response, "general", null, null, null);
    }

    /// <summary>
    /// AI 応答から query_data ツール呼び出しを解析
    /// </summary>
    private static ParsedQueryParams? TryParseQueryDataToolCall(string response)
    {
        var trimmed = response.Trim();

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (root.TryGetProperty("tool_call", out var tc) && tc.GetString() == "query_data")
                {
                    return new ParsedQueryParams
                    {
                        Entity = root.TryGetProperty("entity", out var e) ? e.GetString() ?? "" : "",
                        Action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "list" : "list",
                        Filters = root.TryGetProperty("filters", out var f)
                            ? JsonSerializer.Deserialize<List<FilterClause>>(f.GetRawText())
                            : new List<FilterClause>(),
                        OrderBy = root.TryGetProperty("orderBy", out var o)
                            ? JsonSerializer.Deserialize<OrderClause>(o.GetRawText())
                            : null,
                        Top = root.TryGetProperty("top", out var t) ? t.GetInt32() : 20,
                        Select = root.TryGetProperty("select", out var s)
                            ? JsonSerializer.Deserialize<List<string>>(s.GetRawText())
                            : new List<string>()
                    };
                }
            }
            catch (JsonException)
            {
                // JSON 解析エラーは無視
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────
    // システムプロンプト（グローバル + 役割固有）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// システムプロンプトを構築（役割別）
    /// </summary>
    private string BuildSystemPrompt(string userRole, string? dbContextMarkdown = null)
    {
        string systemPrompt;

        _logger.LogInformation("[BuildSystemPrompt] 役割 {Role} のプロンプト構築", userRole);

        // グローバルAIプロンプト取得
        var frameworkPrompt = _skillLoader.GetSystemPrompt();

        // 役割別プロンプトファイルを選択
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

        var rolePrompt = LoadRolePromptFromMd(promptFile);

        // フレームワークプロンプトと役割プロンプトを統合
        systemPrompt = frameworkPrompt + Environment.NewLine + Environment.NewLine + rolePrompt;

        // プレースホルダーを置換
        systemPrompt = systemPrompt
            .Replace("{current_datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{business_hours}", _businessHours)
            .Replace("{project_name}", _projectName)
            .Replace("{user_role}", userRole);

        // DB 検索結果がある場合は追加
        if (!string.IsNullOrWhiteSpace(dbContextMarkdown))
        {
            systemPrompt += Environment.NewLine + Environment.NewLine;
            systemPrompt += "## DB 検索結果（参考）" + Environment.NewLine;
            systemPrompt += dbContextMarkdown;
        }

        _logger.LogInformation("[BuildSystemPrompt] 役割 {Role} プロンプト長: {Length} 文字", userRole, systemPrompt.Length);

        return systemPrompt;
    }

    private string LoadRolePromptFromMd(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var filePath = Path.Combine(baseDir, "skills", "jpiere", fileName);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("役割プロンプト {File} が見つかりません", filePath);
            return BuildFallbackRolePrompt(fileName);
        }

        var content = File.ReadAllText(filePath).Trim();

        // frontmatter を除去
        if (content.StartsWith("---"))
        {
            var end = content.IndexOf("---", 3);
            if (end >= 0)
            {
                content = content[(end + 3)..].Trim();
            }
        }

        return content;
    }

    private string BuildFallbackRolePrompt(string fileName)
    {
        var role = fileName.Replace("_system-prompt-", "").Replace(".md", "");
        var sb = new StringBuilder();

        sb.AppendLine($"あなたはJPiere契約サービスの{role} AI アシスタントです。");
        sb.AppendLine("業務を支援し、データ照会・分析・推奨アクションの提供を行います。");
        sb.AppendLine("権限外の操作には応じず、適切な担当者に引継ぎを提案してください。");
        sb.AppendLine();
        sb.AppendLine($"現在の日時：{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"営業時間：{_businessHours}");

        return sb.ToString();
    }

    private string GetWelcomeMessage(string? userRole)
    {
        return userRole switch
        {
            "employee" => "こんにちは！JPiere の AI 業務アシスタントです。📋\n契約・見積・TODO の照会など、業務全般を支援します！",
            "contract_manager" => "こんにちは！JPiere 契約担当 AI アシスタントです。💼\n契約・見積・請求の作成・分析をお手伝いします！",
            "accountant" => "こんにちは！JPiere 会計担当 AI アシスタントです。💰\n仕訳・会計・入金・支払の管理を支援します！",
            "purchaser" => "こんにちは！JPiere 購買担当 AI アシスタントです。📦\n発注・受入・AP請求・支払のフローを支援します！",
            "approver" => "こんにちは！JPiere 承認 AI アシスタントです。✅\n承認ワークフローの確認・処理を支援します！",
            "admin" => "こんにちは！JPiere 管理者 AI アシスタントです。⚙️\nシステム全体的管理・設定変更を支援します！",
            _ => "こんにちは！JPiere AI アシスタントです。🤖\n業務のご相談は何でもどうぞ！"
        };
    }

    // ─────────────────────────────────────────────────────────
    // DB クエリ実行（業務ロジック）
    // ─────────────────────────────────────────────────────────

    private async Task<(string ResultText, List<Dictionary<string, string>>? DataRows, string Intent, string? NavUrl, string? NavLabel)>
        ExecuteQueryDataToolAsync(ParsedQueryParams queryParams, string? userMessage = null, string? userRole = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(queryParams.Entity))
                return ("entity が指定されていません。", null, "general", null, null);

            _logger.LogInformation("query_data 実行：entity={Entity}, action={Action}, role={Role}",
                queryParams.Entity, queryParams.Action, userRole);

            // 権限チェック（簡易版）
            if (!IsEntityAccessible(queryParams.Entity, userRole))
            {
                return ($"エンティティ '{queryParams.Entity}' へのアクセス権限がありません。", null, "access_denied", null, null);
            }

            var (data, total) = await _queryExecutor.ExecuteAsync(queryParams, _projectName);

            // count アクションでも簡潔一覧 + 詳細リンクを提示
            if (string.Equals(queryParams.Action, "count", StringComparison.OrdinalIgnoreCase))
            {
                var listTop = queryParams.Top.GetValueOrDefault(5);
                if (listTop <= 0) listTop = 5;

                var listParams = new ParsedQueryParams
                {
                    Entity = queryParams.Entity,
                    Action = "list",
                    Filters = queryParams.Filters,
                    OrderBy = queryParams.OrderBy,
                    Select = queryParams.Select,
                    Top = listTop
                };

                var (listData, listTotal) = await _queryExecutor.ExecuteAsync(listParams, _projectName);
                data = listData ?? new List<IDictionary<string, object?>>();
            }

            string markdown = _queryFormatter.FormatAsMarkdown(data, queryParams, total, _projectName);

            var dataRows = data.Count > 0
                ? data.Select(d => d.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "")).ToList()
                : null;

            var navUrl = $"/{_projectName}/DynamicEntity/Index?entity={queryParams.Entity}";
            var navLabel = $"{queryParams.Entity} 一覧を開く";

            return (markdown, dataRows, queryParams.Entity, navUrl, navLabel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "query_data ツール実行失敗");
            return ($"クエリ実行エラー：{ex.Message}", null, "general", null, null);
        }
    }

    /// <summary>
    /// 役割とエンティティのアクセス可否を判定
    /// </summary>
    private bool IsEntityAccessible(string entity, string? userRole)
    {
        if (string.IsNullOrEmpty(userRole)) return false;

        // 役割別アクセス許可マトリックス（簡易版）
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
    // エスカレーション処理
    // ─────────────────────────────────────────────────────────

    private (string Intent, bool NeedsHandover, string Priority) DetectEscalation(string message)
    {
        var complaintKeywords = new[] { "苦情", "不満", "怒り", "問題", "投诉", "complaint" };
        var urgentKeywords = new[] { "緊急", "至急", "すぐ", "立刻", "urgent", "emergency" };

        var isComplaint = complaintKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase));
        var isUrgent = urgentKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (isComplaint)
            return ("complaint", true, "high");
        if (isUrgent)
            return ("urgent", true, "high");

        return ("general", false, "normal");
    }

    private double EstimateSentiment(string message)
    {
        var negativeKeywords = new[] { "最悪", "ひどい", "ダメ", "悪い", "不满", "terrible", "awful" };
        var positiveKeywords = new[] { "最高", "良い", "素晴らしい", "满意", "great", "excellent" };

        var lower = message.ToLower();

        if (negativeKeywords.Any(k => lower.Contains(k))) return -0.8;
        if (positiveKeywords.Any(k => lower.Contains(k))) return 0.8;

        return 0.0;
    }

    private async Task<ChatMessageResult> HandleEscalationAsync(
        string conversationId, string userMessage,
        string intent, string priority, double sentimentScore,
        string now, string userRole, System.Diagnostics.Stopwatch sw)
    {
        var handoverId = $"HO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..28];
        var reason = intent == "complaint" ? "complaint" : (sentimentScore < _escalationSentimentThreshold ? "negative_sentiment" : "user_request");

        // 対象部門を役割から推定
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
                HId = handoverId,
                CId = conversationId,
                Reason = reason,
                Priority = priority,
                Dept = dept,
                Notes = $"ユーザーメッセージ：{userMessage[..Math.Min(200, userMessage.Length)]}",
                Now = now,
                UserRole = userRole
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
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds
        };
    }

    // ─────────────────────────────────────────────────────────
    // 補助メソッド
    // ─────────────────────────────────────────────────────────

    private async Task<IEnumerable<(string Role, string Content)>> GetRecentMessagesAsync(string conversationId, int count)
    {
        return await _db.QueryAsync<(string, string)>(@"
SELECT sender, content FROM ai_messages
WHERE conversation_id = @Id
ORDER BY sent_at DESC
LIMIT @Count",
            new { Id = conversationId, Count = count });
    }

    private async Task SaveMessageAsync(string messageId, string conversationId, string sender, string content, string timestamp, string? intent = null, double confidence = 0.9, double sentiment = 0)
    {
        await _db.ExecuteAsync(@"
INSERT INTO ai_messages
  (message_id, conversation_id, sender, content, intent, confidence_score, sentiment_score, sent_at, created_at)
VALUES
  (@MessageId, @ConversationId, @Sender, @Content, @Intent, @Confidence, @Sentiment, @Timestamp, @Timestamp)",
            new { MessageId = messageId, ConversationId = conversationId, Sender = sender, Content = content, Intent = intent ?? "general", Confidence = confidence, Sentiment = sentiment, Timestamp = timestamp });
    }

    private List<string> GetRoleQuickReplies(string userRole, string intent)
    {
        return userRole switch
        {
            "employee" => new List<string> { "契約を確認", "TODOを確認", "見積を確認" },
            "contract_manager" => new List<string> { "今月の契約", "未請求一覧", "期限切れ契約" },
            "accountant" => new List<string> { "仕訳を確認", "未収一覧", "月次損益" },
            "purchaser" => new List<string> { "発注状況", "未受入一覧", "仕入先別購買" },
            "approver" => new List<string> { "承認待ち", "承認統計", "却下案件" },
            "admin" => new List<string> { "システム状況", "ユーザー統計", "エラーログ" },
            _ => new List<string> { "契約を検索", "TODOを確認", "お問い合わせ" }
        };
    }

    private string GetTemplateResponse(string type)
    {
        return type switch
        {
            "error" => "申し訳ございませんが、現在データ照会機能が利用できない状態です。しばらくお待ちください。",
            _ => "お問い合わせいただき、ありがとうございます。担当者よりご連絡いたします。"
        };
    }
}
