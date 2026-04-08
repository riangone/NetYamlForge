// ファイル概要：AI チャットサービス共通基底クラス
// AutoDealerChatService / JpiereChatService の共通ロジックを集約
// プロジェクト固有の差分は abstract/virtual メソッドで実装する

using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI.Providers;

namespace NetYamlForge.Services.AI;

/// <summary>
/// AI チャットサービス共通基底クラス。
/// AI 応答生成・エスカレーション検出・DB 操作の共通ロジックを提供します。
/// プロジェクト固有の差分（プロンプト・クイックリプライ等）は派生クラスで実装してください。
/// </summary>
public abstract class BaseChatService
{
    // ─────────────────────────────────────────────────────────
    // 共有フィールド
    // ─────────────────────────────────────────────────────────

    protected readonly IDbConnection _db;
    protected readonly CLIServiceFactory _cliFactory;
    protected readonly ILlmProvider _llmProvider;
    protected readonly SkillLoader _skillLoader;
    protected readonly ProjectScope _projectScope;
    protected readonly ILogger _logger;
    protected readonly QueryParserService _queryParser;
    protected readonly QueryExecutionService _queryExecutor;
    protected readonly QueryResultFormatter _queryFormatter;
    protected readonly TaskQueueService _taskQueue;
    protected readonly ProgressTracker _tracker;
    protected readonly ChatHistoryService _chatHistory;

    protected readonly string _projectName;
    protected readonly string _defaultProvider;
    protected readonly string _businessHours;
    protected readonly double _escalationSentimentThreshold;
    protected readonly int _chatResponseTimeoutSeconds;

    protected BaseChatService(
        IDbConnection db,
        CLIServiceFactory cliFactory,
        ILlmProvider llmProvider,
        SkillLoader skillLoader,
        ProjectScope projectScope,
        ILogger logger,
        QueryParserService queryParser,
        QueryExecutionService queryExecutor,
        QueryResultFormatter queryFormatter,
        TaskQueueService taskQueue,
        ProgressTracker tracker,
        ChatHistoryService chatHistory,
        IConfiguration config,
        string defaultProjectName)
    {
        _db = db;
        _cliFactory = cliFactory;
        _llmProvider = llmProvider;
        _skillLoader = skillLoader;
        _projectScope = projectScope;
        _logger = logger;
        _queryParser = queryParser;
        _queryExecutor = queryExecutor;
        _queryFormatter = queryFormatter;
        _taskQueue = taskQueue;
        _tracker = tracker;
        _chatHistory = chatHistory;

        _projectName = _projectScope.IsSet ? _projectScope.Current.Name : defaultProjectName;
        _defaultProvider = config["AiWindow:DefaultProvider"] ?? config["AICli:DefaultTool"] ?? "qwen";
        _businessHours = config["AiWindow:BusinessHours"] ?? "月〜金 9:00〜18:00";
        _escalationSentimentThreshold = double.TryParse(
            config["AiWindow:EscalationSentimentThreshold"], out var threshold) ? threshold : -0.5;
        _chatResponseTimeoutSeconds = int.TryParse(
            config["AiWindow:ChatResponseTimeoutSeconds"], out var timeout) && timeout > 0
            ? timeout
            : 3600; // デフォルト3600秒（60分）
    }

    // ─────────────────────────────────────────────────────────
    // Abstract メソッド（派生クラスで実装必須）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// システムプロンプトを構築する（プロジェクト・ロール固有）
    /// </summary>
    /// <param name="context">AutoDealer = "customer"/"staff"、JPiere = ロール名</param>
    protected abstract string BuildSystemPrompt(string context, string? dbContextMarkdown = null);

    /// <summary>
    /// ウェルカムメッセージを返す（プロジェクト・ロール固有）
    /// </summary>
    protected abstract string GetWelcomeMessage(string? context);

    /// <summary>
    /// クイックリプライ一覧を返す（プロジェクト・ロール固有）
    /// </summary>
    protected abstract List<string> GetQuickReplies(string context, string intent);

    // ─────────────────────────────────────────────────────────
    // AI 応答生成（共通）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// CLI ファーストの ILlmProvider を使用して応答を生成（複数プロバイダー自動フォールバック）
    /// </summary>
    protected async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        GenerateAiResponseAsync(string message, string context, IEnumerable<(string Role, string Content)> history)
    {
        var systemPrompt = BuildSystemPrompt(context);

        try
        {
            var prompt = BuildPromptWithHistory(message, history);

            _logger.LogInformation("AI 応答生成開始：provider=CliFirstChain, context={Context}, length={Length}, systemPromptLength={SystemPromptLength}",
                context, message?.Length ?? 0, systemPrompt?.Length ?? 0);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_chatResponseTimeoutSeconds));
            
            // ✨ 重要：systemPrompt を systemPromptOverride として渡す
            // これにより、CLI サービスは SkillLoader.GetSystemPrompt() ではなく、
            // ビジネス固有のシステムプロンプトを使用します
            var response = await ExecuteWithSystemPromptOverrideAsync(prompt, systemPrompt, cts.Token);

            _logger.LogInformation("AI 応答取得完了：responseLength={Length}", response?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.LogWarning("AI 応答が空です");
                return (GetTemplateResponse("error"), "error", null, null, null);
            }

            return await ProcessAiResponseAsync(response, message, context);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AI 応答生成がタイムアウトしました（{Timeout}秒）", _chatResponseTimeoutSeconds);
            return ("申し訳ございませんが、応答生成に時間がかかりすぎています。もう一度お試しください。", "timeout", null, null, null);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("CLI"))
        {
            _logger.LogError(ex, "すべての CLI プロバイダーが失敗しました");
            return (GetTemplateResponse("error"), "error", null, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 応答生成エラー：context={Context}", context);
            return (GetTemplateResponse("error"), "error", null, null, null);
        }
    }

    /// <summary>
    /// システムプロンプトを上書きして CLI を実行します。
    /// </summary>
    private async Task<string> ExecuteWithSystemPromptOverrideAsync(
        string prompt, string systemPrompt, CancellationToken ct)
    {
        // ✨ 重要：systemPrompt を systemPromptOverride として渡す
        // これにより、CLI サービス（QwenCode/Claude等）は SkillLoader.GetSystemPrompt() ではなく、
        // ビジネス固有のシステムプロンプト（例：汽车销售客服）を使用します
        return await _llmProvider.CompleteAsync(
            prompt, ct, systemPromptOverride: systemPrompt);
    }

    /// <summary>
    /// CLI 用のプロンプトを構築（システムプロンプト + ユーザープロンプト）
    /// </summary>
    private static string BuildCliPrompt(string userPrompt, string systemPrompt) =>
        $"{systemPrompt}\n\n---\n\n{userPrompt}";

    /// <summary>
    /// AI 応答を処理（DB クエリ実行、ツール呼び出し解析）
    /// </summary>
    private async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        ProcessAiResponseAsync(string response, string? userMessage, string context)
    {
        var queryData = TryParseQueryDataToolCall(response);

        if (queryData != null)
        {
            var (resultText, dataRows, intent, navUrl, navLabel) =
                await ExecuteQueryDataToolAsync(queryData, userMessage, context);
            return (resultText, intent, dataRows, navUrl, navLabel);
        }

        return (response, "general", null, null, null);
    }

    // ─────────────────────────────────────────────────────────
    // DB クエリ実行（共通、virtual で override 可）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// query_data ツール呼び出しを実行。
    /// JPiere のようなロールベースアクセス制御が必要な場合は override してください。
    /// </summary>
    protected virtual async Task<(string ResultText, List<Dictionary<string, string>>? DataRows, string Intent, string? NavUrl, string? NavLabel)>
        ExecuteQueryDataToolAsync(ParsedQueryParams queryParams, string? userMessage = null, string? context = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(queryParams.Entity))
                return ("entity が指定されていません。", null, "general", null, null);

            _logger.LogInformation("query_data 実行：entity={Entity}, action={Action}",
                queryParams.Entity, queryParams.Action);

            var (data, total) = await _queryExecutor.ExecuteAsync(queryParams, _projectName);

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

                var (listData, _) = await _queryExecutor.ExecuteAsync(listParams, _projectName);
                data = listData ?? new List<IDictionary<string, object?>>();
            }

            var markdown = _queryFormatter.FormatAsMarkdown(data, queryParams, total, _projectName);

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

    // ─────────────────────────────────────────────────────────
    // エスカレーション処理（共通）
    // ─────────────────────────────────────────────────────────

    protected (string Intent, bool NeedsHandover, string Priority) DetectEscalation(string message)
    {
        var complaintKeywords = new[] { "苦情", "不満", "怒り", "問題", "投诉", "complaint" };
        var urgentKeywords = new[] { "緊急", "至急", "すぐ", "立刻", "urgent", "emergency" };

        var isComplaint = complaintKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase));
        var isUrgent = urgentKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (isComplaint) return ("complaint", true, "high");
        if (isUrgent) return ("urgent", true, "high");

        return ("general", false, "normal");
    }

    protected double EstimateSentiment(string message)
    {
        var negativeKeywords = new[] { "最悪", "ひどい", "ダメ", "悪い", "不满", "terrible", "awful" };
        var positiveKeywords = new[] { "最高", "良い", "素晴らしい", "满意", "great", "excellent" };

        var lower = message.ToLower();

        if (negativeKeywords.Any(k => lower.Contains(k))) return -0.8;
        if (positiveKeywords.Any(k => lower.Contains(k))) return 0.8;

        return 0.0;
    }

    // ─────────────────────────────────────────────────────────
    // DB 操作（virtual：SQLスキーマが異なる場合は override）
    // ─────────────────────────────────────────────────────────

    protected virtual async Task SaveMessageAsync(
        string messageId, string conversationId, string sender, string content,
        string timestamp, string? intent = null, double confidence = 0.9, double sentiment = 0)
    {
        // デフォルト実装：auto-dealer-demo スキーマ（timestamp カラム）
        await _db.ExecuteAsync(@"
INSERT INTO ai_messages
  (message_id, conversation_id, sender, message_type, content, intent, confidence_score, sentiment_score, timestamp)
VALUES
  (@MessageId, @ConversationId, @Sender, 'text', @Content, @Intent, @Confidence, @Sentiment, @Timestamp)",
            new
            {
                MessageId = messageId, ConversationId = conversationId, Sender = sender,
                Content = content, Intent = intent ?? "general",
                Confidence = confidence, Sentiment = sentiment, Timestamp = timestamp
            });
    }

    protected virtual async Task<IEnumerable<(string Role, string Content)>> GetRecentMessagesAsync(
        string conversationId, int count)
    {
        // デフォルト実装：auto-dealer-demo スキーマ（timestamp カラム）
        return await _db.QueryAsync<(string, string)>(@"
SELECT sender, content FROM ai_messages
WHERE conversation_id = @Id
ORDER BY timestamp DESC
LIMIT @Count",
            new { Id = conversationId, Count = count });
    }

    // ─────────────────────────────────────────────────────────
    // 共通公開メソッド
    // ─────────────────────────────────────────────────────────

    public async Task SubmitFeedbackAsync(string conversationId, int rating, string? comment)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET feedback_rating = @Rating, feedback_comment = @Comment, updated_at = @Now
WHERE conversation_id = @Id",
            new { Rating = rating, Comment = (object?)comment ?? DBNull.Value, Now = now, Id = conversationId });

        _logger.LogInformation("[BaseChatService] フィードバック受信：conv={Id}, rating={Rating}", conversationId, rating);
    }

    // ─────────────────────────────────────────────────────────
    // 共通ユーティリティ（static）
    // ─────────────────────────────────────────────────────────

    protected static string BuildPromptWithHistory(
        string message, IEnumerable<(string Role, string Content)> history)
    {
        var sb = new StringBuilder();
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

    protected static ParsedQueryParams? TryParseQueryDataToolCall(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;

        string? jsonStr = null;

        // 1) Try to find JSON in a code block: ```json ... ```
        var codeBlockMatch = System.Text.RegularExpressions.Regex.Match(
            response, @"```json\s*([\s\S]*?)```", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (codeBlockMatch.Success)
        {
            jsonStr = codeBlockMatch.Groups[1].Value.Trim();
        }
        else
        {
            // 2) Try to find JSON containing 'tool_call' anywhere in the response
            var toolCallMatch = System.Text.RegularExpressions.Regex.Match(
                response, @"\{[\s\S]*""tool_call""[\s\S]*?\}", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (toolCallMatch.Success)
            {
                // Use bracket matching to extract well-formed JSON
                var start = response.IndexOf('{', toolCallMatch.Index);
                if (start >= 0)
                {
                    var depth = 0;
                    var end = -1;
                    var inString = false;
                    var escapeNext = false;
                    for (var i = start; i < response.Length; i++)
                    {
                        var c = response[i];
                        if (escapeNext) { escapeNext = false; continue; }
                        if (c == '\\' && inString) { escapeNext = true; continue; }
                        if (c == '"' && !escapeNext) { inString = !inString; continue; }
                        if (inString) continue;
                        if (c == '{') depth++;
                        else if (c == '}') { depth--; if (depth == 0) { end = i; break; } }
                    }
                    if (end > 0)
                        jsonStr = response.Substring(start, end - start + 1);
                }
            }
        }

        // 3) Fall back: check if response starts with {
        if (jsonStr == null)
        {
            var jsonStart = response.IndexOf('{');
            if (jsonStart < 0) return null;

            var depth = 0;
            var end = -1;
            var inString = false;
            var escapeNext = false;
            for (var i = jsonStart; i < response.Length; i++)
            {
                var c = response[i];
                if (escapeNext) { escapeNext = false; continue; }
                if (c == '\\' && inString) { escapeNext = true; continue; }
                if (c == '"' && !escapeNext) { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) { end = i; break; } }
            }
            if (end > 0)
                jsonStr = response.Substring(jsonStart, end - jsonStart + 1);
        }

        if (jsonStr == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tool_call", out var tc) || tc.GetString() != "query_data")
                return null;

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
        catch (JsonException)
        {
            return null;
        }
    }

    protected string GetTemplateResponse(string type) => type switch
    {
        "error" => "申し訳ございませんが、現在データ照会機能が利用できない状態です。しばらくお待ちください。",
        _ => "お問い合わせいただき、ありがとうございます。担当者よりご連絡いたします。"
    };

    protected string? GetWorkingDirectory(string? project)
    {
        if (string.IsNullOrEmpty(project))
            return Directory.GetCurrentDirectory();

        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "NetYamlForge", "projects", project);

        // フォールバック：直接パス
        if (!Directory.Exists(projectPath))
            projectPath = $"/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/{project}";

        return Directory.Exists(projectPath) ? projectPath : Directory.GetCurrentDirectory();
    }
}
