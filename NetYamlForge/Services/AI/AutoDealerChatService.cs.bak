// ファイル概要: auto-dealer-demo 専用 AI チャットサービス。
// 新フロー: AI が自然言語を解析 → query_data ツールで DB クエリを判断・実行
//           → 結果を受け取って最終応答を生成（顧客・スタッフ共通パイプライン）
// 応答生成の優先順位: Claude API (Tool Use) → CLI + QueryParser → テンプレート

using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// auto-dealer-demo AI チャットの中核サービス。
/// AI 主導の DB クエリ判断・実行・応答生成を担当します。
/// </summary>
public class AutoDealerChatService
{
    private readonly IDbConnection _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly CLIServiceFactory _cliFactory;
    private readonly CliConfig _cliConfig;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<AutoDealerChatService> _logger;
    private readonly QueryParserService _queryParser;
    private readonly QueryExecutionService _queryExecutor;
    private readonly QueryResultFormatter _queryFormatter;

    private string ClaudeApiKey => _config["AiWindow:Claude:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
    private string ClaudeModel => _config["AiWindow:Claude:Model"] ?? "claude-haiku-4-5-20251001";
    private int ClaudeMaxTokens => int.TryParse(_config["AiWindow:Claude:MaxTokens"], out var v) ? v : 1024;
    private string DealerName => _config["AiWindow:DealerName"] ?? "AI 窓口ディーラー";
    private string BusinessHours => _config["AiWindow:BusinessHours"] ?? "月〜土 9:00〜18:00";
    private bool CliFirst => bool.TryParse(_config["AiWindow:CliFirst"], out var c) ? c : true;
    private int CliTimeoutSeconds => int.TryParse(_config["AiWindow:CliTimeoutSeconds"], out var t) ? t : 8;
    private string ProjectName => _projectScope.IsSet ? _projectScope.Current.Name : "auto-dealer-demo";
    private string[] ProviderPriority =>
        _config.GetSection("AiWindow:ProviderPriority").Get<string[]>() ?? ["claude", "qwen", "gemini", "ollama"];

    public AutoDealerChatService(
        IDbConnection db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        CLIServiceFactory cliFactory,
        IOptions<CliConfig> cliConfig,
        ProjectScope projectScope,
        ILogger<AutoDealerChatService> logger,
        QueryParserService queryParser,
        QueryExecutionService queryExecutor,
        QueryResultFormatter queryFormatter)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _cliFactory = cliFactory;
        _cliConfig = cliConfig.Value;
        _projectScope = projectScope;
        _logger = logger;
        _queryParser = queryParser;
        _queryExecutor = queryExecutor;
        _queryFormatter = queryFormatter;
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
            ? $"こんにちは！{DealerName}のAI業務アシスタントです。🤝\nリード管理・予約確認・在庫照会など、業務に関することは何でもご相談ください！"
            : $"こんにちは！{DealerName}のAIカスタマーサポートです。🚗\n試乗・ご購入・サービスのご相談は何でもどうぞ！";

        return new ChatSessionResult { ConversationId = conversationId, WelcomeMessage = welcome };
    }

    // ─────────────────────────────────────────────────────────
    // メッセージ処理（顧客）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 顧客メッセージを処理します。
    /// フロー: エスカレーション判定 → AI が DB クエリを判断・実行 → 最終応答生成
    /// </summary>
    public async Task<ChatMessageResult> SendMessageAsync(string conversationId, string customerMessage)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // 1. エスカレーション判定（ルールベース・高速）
        var (escalationIntent, needsHandover, priority) = DetectEscalation(customerMessage);
        var sentimentScore = EstimateSentiment(customerMessage);

        // 2. 履歴取得 + メッセージ保存
        var history = await GetRecentMessagesAsync(conversationId, 10);
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "customer", customerMessage, now);

        // 3. エスカレーション処理（担当者へ引き継ぎ）
        if (needsHandover || sentimentScore < -0.5)
            return await HandleEscalationAsync(conversationId, customerMessage, escalationIntent, priority, sentimentScore, now, sw);

        // 4. AI が DB クエリを決定・実行・最終応答を生成
        var (responseText, resolvedIntent, dataRows, navUrl, navLabel) =
            await GenerateAiResponseAsync(customerMessage, isStaff: false, history);

        // 5. AI 応答を保存・会話更新
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, now, resolvedIntent, 0.9, sentimentScore);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET last_intent = @Intent, last_confidence = 0.9, sentiment_score = @Sentiment, updated_at = @Now
WHERE conversation_id = @Id",
            new { Intent = resolvedIntent, Sentiment = sentimentScore, Now = now, Id = conversationId });

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

    /// <summary>
    /// スタッフメッセージを処理します。
    /// フロー: AI が DB クエリを判断・実行 → 結果を含めた最終応答生成
    /// </summary>
    public async Task<ChatMessageResult> SendStaffMessageAsync(string conversationId, string staffMessage)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var history = await GetRecentMessagesAsync(conversationId, 10);
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "customer", staffMessage, now);

        // AI が DB クエリを判断・実行・最終応答を生成
        var (responseText, entityLabel, dataRows, navUrl, navLabel) =
            await GenerateAiResponseAsync(staffMessage, isStaff: true, history);

        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, now, entityLabel, 0.9, 0);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations SET last_intent = @Intent, updated_at = @Now WHERE conversation_id = @Id",
            new { Intent = entityLabel, Now = now, Id = conversationId });

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
    // AI 主導クエリ・応答生成パイプライン（顧客・スタッフ共通）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// AI が自然言語を解析し、必要に応じて DB クエリを実行して最終応答を生成します。
    /// 優先順位: Claude API (Tool Use) → CLI + QueryParser → テンプレート
    /// </summary>
    private async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        GenerateAiResponseAsync(string message, bool isStaff, IEnumerable<(string Role, string Content)> history)
    {
        var systemPrompt = BuildSystemPrompt(isStaff);

        // ① CLI Tool Use（CliFirst=true の場合は CLI を優先）
        if (CliFirst)
        {
            try
            {
                return await CallCliWithQueryToolAsync(message, history, isStaff, systemPrompt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CLI Tool Use 失敗。Claude API にフォールバック");
            }
        }

        // ② Claude API with Tool Use（CLI が無効 or 失敗した場合のフォールバック）
        if (!string.IsNullOrWhiteSpace(ClaudeApiKey))
        {
            try
            {
                return await CallClaudeWithQueryToolAsync(message, history, systemPrompt, BuildToolDefinitions(isStaff));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Claude API も失敗。テンプレートにフォールバック");
            }
        }

        // ③ テンプレートフォールバック
        var (fallbackIntent, _, _) = DetectEscalation(message);
        return (GetTemplateResponse(fallbackIntent), fallbackIntent, null, null, null);
    }

    // ─────────────────────────────────────────────────────────
    // CLI Tool Use（プロンプトエンジニアリングで Tool Use を模倣）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// CLI に Tool Use を模倣させます。
    /// Round 1: CLIがメッセージを解析 → DBが必要なら tool_call JSON を返す、不要なら直接回答
    /// Round 2（tool_call の場合のみ）: DB実行後、結果を渡してCLIが最終応答を生成
    /// </summary>
    private async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        CallCliWithQueryToolAsync(string message, IEnumerable<(string Role, string Content)> history, bool isStaff, string systemPrompt)
    {
        var sysWithToolInstructions = AppendCliToolCallInstructions(systemPrompt, isStaff);
        var prompt = BuildFinalResponsePrompt(message, history);

        // Round 1: CLIが判断（直接回答 or ツール呼び出しJSON）
        var round1 = await TryCliProvidersInOrderAsync(prompt, sysWithToolInstructions);
        if (round1 == null)
            throw new InvalidOperationException("CLIから応答なし");

        // tool_call JSON でなければそのまま返す
        if (!TryParseCliTool(round1, out var toolName, out var toolJson))
            return (round1, "general", null, null, null);

        // ツール実行
        using var toolDoc = JsonDocument.Parse(toolJson);
        var toolElement = toolDoc.RootElement.Clone();

        string toolResultText;
        List<Dictionary<string, string>>? dataRows = null;
        string intent = "general";
        string? navUrl = null, navLabel = null;

        if (toolName == "query_data")
        {
            var (rt, rows, qi, qNav, qLabel) = await ExecuteQueryDataToolAsync(toolElement);
            toolResultText = rt;
            dataRows = rows;
            intent = qi;
            navUrl = qNav;
            navLabel = qLabel;
        }
        else if (toolName == "create_appointment_request")
        {
            toolResultText = await ExecuteCreateAppointmentAsync(toolElement);
        }
        else
        {
            toolResultText = $"不明なツール: {toolName}";
        }

        // Round 2: DB結果を渡してCLIが最終応答を生成
        var sysWithData = BuildSystemPrompt(isStaff,
            dbContextMarkdown: toolName == "query_data" ? toolResultText : null);
        var round2Prompt = toolName == "query_data"
            ? prompt
            : $"{prompt}\n\n【処理結果】{toolResultText}";
        var finalResponse = await TryCliProvidersInOrderAsync(round2Prompt, sysWithData);

        return (finalResponse ?? toolResultText, intent, dataRows, navUrl, navLabel);
    }

    /// <summary>
    /// システムプロンプトに CLI 向けの tool_call 出力形式を追記します。
    /// </summary>
    private static string AppendCliToolCallInstructions(string systemPrompt, bool isStaff)
    {
        var sb = new StringBuilder(systemPrompt);
        sb.AppendLine();
        sb.AppendLine("## ツール呼び出しルール");
        sb.AppendLine("DBデータが必要な場合は、以下のJSON**だけ**を出力してください（説明文・前後のテキスト一切不要）:");
        sb.AppendLine("{\"tool_call\":\"query_data\",\"entity\":\"テーブル名\",\"filters\":[{\"field\":\"フィールド名\",\"op\":\"eq|like|gt|lt|gte|lte\",\"value\":\"値\"}],\"orderBy\":{\"field\":\"フィールド名\",\"dir\":\"asc|desc\"},\"top\":20}");
        sb.AppendLine("DBが不要な場合は通常の日本語で回答してください。");
        if (!isStaff)
            sb.AppendLine("予約を作成する場合: {\"tool_call\":\"create_appointment_request\",\"customer_name\":\"名前\",\"appointment_type\":\"test_drive|service|consultation\"}");
        return sb.ToString();
    }

    /// <summary>
    /// CLI の応答が tool_call JSON かどうかを判定します。
    /// </summary>
    private static bool TryParseCliTool(string response, out string toolName, out string rawJson)
    {
        toolName = "";
        rawJson = "";
        var trimmed = response.Trim();
        if (!trimmed.StartsWith("{")) return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.TryGetProperty("tool_call", out var tc))
            {
                toolName = tc.GetString() ?? "";
                rawJson = trimmed;
                return !string.IsNullOrWhiteSpace(toolName);
            }
        }
        catch (JsonException) { }
        return false;
    }

    // ─────────────────────────────────────────────────────────
    // Claude API Tool Use（query_data ツールで DB クエリを実行）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Claude API に query_data ツールを渡し、AI がクエリ実行・応答生成を担当します。
    /// Round 1: AI がメッセージを解析 → query_data ツールを呼ぶか直接回答
    /// Round 2: ツール結果（DB データ）を受け取り最終応答を生成
    /// </summary>
    private async Task<(string ResponseText, string Intent, List<Dictionary<string, string>>? DataRows, string? NavUrl, string? NavLabel)>
        CallClaudeWithQueryToolAsync(
            string message,
            IEnumerable<(string Role, string Content)> history,
            string systemPrompt,
            List<object> tools)
    {
        var messages = new List<object>();
        foreach (var (role, content) in history.Reverse())
            messages.Add(new { role = role == "ai" ? "assistant" : "user", content });
        messages.Add(new { role = "user", content = message });

        var intent = "general";
        List<Dictionary<string, string>>? dataRows = null;
        string? navUrl = null, navLabel = null;

        const int maxRounds = 3;
        for (int round = 0; round < maxRounds; round++)
        {
            var requestBody = new
            {
                model = ClaudeModel,
                max_tokens = ClaudeMaxTokens,
                system = systemPrompt,
                tools,
                messages
            };

            var client = _httpClientFactory.CreateClient("ClaudeApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", ClaudeApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(req);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;
            var contentArr = root.GetProperty("content");

            // AI が直接テキストで回答した場合（DB クエリ不要と判断）
            if (stopReason != "tool_use")
            {
                foreach (var block in contentArr.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text" &&
                        block.TryGetProperty("text", out var txt) &&
                        !string.IsNullOrWhiteSpace(txt.GetString()))
                    {
                        return (txt.GetString()!.Trim(), intent, dataRows, navUrl, navLabel);
                    }
                }
                break;
            }

            // AI がツールを呼び出した場合 → 実行して結果を返す
            var assistantContent = new List<object>();
            var toolResults = new List<object>();

            foreach (var block in contentArr.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var btype)) continue;

                if (btype.GetString() == "text" && block.TryGetProperty("text", out var t2))
                    assistantContent.Add(new { type = "text", text = t2.GetString() ?? "" });

                else if (btype.GetString() == "tool_use")
                {
                    var toolId   = block.TryGetProperty("id",    out var tid)   ? tid.GetString()   ?? "" : "";
                    var toolName = block.TryGetProperty("name",  out var tname) ? tname.GetString() ?? "" : "";
                    var input    = block.TryGetProperty("input", out var inp)   ? inp : default;

                    assistantContent.Add(new
                    {
                        type  = "tool_use",
                        id    = toolId,
                        name  = toolName,
                        input = input.ValueKind == JsonValueKind.Undefined
                            ? (object)new { } : JsonSerializer.Deserialize<object>(input.GetRawText())!
                    });

                    string toolResultText;
                    if (toolName == "query_data")
                    {
                        // AI が生成したクエリパラメータを使ってフレームワーク経由で DB クエリ実行
                        var (resultText, rows, queryIntent, queryNavUrl, queryNavLabel) =
                            await ExecuteQueryDataToolAsync(input);
                        toolResultText = resultText;
                        if (rows != null)
                        {
                            dataRows = rows;
                            intent   = queryIntent;
                            navUrl   = queryNavUrl;
                            navLabel = queryNavLabel;
                        }
                    }
                    else if (toolName == "create_appointment_request")
                    {
                        toolResultText = await ExecuteCreateAppointmentAsync(input);
                    }
                    else
                    {
                        toolResultText = $"不明なツール: {toolName}";
                    }

                    toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = toolResultText });
                }
            }

            // DB 結果をメッセージに追加し、次ラウンドで AI が最終応答を生成
            messages =
            [
                ..messages,
                new { role = "assistant", content = (object)assistantContent },
                new { role = "user",      content = (object)toolResults }
            ];
        }

        return (GetTemplateResponse("general"), intent, dataRows, navUrl, navLabel);
    }

    /// <summary>
    /// AI が生成した query_data ツール入力をフレームワーク経由で安全に実行します。
    /// IDynamicCrudRepository を使用するため SQL インジェクションのリスクがありません。
    /// </summary>
    private async Task<(string ResultText, List<Dictionary<string, string>>? DataRows, string Intent, string? NavUrl, string? NavLabel)>
        ExecuteQueryDataToolAsync(JsonElement input)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var queryParams = JsonSerializer.Deserialize<ParsedQueryParams>(input.GetRawText(), options)
                ?? new ParsedQueryParams();

            if (string.IsNullOrWhiteSpace(queryParams.Entity))
                return ("entity が指定されていません。", null, "general", null, null);

            _logger.LogInformation("query_data 実行: entity={Entity}, action={Action}", queryParams.Entity, queryParams.Action);

            // IDynamicCrudRepository 経由で安全に SQL を生成・実行
            var (data, total) = await _queryExecutor.ExecuteAsync(queryParams, ProjectName);
            var markdown = _queryFormatter.FormatAsMarkdown(data, queryParams, total);

            var dataRows = data.Count > 0
                ? data.Select(d => d.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "")).ToList()
                : null;

            var navUrl   = $"/{ProjectName}/DynamicEntity/Index?entity={queryParams.Entity}";
            var navLabel = $"{queryParams.Entity} 一覧を開く";

            return (markdown, dataRows, queryParams.Entity, navUrl, navLabel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "query_data ツール実行失敗");
            return ($"クエリ実行エラー: {ex.Message}", null, "general", null, null);
        }
    }

    // ─────────────────────────────────────────────────────────
    // ツール定義
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// AI に渡すツール定義を構築します。
    /// query_data: DB クエリ実行（全チャンネル共通）
    /// create_appointment_request: 予約作成（顧客向けのみ）
    /// </summary>
    private static List<object> BuildToolDefinitions(bool isStaff)
    {
        var tools = new List<object>
        {
            new
            {
                name = "query_data",
                description = "データベースからビジネスデータを検索します。車両在庫・予約・顧客・営業リードの情報を取得できます。ユーザーがデータを尋ねた場合は必ずこのツールを使用してください。",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        entity = new
                        {
                            type = "string",
                            description = "検索対象テーブル名",
                            @enum = new[] { "vehicles", "service_appointments", "sales_leads", "customers" }
                        },
                        action = new
                        {
                            type = "string",
                            description = "クエリ種別: list=一覧取得, count=件数取得",
                            @enum = new[] { "list", "count" }
                        },
                        filters = new
                        {
                            type = "array",
                            description = "フィルタ条件。例: [{\"field\":\"status\",\"op\":\"eq\",\"value\":\"available\"}]",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    field  = new { type = "string", description = "フィールド名" },
                                    op     = new { type = "string", description = "演算子", @enum = new[] { "eq", "ne", "gt", "lt", "gte", "lte", "like", "between", "is_null" } },
                                    value  = new { description = "値（between の場合は開始値または today/this_week/this_month 等の相対日付）" },
                                    value2 = new { description = "終了値（between のみ）" }
                                },
                                required = new[] { "field", "op" }
                            }
                        },
                        orderBy = new
                        {
                            type = "object",
                            description = "並び順",
                            properties = new
                            {
                                field = new { type = "string" },
                                dir   = new { type = "string", @enum = new[] { "asc", "desc" } }
                            }
                        },
                        select = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "取得するフィールド名（省略時は全フィールド）"
                        },
                        top = new
                        {
                            type = "integer",
                            description = "最大取得件数（デフォルト: 20）"
                        }
                    },
                    required = new[] { "entity" }
                }
            }
        };

        // 顧客向けのみ: 予約作成ツール
        if (!isStaff)
        {
            tools.Add(new
            {
                name = "create_appointment_request",
                description = "試乗・サービス・相談の予約リクエストをDBに登録します。お客様の氏名・希望日時が揃った時点で使用してください。",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        customer_name    = new { type = "string", description = "お客様のお名前" },
                        phone            = new { type = "string", description = "お電話番号" },
                        appointment_type = new { type = "string", description = "予約種別", @enum = new[] { "test_drive", "service", "consultation" } },
                        preferred_date   = new { type = "string", description = "希望日（YYYY-MM-DD 形式）" },
                        preferred_time   = new { type = "string", description = "希望時間帯（例: 午前 / 14:00）" },
                        vehicle_interest = new { type = "string", description = "興味のある車種（任意）" },
                        notes            = new { type = "string", description = "備考・ご要望（任意）" }
                    },
                    required = new[] { "customer_name", "appointment_type" }
                }
            });
        }

        return tools;
    }

    // ─────────────────────────────────────────────────────────
    // システムプロンプト
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 顧客・スタッフ共通のシステムプロンプトを構築します。
    /// 利用可能なエンティティのフィールド情報と相対日付構文を含みます。
    /// </summary>
    private string BuildSystemPrompt(bool isStaff, string? dbContextMarkdown = null)
    {
        var sb = new StringBuilder();

        if (isStaff)
        {
            sb.AppendLine($"あなたは{DealerName}の社員向けAI業務アシスタントです。");
            sb.AppendLine("リード管理・予約確認・在庫照会・顧客情報の照会など業務全般を支援します。");
            sb.AppendLine("ユーザーがデータを尋ねた場合は **必ず query_data ツールを使用して** DBから最新情報を取得し、その結果に基づいて回答してください。");
        }
        else
        {
            sb.AppendLine($"あなたは{DealerName}のAIカスタマーサポートです。");
            sb.AppendLine("車両購入・試乗・サービスのご相談に対応します。");
            sb.AppendLine("在庫や予約状況が必要な場合は **query_data ツールを使用して** DBから最新情報を取得し、具体的な情報を案内してください。");
            sb.AppendLine("予約を作成する場合は create_appointment_request ツールを使用してください。");
        }

        sb.AppendLine();
        sb.AppendLine($"現在の日時: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"営業時間: {BusinessHours}");

        sb.AppendLine();
        sb.AppendLine("## query_data で利用可能なエンティティとフィールド");
        sb.AppendLine("**vehicles** (車両在庫)");
        sb.AppendLine("  フィールド: brand, model, grade, year, fuel_type, price, color, mileage, status");
        sb.AppendLine("  status の値: available(販売中) / reserved(商談中) / sold(売約済)");
        sb.AppendLine("  fuel_type の値: ガソリン / ハイブリッド / 電気 / ディーゼル");
        sb.AppendLine();
        sb.AppendLine("**service_appointments** (予約)");
        sb.AppendLine("  フィールド: appointment_type, preferred_date, status, notes");
        sb.AppendLine("  appointment_type: test_drive(試乗) / service(整備) / consultation(相談)");
        sb.AppendLine("  status: pending(未確認) / confirmed(確定) / completed(完了) / cancelled(キャンセル)");
        sb.AppendLine();
        sb.AppendLine("**sales_leads** (営業リード)");
        sb.AppendLine("  フィールド: customer_id, status, vehicle_interest, budget_range, created_at");
        sb.AppendLine("  status: new / active / won / lost");
        sb.AppendLine();
        sb.AppendLine("**customers** (顧客)");
        sb.AppendLine("  フィールド: name, phone, email, tier_level");
        sb.AppendLine("  tier_level: standard / silver / gold / vip");

        sb.AppendLine();
        sb.AppendLine("## filters の日付相対指定");
        sb.AppendLine("value に以下の文字列を使用すると自動変換されます:");
        sb.AppendLine("today / yesterday / this_week / last_week / this_month / last_month / this_year / last_year");

        if (!string.IsNullOrWhiteSpace(dbContextMarkdown))
        {
            sb.AppendLine();
            sb.AppendLine("## DB 検索結果（参考）");
            sb.AppendLine(dbContextMarkdown);
        }

        if (!isStaff)
        {
            sb.AppendLine();
            sb.AppendLine("## 応答ルール");
            sb.AppendLine("- 丁寧な敬語で回答してください");
            sb.AppendLine("- 在庫データに基づいて具体的な車種・価格をご案内してください");
            sb.AppendLine("- 具体的な価格交渉・特別割引は「担当者にお繋ぎします」と案内してください");
        }

        return sb.ToString();
    }

    /// <summary>CLI 向け: 最終応答生成用のプロンプトを構築します。</summary>
    private static string BuildFinalResponsePrompt(string message, IEnumerable<(string Role, string Content)> history)
    {
        var sb = new StringBuilder();
        var histList = history.Reverse().ToList();
        if (histList.Count > 0)
        {
            sb.AppendLine("【直近の会話】");
            foreach (var (role, content) in histList.TakeLast(6))
                sb.AppendLine($"{(role == "customer" ? "ユーザー" : "AI")}: {content}");
            sb.AppendLine();
        }
        sb.AppendLine($"【現在のメッセージ】\n{message}");
        sb.AppendLine();
        sb.AppendLine("上記に対する応答を出力してください。説明・前置きは不要です。応答のみ出力してください。");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────
    // 予約作成（顧客向けツール実行）
    // ─────────────────────────────────────────────────────────

    private async Task<string> ExecuteCreateAppointmentAsync(JsonElement input)
    {
        var customerName    = input.TryGetProperty("customer_name",    out var cn) ? cn.GetString()  : "";
        var phone           = input.TryGetProperty("phone",            out var ph) ? ph.GetString()  : "";
        var appointmentType = input.TryGetProperty("appointment_type", out var at) ? at.GetString()  : "consultation";
        var preferredDate   = input.TryGetProperty("preferred_date",   out var pd) ? pd.GetString()  : null;
        var preferredTime   = input.TryGetProperty("preferred_time",   out var pt) ? pt.GetString()  : null;
        var vehicleInterest = input.TryGetProperty("vehicle_interest", out var vi) ? vi.GetString()  : null;
        var notes           = input.TryGetProperty("notes",            out var no) ? no.GetString()  : null;

        if (string.IsNullOrWhiteSpace(customerName))
            return "お名前をお教えください。";

        var appointmentId    = $"APT-{Guid.NewGuid():N}"[..28];
        var now              = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var preferredDateFull = preferredDate != null
            ? $"{preferredDate} {preferredTime ?? "09:00"}:00"
            : DateTime.Now.AddDays(3).ToString("yyyy-MM-dd 09:00:00");

        try
        {
            await _db.ExecuteAsync(@"
INSERT INTO service_appointments
  (appointment_id, appointment_type, preferred_date, status, notes, created_at)
VALUES
  (@Id, @Type, @Date, 'pending', @Notes, @Now)",
                new
                {
                    Id    = appointmentId,
                    Type  = appointmentType,
                    Date  = preferredDateFull,
                    Notes = $"AI予約リクエスト: {customerName} {phone} - {vehicleInterest} - {notes}",
                    Now   = now
                });

            var typeLabel = appointmentType switch
            {
                "test_drive"   => "試乗",
                "service"      => "サービス・整備",
                "consultation" => "ご相談",
                _              => "ご予約"
            };
            var dateLabel = preferredDate != null ? $" {preferredDate}" : "";
            return $"{typeLabel}のご予約リクエストを承りました（予約ID: {appointmentId}）。{dateLabel}に担当者より確認のご連絡をいたします。";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "予約作成失敗");
            return "予約リクエストの登録中にエラーが発生しました。お電話でのご予約をご利用ください。";
        }
    }

    // ─────────────────────────────────────────────────────────
    // エスカレーション
    // ─────────────────────────────────────────────────────────

    private async Task<ChatMessageResult> HandleEscalationAsync(
        string conversationId, string customerMessage,
        string intent, string priority, double sentimentScore,
        string now, System.Diagnostics.Stopwatch sw)
    {
        var handoverId = $"HO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..28];
        var reason = intent == "complaint" ? "complaint" : (sentimentScore < -0.5 ? "negative_sentiment" : "customer_request");
        var dept   = intent == "complaint" ? "service" : "sales";

        await _db.ExecuteAsync(@"
INSERT INTO ai_handovers
  (handover_id, conversation_id, reason, priority, target_department, status, handover_notes, escalated_at)
VALUES
  (@HId, @CId, @Reason, @Priority, @Dept, 'pending', @Notes, @Now)",
            new
            {
                HId = handoverId, CId = conversationId, Reason = reason,
                Priority = priority, Dept = dept,
                Notes = $"お客様メッセージ: {customerMessage[..Math.Min(200, customerMessage.Length)]}",
                Now = now
            });

        await _db.ExecuteAsync(@"
UPDATE ai_conversations SET status = 'escalated', updated_at = @Now WHERE conversation_id = @Id",
            new { Now = now, Id = conversationId });

        var escalationMsg = reason == "complaint"
            ? "ご不満をおかけして大変申し訳ございません。ただいま担当者にお繋ぎしております。少々お待ちください。🙇"
            : "担当者にお繋ぎします。少々お待ちください。通常 5〜15 分以内に対応いたします。";

        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", escalationMsg, now, reason, 0.9, sentimentScore);

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = escalationMsg,
            Intent = reason,
            SuggestHandover = true,
            HandoverId = handoverId,
            QuickReplies = [],
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds
        };
    }

    // ─────────────────────────────────────────────────────────
    // ポーリング・オペレーター機能
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

        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "agent",
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

    // ─────────────────────────────────────────────────────────
    // オペレーター向けデータ取得
    // ─────────────────────────────────────────────────────────

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
WHERE h.conversation_id = @CId ORDER BY h.escalated_at DESC LIMIT 1",
            new { CId = conversationId });
    }

    public async Task<IEnumerable<ConversationMessage>> GetMessagesAsync(string conversationId)
    {
        return await _db.QueryAsync<ConversationMessage>(@"
SELECT message_id AS MessageId, sender AS Sender, content AS Content,
       intent AS Intent, timestamp AS Timestamp
FROM ai_messages WHERE conversation_id = @Id ORDER BY timestamp ASC",
            new { Id = conversationId });
    }

    // ─────────────────────────────────────────────────────────
    // CLI プロバイダーチェーン
    // ─────────────────────────────────────────────────────────

    private async Task<string?> TryCliProvidersInOrderAsync(string prompt, string systemOverride)
    {
        foreach (var name in ProviderPriority)
        {
            var cli = _cliFactory.TryGetService(name);
            if (cli == null) continue;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(CliTimeoutSeconds));
                var raw = await cli.ExecuteAsync(
                    prompt, workingDirectory: Path.GetTempPath(),
                    sessionId: null, allowedTools: [],
                    systemPromptOverride: systemOverride, ct: cts.Token);

                var result = ExtractCliResponseText(raw);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    _logger.LogInformation("CLI応答成功 provider={Name}", name);
                    return result;
                }
            }
            catch (OperationCanceledException) { _logger.LogWarning("CLIタイムアウト ({Sec}s): {Name}", CliTimeoutSeconds, name); }
            catch (Exception ex)               { _logger.LogWarning(ex, "CLI失敗: {Name}", name); }
        }

        return null;
    }

    private static string ExtractCliResponseText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            if (!line.StartsWith("{")) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var t) && t.GetString() == "result" &&
                    root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    var text = r.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                }
            }
            catch (JsonException) { }
        }

        foreach (var line in lines)
        {
            if (!line.StartsWith("{")) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var t) || t.GetString() != "assistant") continue;
                if (!root.TryGetProperty("message", out var msg)) continue;
                if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;

                var textParts = new List<string>();
                foreach (var item in content.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var itemType) || itemType.GetString() != "text") continue;
                    if (item.TryGetProperty("text", out var textProp))
                    {
                        var txt = textProp.GetString();
                        if (!string.IsNullOrWhiteSpace(txt)) textParts.Add(txt);
                    }
                }
                if (textParts.Count > 0) return string.Join("\n", textParts).Trim();
            }
            catch (JsonException) { }
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("```") || line.StartsWith("---") || line.StartsWith("#") || line.StartsWith("{")) continue;
            if (line.Length < 5) continue;
            return line;
        }

        return lines.FirstOrDefault() ?? raw.Trim();
    }

    // ─────────────────────────────────────────────────────────
    // エスカレーション判定（ルールベース）
    // ─────────────────────────────────────────────────────────

    private static (string intent, bool needsHandover, string priority) DetectEscalation(string message)
    {
        var m = message.ToLowerInvariant();
        if (Contains(m, "苦情", "クレーム", "ひどい", "最悪", "怒", "詐欺", "訴える"))
            return ("complaint", true, "high");
        if (Contains(m, "担当者", "オペレーター", "人間", "スタッフ", "人に繋いで"))
            return ("human_agent", true, "medium");
        if (Contains(m, "高額", "値引き交渉", "特別価格", "大幅値引"))
            return ("high_value_deal", true, "medium");
        return ("general", false, "low");
    }

    private static double EstimateSentiment(string message)
    {
        var m = message.ToLowerInvariant();
        double score = 0;
        if (Contains(m, "ありがとう", "嬉しい", "満足", "良かった", "助かった")) score += 0.5;
        if (Contains(m, "苦情", "クレーム", "ひどい", "最悪", "怒", "不満"))    score -= 0.7;
        if (Contains(m, "問題", "困る", "変", "おかしい"))                       score -= 0.3;
        return Math.Max(-1.0, Math.Min(1.0, score));
    }

    private static bool Contains(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    // ─────────────────────────────────────────────────────────
    // テンプレート応答・クイックリプライ
    // ─────────────────────────────────────────────────────────

    private static string GetTemplateResponse(string intent) => intent switch
    {
        "greeting"            => "いらっしゃいませ！どのようなご用件でしょうか？試乗・購入・サービスのご相談など、お気軽にどうぞ。😊",
        "complaint"           => "ご不満をおかけして大変申し訳ございません。担当者にお繋ぎします。",
        "high_value_deal"     => "詳細なご相談は担当者が丁寧にご案内いたします。少々お待ちください。",
        _                     => "ご質問ありがとうございます。詳しい内容については担当者が丁寧にご案内いたします。他にご不明な点はございますか？"
    };

    private static List<string> GetCustomerQuickReplies(string intent) => intent switch
    {
        "vehicles"             => ["試乗の予約をする", "価格を教えて", "担当者に相談する"],
        "service_appointments" => ["予約を確認する", "予約を変更する", "担当者に相談する"],
        _                      => ["在庫車両を見たい", "試乗の予約をしたい", "車検・点検について", "担当者に相談する"]
    };

    private static List<string> GetStaffQuickReplies(string entity) => entity switch
    {
        "service_appointments" => ["本日の予約一覧", "未確認予約を確認", "予約を新規登録"],
        "sales_leads"          => ["ホットリードを表示", "停滞リードを確認", "新規リードを登録"],
        "customers"            => ["顧客を検索", "VIP顧客を確認", "法人顧客を確認"],
        "vehicles"             => ["在庫一覧を確認", "新着車両を確認", "在庫を追加"],
        _                      => ["予約を確認", "リードを確認", "顧客を検索", "在庫を確認"],
    };

    // ─────────────────────────────────────────────────────────
    // DB 操作
    // ─────────────────────────────────────────────────────────

    private async Task SaveMessageAsync(
        string messageId, string conversationId, string sender, string content, string now,
        string? intent = null, double confidence = 0, double sentiment = 0)
    {
        await _db.ExecuteAsync(@"
INSERT INTO ai_messages
  (message_id, conversation_id, sender, message_type, content, intent, confidence_score, sentiment_score, timestamp)
VALUES
  (@MId, @CId, @Sender, 'text', @Content, @Intent, @Conf, @Sent, @Now)",
            new { MId = messageId, CId = conversationId, Sender = sender, Content = content,
                  Intent = intent, Conf = confidence, Sent = sentiment, Now = now });
    }

    private async Task<IEnumerable<(string Role, string Content)>> GetRecentMessagesAsync(string conversationId, int limit)
    {
        var rows = await _db.QueryAsync(@"
SELECT sender, content FROM ai_messages
WHERE conversation_id = @Id ORDER BY timestamp DESC LIMIT @Limit",
            new { Id = conversationId, Limit = limit });

        return rows.Select(r => (Role: (string)r.sender, Content: (string)r.content));
    }
}

// ─────────────────────────────────────────────────────────
// DTO
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
    public string? Notes { get; init; }
    public string? EscalatedAt { get; init; }
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
