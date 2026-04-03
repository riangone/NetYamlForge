// ファイル概要：auto-dealer-demo 専用 AI チャットサービス（グローバル AI 統一版）
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
/// auto-dealer-demo AI チャットサービス（グローバル AI 統一版）
/// セッション管理、DB クエリ実行、分析レポート生成を担当
/// </summary>
public class AutoDealerChatService
{
    private readonly IDbConnection _db;
    private readonly CLIServiceFactory _cliFactory;
    private readonly SkillLoader _skillLoader;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<AutoDealerChatService> _logger;
    private readonly QueryParserService _queryParser;
    private readonly QueryExecutionService _queryExecutor;
    private readonly QueryResultFormatter _queryFormatter;
    private readonly TaskQueueService _taskQueue;
    private readonly ProgressTracker _tracker;
    private readonly ChatHistoryService _chatHistory;

    // 設定値
    private readonly string _dealerName;
    private readonly string _businessHours;
    private readonly string _projectName;
    private readonly string _defaultProvider;

    public AutoDealerChatService(
        IDbConnection db,
        CLIServiceFactory cliFactory,
        SkillLoader skillLoader,
        ProjectScope projectScope,
        ILogger<AutoDealerChatService> logger,
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

        _dealerName = config["AiWindow:DealerName"] ?? "AI 窓口ディーラー";
        _businessHours = config["AiWindow:BusinessHours"] ?? "月〜土 9:00〜18:00";
        _projectName = _projectScope.IsSet ? _projectScope.Current.Name : "auto-dealer-demo";
        // AiWindow に DefaultProvider がなければ AICli:DefaultTool をフォールバック
        _defaultProvider = config["AiWindow:DefaultProvider"] ?? config["AICli:DefaultTool"] ?? "qwen";
    }

    // ─────────────────────────────────────────────────────────
    // セッション管理
    // ─────────────────────────────────────────────────────────

    public async Task<ChatSessionResult> StartSessionAsync(string channel = "web")
    {
        var conversationId = $"CONV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await _db.ExecuteAsync(@"
INSERT INTO ai_conversations
  (conversation_id, channel, status, started_at, created_at, updated_at)
VALUES
  (@ConversationId, @Channel, 'active', @Now, @Now, @Now)",
            new { ConversationId = conversationId, Channel = channel, Now = now });

        var welcome = channel == "staff"
            ? $"こんにちは！{_dealerName}の AI 業務アシスタントです。🤝\nリード管理・予約確認・在庫照会など、業務に関することは何でもご相談ください！"
            : $"こんにちは！{_dealerName}の AI カスタマーサポートです。🚗\n試乗・ご購入・サービスのご相談は何でもどうぞ！";

        return new ChatSessionResult { ConversationId = conversationId, WelcomeMessage = welcome };
    }

    // ─────────────────────────────────────────────────────────
    // メッセージ処理（顧客）
    // ─────────────────────────────────────────────────────────

    public async Task<ChatMessageResult> SendMessageAsync(string conversationId, string customerMessage)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // 1. エスカレーション判定
        var (escalationIntent, needsHandover, priority) = DetectEscalation(customerMessage);
        var sentimentScore = EstimateSentiment(customerMessage);

        // 2. 履歴取得 + メッセージ保存
        var history = await GetRecentMessagesAsync(conversationId, 10);
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "customer", customerMessage, now);

        // 3. エスカレーション処理
        if (needsHandover || sentimentScore < -0.5)
            return await HandleEscalationAsync(conversationId, customerMessage, escalationIntent, priority, sentimentScore, now, sw);

        // 4. AI 応答生成（グローバル AI と共通ロジック）
        var (responseText, resolvedIntent, dataRows, navUrl, navLabel) =
            await GenerateAiResponseAsync(customerMessage, isStaff: false, history);

        // 5. AI 応答を保存・会話更新
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, now, resolvedIntent, 0.9, sentimentScore);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET last_intent = @Intent, last_confidence = 0.9, sentiment_score = @Sentiment, updated_at = @Now
WHERE conversation_id = @Id",
            new { Intent = resolvedIntent, Sentiment = sentimentScore, Now = now, Id = conversationId });

        // 6. グローバル AI 履歴にも保存（別タブ・ブラウザ再起動対応）
        // UserId は顧客識別子（conversationId を使用）
        // プロジェクト独立 DB に保存して他プロジェクトと隔離
        await _chatHistory.SaveMessageAsync(
            conversationId,
            customerMessage,
            "user",
            provider: _defaultProvider,
            chatContext: "dealer-customer",
            projectName: _projectName);
        await _chatHistory.SaveMessageAsync(
            conversationId,
            responseText,
            "assistant",
            provider: _defaultProvider,
            chatContext: "dealer-customer",
            projectName: _projectName);

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = responseText,
            Intent = resolvedIntent,
            SuggestHandover = false,
            QuickReplies = GetCustomerQuickReplies(resolvedIntent),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds
        };
    }

    // ─────────────────────────────────────────────────────────
    // メッセージ処理（スタッフ）
    // ─────────────────────────────────────────────────────────

    public async Task<ChatMessageResult> SendStaffMessageAsync(string conversationId, string staffMessage)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var history = await GetRecentMessagesAsync(conversationId, 10);
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "customer", staffMessage, now);

        // AI 応答生成（グローバル AI と共通ロジック）
        var (responseText, entityLabel, dataRows, navUrl, navLabel) =
            await GenerateAiResponseAsync(staffMessage, isStaff: true, history);

        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, now, entityLabel, 0.9, 0);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations SET last_intent = @Intent, updated_at = @Now WHERE conversation_id = @Id",
            new { Intent = entityLabel, Now = now, Id = conversationId });

        // グローバル AI 履歴にも保存（別タブ・ブラウザ再起動対応）
        // プロジェクト独立 DB に保存して他プロジェクトと隔離
        await _chatHistory.SaveMessageAsync(
            conversationId,
            staffMessage,
            "user",
            provider: _defaultProvider,
            chatContext: "dealer-staff",
            projectName: _projectName);
        await _chatHistory.SaveMessageAsync(
            conversationId,
            responseText,
            "assistant",
            provider: _defaultProvider,
            chatContext: "dealer-staff",
            projectName: _projectName);

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = responseText,
            Intent = entityLabel,
            SuggestHandover = false,
            QuickReplies = GetStaffQuickReplies(entityLabel),
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
    /// グローバル AI と同じ流式処理ロジックを使用
    /// </summary>
    private async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        GenerateAiResponseAsync(string message, bool isStaff, IEnumerable<(string Role, string Content)> history)
    {
        // システムプロンプトを構築（グローバル + 業務固有）
        var systemPrompt = BuildSystemPrompt(isStaff);

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

            _logger.LogDebug("AI 応答生成開始：provider={Provider}, messageLength={Length}",
                _defaultProvider, message?.Length ?? 0);

            // CLI を実行（非流式処理）
            var response = await ExecuteCliAsync(
                cliService,
                prompt,
                systemPrompt,
                _defaultProvider);

            _logger.LogDebug("AI 応答取得完了：responseLength={Length}", response?.Length ?? 0);

            // 応答を処理（業務ロジック：DB クエリ実行、分析レポート生成）
            return await ProcessAiResponseAsync(response, message, isStaff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 応答生成エラー：provider={Provider}, message={Message}",
                _defaultProvider, message);
            return (GetTemplateResponse("error"), "error", null, null, null);
        }
    }

    /// <summary>
    /// CLI を実行して AI 応答を取得
    /// 非流式処理を使用して AI モデルの重複応答を防止
    /// </summary>
    private async Task<string> ExecuteCliAsync(
        ICLIService cliService,
        string prompt,
        string systemPrompt,
        string provider)
    {
        // 作業ディレクトリを取得
        var workingDir = GetWorkingDirectory(_projectName);

        _logger.LogDebug("[AutoDealerChat] AI 実行開始：provider={Provider}", provider);

        // 非流式で CLI を実行（流式より安定）
        var response = await cliService.ExecuteAsync(
            prompt,
            workingDir,
            sessionId: null,
            allowedTools: null,
            systemPromptOverride: systemPrompt,
            CancellationToken.None);

        _logger.LogInformation("[AutoDealerChat] AI 応答取得完了：responseLength={Length}", response?.Length ?? 0);

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

        // 直接使用源代码中的项目目录
        // /home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/{project}
        var projectPath = $"/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/{project}";

        return Directory.Exists(projectPath) ? projectPath : Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// 履歴付きプロンプトを構築
    /// </summary>
    private static string BuildPromptWithHistory(string message, IEnumerable<(string Role, string Content)> history, string systemPrompt)
    {
        var sb = new StringBuilder();
        
        // システムプロンプト
        sb.AppendLine(systemPrompt);
        sb.AppendLine();
        
        // 会話履歴
        sb.AppendLine("【会話履歴】");
        foreach (var (role, content) in history.Reverse().Take(10))
        {
            sb.AppendLine($"{(role == "ai" ? "AI" : "ユーザー")}: {content}");
        }
        sb.AppendLine();
        
        // 現在のメッセージ
        sb.AppendLine("【現在のメッセージ】");
        sb.AppendLine(message);
        
        return sb.ToString();
    }

    /// <summary>
    /// AI 応答を処理（DB クエリ実行、分析レポート生成）
    /// </summary>
    private async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        ProcessAiResponseAsync(string response, string userMessage, bool isStaff)
    {
        // AI が query_data ツール呼び出しを希望しているか解析
        var queryData = TryParseQueryDataToolCall(response);
        
        if (queryData != null)
        {
            // DB クエリを実行
            var (resultText, dataRows, intent, navUrl, navLabel) =
                await ExecuteQueryDataToolAsync(queryData, userMessage);
            
            // 分析レポートは AI がシステムプロンプトに従って生成する
            // ここでは追加の処理は行わない
            return (resultText, intent, dataRows, navUrl, navLabel);
        }
        
        // ツール呼び出しがない場合は、AI の応答をそのまま返す
        return (response, "general", null, null, null);
    }

    /// <summary>
    /// AI 応答から query_data ツール呼び出しを解析
    /// </summary>
    private static ParsedQueryParams? TryParseQueryDataToolCall(string response)
    {
        var trimmed = response.Trim();
        
        // JSON 形式のツール呼び出しを検出
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("tool_call", out var tc) && tc.GetString() == "query_data")
                {
                    // query_data ツールのパラメータを抽出
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
    // システムプロンプト（グローバル + 業務固有）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// システムプロンプトを構築
    /// グローバル AI の提示詞 + 業務固有の提示詞を統合（方案 A：完全统一）
    /// </summary>
    private string BuildSystemPrompt(bool isStaff, string? dbContextMarkdown = null)
    {
        // 1. グローバル AI の提示詞を取得（フレームワーク開発 AI としての基本指示）
        //    但し、auto-dealer-demo は業務データアクセスが許可されているため、
        //    権限制限部分は業務用に上書きする
        var frameworkPrompt = _skillLoader.GetSystemPrompt();

        // 2. 業務固有のプロンプトを取得
        var autoDealerPrompt = LoadAutoDealerPromptFromMd(isStaff);

        // 3. グローバル AI 提示詞から「権限制限」セクションを業務用に置換
        //    auto-dealer-demo は顧客情報・車両在庫・販売リードの照会が許可されている
        var systemPrompt = frameworkPrompt
            .Replace("❌ **auto-dealer-demo の業務データへのアクセス**", "✅ **auto-dealer-demo の業務データへのアクセス**")
            .Replace("顧客情報・車両在庫・販売リードの照会は禁止", "顧客情報・車両在庫・販売リードの照会が可能")
            .Replace("業務ロジックの変更は禁止", "業務ロジックの変更は禁止（読み取り専用）");

        // 4. 業務固有プロンプトを追加
        systemPrompt += Environment.NewLine + Environment.NewLine;
        systemPrompt += "# 🚗 自動車販売ディーラー業務指示" + Environment.NewLine;
        systemPrompt += autoDealerPrompt;

        // 5. プレースホルダーを置換
        systemPrompt = systemPrompt
            .Replace("{current_datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{business_hours}", _businessHours)
            .Replace("{dealer_name}", _dealerName);

        // 6. DB 検索結果がある場合は追加
        if (!string.IsNullOrWhiteSpace(dbContextMarkdown))
        {
            systemPrompt += Environment.NewLine + Environment.NewLine;
            systemPrompt += "## DB 検索結果（参考）" + Environment.NewLine;
            systemPrompt += dbContextMarkdown;
        }

        return systemPrompt;
    }

    private string LoadAutoDealerPromptFromMd(bool isStaff)
    {
        var baseDir = AppContext.BaseDirectory;
        var filePath = Path.Combine(baseDir, "skills", "auto-dealer", 
            isStaff ? "_system-prompt-staff.md" : "_system-prompt-customer.md");
        
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("業務プロンプト {File} が見つかりません", filePath);
            return BuildFallbackAutoDealerPrompt(isStaff);
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

    private string BuildFallbackAutoDealerPrompt(bool isStaff)
    {
        var sb = new StringBuilder();
        
        if (isStaff)
        {
            sb.AppendLine($"あなたは{_dealerName}の社員向け AI 業務アシスタントです。");
            sb.AppendLine("リード管理・予約確認・在庫照会・顧客情報の照会など業務全般を支援します。");
            sb.AppendLine("データ照会時は、必ず「優先度分類 → リスト → 統計 → 推奨アクション」の形式で回答してください。");
        }
        else
        {
            sb.AppendLine($"あなたは{_dealerName}の AI カスタマーサポートです。");
            sb.AppendLine("車両購入・試乗・サービスのご相談に対応します。");
            sb.AppendLine("丁寧な敬語で回答してください。");
        }
        
        sb.AppendLine();
        sb.AppendLine($"現在の日時：{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"営業時間：{_businessHours}");
        
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────
    // DB クエリ実行（業務ロジック）
    // ─────────────────────────────────────────────────────────

    private async Task<(string ResultText, List<Dictionary<string, string>>? DataRows, string Intent, string? NavUrl, string? NavLabel)>
        ExecuteQueryDataToolAsync(ParsedQueryParams queryParams, string? userMessage = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(queryParams.Entity))
                return ("entity が指定されていません。", null, "general", null, null);

            _logger.LogInformation("query_data 実行：entity={Entity}, action={Action}", 
                queryParams.Entity, queryParams.Action);

            // IDynamicCrudRepository 経由で安全に SQL を生成・実行
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

            // 業務分析レポートを生成
            // AI がシステムプロンプトに従って分析レポート形式で応答を生成するため、
            // ここでは簡潔な Markdown 形式に変換するのみ
            string markdown;
            if (!string.IsNullOrEmpty(userMessage) && LooksLikeBusinessQuery(userMessage))
            {
                _logger.LogInformation("業務クエリ検出：userMessage={Message}", userMessage);
                // 業務クエリの場合は、AI に分析レポート生成を委ねる
                // queryFormatter は基本的な Markdown 形式を生成
                markdown = _queryFormatter.FormatAsMarkdown(data, queryParams, total, _projectName);
            }
            else
            {
                markdown = _queryFormatter.FormatAsMarkdown(data, queryParams, total, _projectName);
            }

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

    private static bool LooksLikeBusinessQuery(string message)
    {
        var m = message.ToLower();

        if (m.Contains("優先") || m.Contains("重要") || m.Contains("priority")) return true;
        if (m.Contains("分類") || m.Contains("分析") || m.Contains("レポート")) return true;
        if (m.Contains("今日") || m.Contains("本日") || m.Contains("today")) return true;
        if (m.Contains("今週") || m.Contains("今月") || m.Contains("this week") || m.Contains("this month")) return true;
        if (m.Contains("連絡") || m.Contains("フォロー") || m.Contains("contact") || m.Contains("follow")) return true;
        if (m.Contains("未連絡") || m.Contains("新規") || m.Contains("new")) return true;

        return false;
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
        // 簡易的な感情分析（実際には SentimentAnalyzer を使用）
        var negativeKeywords = new[] { "最悪", "ひどい", "ダメ", "悪い", "不满", "terrible", "awful" };
        var positiveKeywords = new[] { "最高", "良い", "素晴らしい", "满意", "great", "excellent" };

        var lower = message.ToLower();
        
        if (negativeKeywords.Any(k => lower.Contains(k))) return -0.8;
        if (positiveKeywords.Any(k => lower.Contains(k))) return 0.8;

        return 0.0;
    }

    private async Task<ChatMessageResult> HandleEscalationAsync(
        string conversationId, string customerMessage,
        string intent, string priority, double sentimentScore,
        string now, System.Diagnostics.Stopwatch sw)
    {
        var handoverId = $"HO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..28];
        var reason = intent == "complaint" ? "complaint" : (sentimentScore < -0.5 ? "negative_sentiment" : "customer_request");
        var dept = intent == "complaint" ? "service" : "sales";

        await _db.ExecuteAsync(@"
INSERT INTO ai_handovers
  (handover_id, conversation_id, reason, priority, target_department, status, handover_notes, escalated_at)
VALUES
  (@HId, @CId, @Reason, @Priority, @Dept, 'pending', @Notes, @Now)",
            new
            {
                HId = handoverId, CId = conversationId, Reason = reason,
                Priority = priority, Dept = dept,
                Notes = $"お客様メッセージ：{customerMessage[..Math.Min(200, customerMessage.Length)]}",
                Now = now
            });

        await _db.ExecuteAsync(@"
UPDATE ai_conversations SET status = 'escalated', updated_at = @Now WHERE conversation_id = @Id",
            new { Now = now, Id = conversationId });

        var escalationMsg = reason == "complaint"
            ? "ご不満をおかけして大変申し訳ございません。ただいま担当者にお繋ぎします。少々お待ちください。🙇"
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
ORDER BY timestamp DESC
LIMIT @Count",
            new { Id = conversationId, Count = count });
    }

    private async Task SaveMessageAsync(string messageId, string conversationId, string sender, string content, string timestamp, string? intent = null, double confidence = 0.9, double sentiment = 0)
    {
        await _db.ExecuteAsync(@"
INSERT INTO ai_messages
  (message_id, conversation_id, sender, message_type, content, intent, confidence_score, sentiment_score, timestamp)
VALUES
  (@MessageId, @ConversationId, @Sender, 'text', @Content, @Intent, @Confidence, @Sentiment, @Timestamp)",
            new { MessageId = messageId, ConversationId = conversationId, Sender = sender, Content = content, Intent = intent ?? "general", Confidence = confidence, Sentiment = sentiment, Timestamp = timestamp });
    }

    private List<string> GetCustomerQuickReplies(string intent)
    {
        return intent switch
        {
            "vehicle_inquiry" => new List<string> { "在庫を確認", "試乗を予約", "価格を聞く" },
            "appointment" => new List<string> { "予約を変更", "予約をキャンセル", "新しい予約" },
            _ => new List<string> { "車両を探す", "試乗を予約する", "お問い合わせ" }
        };
    }

    private List<string> GetStaffQuickReplies(string intent)
    {
        return intent switch
        {
            "sales_leads" => new List<string> { "新規リード", "フォローアップ必要", "成約済み" },
            "customers" => new List<string> { "VIP 顧客", "未連絡顧客", "購入履歴" },
            _ => new List<string> { "顧客を検索", "リードを確認", "予約を確認" }
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

    // ─────────────────────────────────────────────────────────
    // オペレーター機能（既存ロジックを維持）
    // ─────────────────────────────────────────────────────────

    public async Task<IEnumerable<ChatPollMessage>> GetUpdatesAsync(string conversationId, DateTime? since)
    {
        var sinceStr = since?.ToString("yyyy-MM-dd HH:mm:ss")
                       ?? DateTime.UtcNow.AddMinutes(-30).ToString("yyyy-MM-dd HH:mm:ss");

        return await _db.QueryAsync<ChatPollMessage>(@"
SELECT message_id AS MessageId, sender AS Sender, content AS Content, timestamp AS Timestamp
FROM ai_messages
WHERE conversation_id = @Id AND sender IN ('agent', 'ai') AND timestamp > @Since
ORDER BY timestamp ASC",
            new { Id = conversationId, Since = sinceStr });
    }

    public async Task OperatorReplyAsync(string conversationId, string operatorId, string message)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "agent", message, now);
        await _db.ExecuteAsync(@"
UPDATE ai_handovers
SET status = 'in_progress', assigned_to_user_id = @OpId, assigned_at = COALESCE(assigned_at, @Now), updated_at = @Now
WHERE conversation_id = @CId AND status IN ('pending','assigned')",
            new { OpId = operatorId, Now = now, CId = conversationId });
    }

    public async Task<bool> AcceptHandoverAsync(string handoverId, string operatorId)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var affected = await _db.ExecuteAsync(@"
UPDATE ai_handovers SET status = 'assigned', assigned_to_user_id = @OpId, assigned_at = @Now
WHERE handover_id = @HId AND status = 'pending'",
            new { OpId = operatorId, Now = now, HId = handoverId });
        return affected > 0;
    }

    public async Task ResolveHandoverAsync(string conversationId, string operatorId, string? resolutionNotes)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await _db.ExecuteAsync(@"
UPDATE ai_handovers
SET status = 'resolved', resolved_at = @Now, resolution_notes = @Notes
WHERE conversation_id = @CId AND assigned_to_user_id = @OpId",
            new { Now = now, Notes = resolutionNotes ?? "", CId = conversationId, OpId = operatorId });

        await _db.ExecuteAsync(@"
UPDATE ai_conversations SET status = 'completed', ended_at = @Now, updated_at = @Now WHERE conversation_id = @CId",
            new { Now = now, CId = conversationId });

        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai",
            "対応が完了しました。ご利用ありがとうございました。またのご来店をお待ちしております。🚗", now);
    }

    public async Task SubmitFeedbackAsync(string conversationId, int rating, string? comment)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await _db.ExecuteAsync(@"
INSERT OR IGNORE INTO ai_feedback (feedback_id, conversation_id, rating, feedback_text, category, created_at)
VALUES (@FId, @CId, @Rating, @Comment, 'other', @Now)",
            new { FId = $"FB-{Guid.NewGuid():N}"[..28], CId = conversationId, Rating = rating, Comment = comment ?? "", Now = now });
    }

    public async Task<OperatorHandoverDetail?> GetHandoverDetailAsync(string handoverId)
    {
        return await _db.QueryFirstOrDefaultAsync<OperatorHandoverDetail>(@"
SELECT h.handover_id AS HandoverId, h.conversation_id AS ConversationId,
       h.reason AS Reason, h.priority AS Priority, h.status AS Status,
       h.handover_notes AS Notes, h.escalated_at AS EscalatedAt,
       h.assigned_at AS AssignedAt, h.assigned_to_user_id AS AssignedToUserId,
       c.customer_id AS CustomerId, cu.name AS CustomerName,
       cu.tier_level AS CustomerTier, cu.phone AS CustomerPhone, cu.email AS CustomerEmail
FROM ai_handovers h
INNER JOIN ai_conversations c ON h.conversation_id = c.conversation_id
LEFT JOIN customers cu ON c.customer_id = cu.customer_id
WHERE h.handover_id = @HId",
            new { HId = handoverId });
    }

    public async Task<IEnumerable<OperatorHandoverDetail>> GetPendingHandoversAsync()
    {
        return await _db.QueryAsync<OperatorHandoverDetail>(@"
SELECT h.handover_id AS HandoverId, h.conversation_id AS ConversationId,
       h.reason AS Reason, h.priority AS Priority, h.status AS Status,
       h.handover_notes AS Notes, h.escalated_at AS EscalatedAt,
       h.assigned_at AS AssignedAt, h.assigned_to_user_id AS AssignedToUserId,
       c.customer_id AS CustomerId, cu.name AS CustomerName,
       cu.tier_level AS CustomerTier, cu.phone AS CustomerPhone, cu.email AS CustomerEmail
FROM ai_handovers h
INNER JOIN ai_conversations c ON h.conversation_id = c.conversation_id
LEFT JOIN customers cu ON c.customer_id = cu.customer_id
WHERE h.status IN ('pending', 'assigned', 'in_progress')
ORDER BY CASE h.priority WHEN 'urgent' THEN 1 WHEN 'high' THEN 2 WHEN 'medium' THEN 3 ELSE 4 END,
         h.escalated_at ASC");
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(string conversationId)
    {
        return await _db.QueryAsync<ChatMessage>(@"
SELECT message_id AS MessageId, sender AS Sender, content AS Content, timestamp AS Timestamp, intent AS Intent
FROM ai_messages
WHERE conversation_id = @Id
ORDER BY timestamp ASC",
            new { Id = conversationId });
    }

    public async Task<OperatorHandoverDetail?> GetHandoverByConversationAsync(string conversationId)
    {
        return await _db.QueryFirstOrDefaultAsync<OperatorHandoverDetail>(@"
SELECT h.handover_id AS HandoverId, h.conversation_id AS ConversationId,
       h.reason AS Reason, h.priority AS Priority, h.status AS Status,
       h.handover_notes AS Notes, h.escalated_at AS EscalatedAt,
       h.assigned_at AS AssignedAt, h.assigned_to_user_id AS AssignedToUserId,
       c.customer_id AS CustomerId, cu.name AS CustomerName,
       cu.tier_level AS CustomerTier, cu.phone AS CustomerPhone, cu.email AS CustomerEmail
FROM ai_handovers h
INNER JOIN ai_conversations c ON h.conversation_id = c.conversation_id
LEFT JOIN customers cu ON c.customer_id = cu.customer_id
WHERE h.conversation_id = @CId
ORDER BY h.escalated_at DESC
LIMIT 1",
            new { CId = conversationId });
    }
}

// ─────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────

public record ChatSessionResult
{
    public string ConversationId { get; init; } = "";
    public string WelcomeMessage { get; init; } = "";
}

public record ChatMessageResult
{
    public string ResponseText { get; init; } = "";
    public string Intent { get; init; } = "";
    public bool SuggestHandover { get; init; }
    public string? HandoverId { get; init; }
    public List<string> QuickReplies { get; init; } = [];
    public int ProcessingTimeMs { get; init; }
    public List<Dictionary<string, string>>? DataRows { get; init; }
    public string? NavigationUrl { get; init; }
    public string? NavigationLabel { get; init; }
}

public record ChatPollMessage
{
    public string MessageId { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Content { get; init; } = "";
    public string Timestamp { get; init; } = "";
}

public record OperatorHandoverDetail
{
    public string HandoverId { get; init; } = "";
    public string ConversationId { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Priority { get; init; } = "";
    public string Status { get; init; } = "";
    public string Notes { get; init; } = "";
    public string EscalatedAt { get; init; } = "";
    public string? AssignedAt { get; init; }
    public string? AssignedToUserId { get; init; }
    public string? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerTier { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerEmail { get; init; }
}

public record ConversationMessage
{
    public string MessageId { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Content { get; init; } = "";
    public string? Intent { get; init; }
    public string Timestamp { get; init; } = "";
}
