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
    /// <param name="message">ユーザーメッセージ</param>
    /// <param name="context">コンテキスト（customer/staff 等）</param>
    /// <param name="history">会話履歴</param>
    /// <param name="providerOverride">使用する CLI プロバイダー名。null の場合はデフォルトチェーンを使用</param>
    protected async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        GenerateAiResponseAsync(string message, string context, IEnumerable<(string Role, string Content)> history,
            string? providerOverride = null)
    {
        var systemPrompt = BuildSystemPrompt(context);

        try
        {
            var prompt = BuildPromptWithHistory(message, history);
            var providerLabel = providerOverride ?? "CliFirstChain";

            _logger.LogInformation("AI 応答生成開始：provider={Provider}, context={Context}, length={Length}, systemPromptLength={SystemPromptLength}",
                providerLabel, context, message?.Length ?? 0, systemPrompt?.Length ?? 0);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_chatResponseTimeoutSeconds));

            var response = await ExecuteWithSystemPromptOverrideAsync(prompt ?? string.Empty, systemPrompt ?? string.Empty, cts.Token, providerOverride);

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
    /// providerOverride が指定された場合はそのプロバイダーを優先して使用します。
    /// </summary>
    private async Task<string> ExecuteWithSystemPromptOverrideAsync(
        string prompt, string systemPrompt, CancellationToken ct, string? providerOverride = null)
    {
        // プロバイダー指定がある場合、そのプロバイダーを直接使用
        if (!string.IsNullOrWhiteSpace(providerOverride))
        {
            var cli = _cliFactory.TryGetService(providerOverride);
            if (cli != null)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var raw = await cli.ExecuteAsync(
                        prompt,
                        workingDirectory: Path.GetTempPath(),
                        sessionId: null,
                        allowedTools: [],
                        systemPromptOverride: systemPrompt,
                        ct: cts.Token);
                    // テキスト抽出はプロバイダーに任せる（CliFirstLlmProvider と同ロジック）
                    var extracted = ExtractResponseText(raw);
                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        _logger.LogInformation("プロバイダー指定実行成功: provider={Provider}", providerOverride);
                        return extracted;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "指定プロバイダー失敗、デフォルトチェーンへフォールバック: provider={Provider}", providerOverride);
                }
            }
            else
            {
                _logger.LogWarning("指定プロバイダーが見つかりません、デフォルトチェーンへフォールバック: provider={Provider}", providerOverride);
            }
        }

        return await _llmProvider.CompleteAsync(prompt, ct, systemPromptOverride: systemPrompt);
    }

    /// <summary>
    /// CLI 生出力からテキストを抽出します（JSON 形式対応）。
    /// </summary>
    private static string ExtractResponseText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("{")) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var t) && t.GetString() == "result" &&
                    root.TryGetProperty("result", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var text = r.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
                if (root.TryGetProperty("type", out var t2) && t2.GetString() == "assistant" &&
                    root.TryGetProperty("message", out var msg))
                {
                    if (msg.TryGetProperty("content", out var content) && content.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var item in content.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var ct2) && ct2.GetString() == "text" &&
                                item.TryGetProperty("text", out var txt))
                            {
                                var text = txt.GetString();
                                if (!string.IsNullOrWhiteSpace(text)) return text;
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }
        }
        return raw;
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
        
        _logger.LogInformation("[ProcessAiResponse] 工具调用解析结果: HasQueryData={HasData}, ResponseLength={Length}, ResponsePreview={Preview}",
            queryData != null, response?.Length, 
            response?.Substring(0, Math.Min(200, response?.Length ?? 0)));

        if (queryData != null)
        {
            _logger.LogInformation("[ProcessAiResponse] 执行查询工具: Entity={Entity}, Action={Action}",
                queryData.Entity, queryData.Action);
            
            var (resultText, dataRows, intent, navUrl, navLabel) =
                await ExecuteQueryDataToolAsync(queryData, userMessage, context);
            
            _logger.LogInformation("[ProcessAiResponse] 查询完成: DataRowsCount={Count}, ResultLength={Length}",
                dataRows?.Count ?? 0, resultText?.Length ?? 0);
            
            return (resultText ?? string.Empty, intent ?? "general", dataRows, navUrl, navLabel);
        }

        _logger.LogWarning("[ProcessAiResponse] AI 未输出工具调用，返回纯文本");
        return (response ?? string.Empty, "general", null, null, null);
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

    private static readonly string[] EscalationKeywords =
    {
        // 直接的な不満
        "苦情", "クレーム", "怒り", "不満", "訴える", "返金", "責任者", "解約",
        // 繰り返し・放置
        "何度も", "また同じ", "前回も", "ずっと待って", "いつになったら", "まだですか",
        // 強い否定
        "最悪", "ひどい", "あり得ない", "絶対おかしい", "誠意がない",
        // 脅し
        "弁護士", "消費者センター", "SNS", "口コミ", "評判", "炎上"
    };

    protected (string Intent, bool NeedsHandover, string Priority) DetectEscalation(string message)
    {
        var urgentKeywords = new[] { "緊急", "至急", "すぐ", "立刻", "urgent", "emergency" };

        var isEscalation = EscalationKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase));
        var isUrgent = urgentKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (isEscalation) return ("complaint", true, "high");
        if (isUrgent) return ("urgent", true, "high");

        return ("general", false, "normal");
    }

    protected double EstimateSentiment(string message)
    {
        var score = 0.0f;
        var lowerMsg = message.ToLowerInvariant();

        var negativeWeights = new Dictionary<string, float>
        {
            { "最悪" , -0.6f }, { "ひどい" , -0.5f }, { "怒り" , -0.5f },
            { "不満" , -0.4f }, { "何度も" , -0.35f }, { "まだですか" , -0.3f },
            { "クレーム" , -0.5f }, { "あり得ない" , -0.5f }
        };
        var positiveWeights = new Dictionary<string, float>
        {
            { "ありがとう" , 0.4f }, { "良かった" , 0.3f }, { "助かりました" , 0.4f },
            { "素晴らしい" , 0.5f }, { "丁寧" , 0.3f }
        };

        foreach (var (word, weight) in negativeWeights)
            if (lowerMsg.Contains(word)) score += weight;
        foreach (var (word, weight) in positiveWeights)
            if (lowerMsg.Contains(word)) score += weight;

        return Math.Clamp(score, -1.0f, 1.0f);
    }

    // ─────────────────────────────────────────────────────────
    // DB 操作（virtual：SQLスキーマが異なる場合は override）
    // ─────────────────────────────────────────────────────────

    protected virtual async Task SaveMessageAsync(
        string messageId, string conversationId, string sender, string content,
        string timestamp, string? intent = null, double confidence = 0.9, double sentiment = 0,
        List<UiComponent>? components = null)
    {
        // コンポーネントを JSON にシリアライズ
        string? componentsJson = null;
        if (components?.Count > 0)
        {
            try
            {
                componentsJson = JsonSerializer.Serialize(components, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "コンポーネントのシリアライズに失敗しました");
            }
        }

        // デフォルト実装：auto-dealer-demo スキーマ（timestamp カラム）
        await _db.ExecuteAsync(@"
INSERT INTO ai_messages
  (message_id, conversation_id, sender, message_type, content, intent, confidence_score, sentiment_score, components_json, timestamp)
VALUES
  (@MessageId, @ConversationId, @Sender, 'text', @Content, @Intent, @Confidence, @Sentiment, @ComponentsJson, @Timestamp)",
            new
            {
                MessageId = messageId, ConversationId = conversationId, Sender = sender,
                Content = content, Intent = intent ?? "general",
                Confidence = confidence, Sentiment = sentiment,
                ComponentsJson = (object?)componentsJson ?? DBNull.Value,
                Timestamp = timestamp
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
            var label = role switch { "ai" => "AI", "system" => "システム", _ => "ユーザー" };
            sb.AppendLine($"{label}: {content}");
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

    // ─────────────────────────────────────────────────────────
    // UI コンポーネント生成（virtual：派生クラスで override 可）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// インテントに応じたUIコンポーネントを生成する。
    /// デフォルトは null を返す（コンポーネント不使用）。派生クラスで override して実装する。
    /// </summary>
    protected virtual List<UiComponent>? BuildComponents(
        string intent,
        List<Dictionary<string, string>>? dataRows,
        string? context)
    {
        return null;
    }

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
