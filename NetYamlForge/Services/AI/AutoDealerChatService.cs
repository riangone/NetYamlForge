// ファイル概要: auto-dealer-demo 専用 AI チャットサービスです。
// 応答生成の優先順位: CLI（CLIServiceFactory）→ Claude API（直接）→ テンプレートフォールバック
// DB に対話履歴・エスカレーション情報を永続化します。

using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI;

/// <summary>
/// auto-dealer-demo AI チャットの中核サービス。
/// CLI ファースト応答生成・DB 永続化・エスカレーション判定を担当します。
/// </summary>
public class AutoDealerChatService
{
    private readonly IDbConnection _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly CLIServiceFactory _cliFactory;
    private readonly CliConfig _cliConfig;
    private readonly ILogger<AutoDealerChatService> _logger;

    // AiWindow 設定キー
    private string ClaudeApiKey => _config["AiWindow:Claude:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
    private string ClaudeModel => _config["AiWindow:Claude:Model"] ?? "claude-haiku-4-5-20251001";
    private int ClaudeMaxTokens => int.TryParse(_config["AiWindow:Claude:MaxTokens"], out var v) ? v : 512;
    private string DealerName => _config["AiWindow:DealerName"] ?? "AI 窓口ディーラー";
    private string BusinessHours => _config["AiWindow:BusinessHours"] ?? "月〜土 9:00〜18:00";
    private bool FallbackToTemplate => bool.TryParse(_config["AiWindow:FallbackToTemplate"], out var f) ? f : true;
    private bool CliFirst => bool.TryParse(_config["AiWindow:CliFirst"], out var c) ? c : true;
    private int CliTimeoutSeconds => int.TryParse(_config["AiWindow:CliTimeoutSeconds"], out var t) ? t : 8;

    public AutoDealerChatService(
        IDbConnection db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        CLIServiceFactory cliFactory,
        IOptions<CliConfig> cliConfig,
        ILogger<AutoDealerChatService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _cliFactory = cliFactory;
        _cliConfig = cliConfig.Value;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────
    // セッション管理
    // ─────────────────────────────────────────────────────────

    /// <summary>AI 対話セッションを開始します。</summary>
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

        _logger.LogInformation("AI チャットセッション開始: {Id}, channel={Ch}", conversationId, channel);

        return new ChatSessionResult
        {
            ConversationId = conversationId,
            WelcomeMessage = $"こんにちは！{DealerName}のAIカスタマーサポートです。🚗\n試乗・ご購入・サービスのご相談は何でもどうぞ！"
        };
    }

    // ─────────────────────────────────────────────────────────
    // メッセージ処理
    // ─────────────────────────────────────────────────────────

    /// <summary>顧客メッセージを処理し AI 応答を返します。</summary>
    public async Task<ChatMessageResult> SendMessageAsync(string conversationId, string customerMessage)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // 1. 顧客メッセージを保存
        var customerMsgId = $"MSG-{Guid.NewGuid():N}"[..32];
        await SaveMessageAsync(customerMsgId, conversationId, "customer", customerMessage, now);

        // 2. 簡易意図・感情検出
        var (intent, needsHandover, priority) = DetectIntentAndEscalation(customerMessage);
        var sentimentScore = EstimateSentiment(customerMessage);

        // 3. エスカレーション判定
        if (needsHandover || sentimentScore < -0.5)
        {
            return await HandleEscalationAsync(conversationId, customerMessage, intent, priority, sentimentScore, now, sw);
        }

        // 4. Claude API (または テンプレート) で応答生成
        var history = await GetRecentMessagesAsync(conversationId, 10);
        var responseText = await GenerateResponseAsync(customerMessage, intent, history);

        // 5. AI 応答を保存
        var aiMsgId = $"MSG-{Guid.NewGuid():N}"[..32];
        await SaveMessageAsync(aiMsgId, conversationId, "ai", responseText, now, intent, 0.85, sentimentScore);

        // 6. 会話更新
        await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET last_intent = @Intent, last_confidence = 0.85, sentiment_score = @Sentiment, updated_at = @Now
WHERE conversation_id = @Id",
            new { Intent = intent, Sentiment = sentimentScore, Now = now, Id = conversationId });

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = responseText,
            Intent = intent,
            SuggestHandover = false,
            QuickReplies = GetQuickReplies(intent),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds
        };
    }

    /// <summary>エスカレーション処理 — handover 作成 + 通知メッセージ返却。</summary>
    private async Task<ChatMessageResult> HandleEscalationAsync(
        string conversationId, string customerMessage,
        string intent, string priority, double sentimentScore,
        string now, System.Diagnostics.Stopwatch sw)
    {
        // エスカレーション作成
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
                HId = handoverId,
                CId = conversationId,
                Reason = reason,
                Priority = priority,
                Dept = dept,
                Notes = $"お客様メッセージ: {customerMessage[..Math.Min(200, customerMessage.Length)]}",
                Now = now
            });

        // 会話ステータスを escalated に変更
        await _db.ExecuteAsync(@"
UPDATE ai_conversations SET status = 'escalated', updated_at = @Now WHERE conversation_id = @Id",
            new { Now = now, Id = conversationId });

        // エスカレーション通知メッセージを保存
        var escalationMsg = reason == "complaint"
            ? "ご不満をおかけして大変申し訳ございません。ただいま担当者にお繋ぎしております。少々お待ちください。🙇"
            : "担当者にお繋ぎします。少々お待ちください。通常 5〜15 分以内に対応いたします。";

        var aiMsgId = $"MSG-{Guid.NewGuid():N}"[..32];
        await SaveMessageAsync(aiMsgId, conversationId, "ai", escalationMsg, now, reason, 0.9, sentimentScore);

        _logger.LogInformation("エスカレーション作成: {HId}, conv={CId}, reason={Reason}", handoverId, conversationId, reason);
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
    // ポーリング（オペレーター返信を顧客が受け取る）
    // ─────────────────────────────────────────────────────────

    /// <summary>最後に確認した時刻以降のオペレーター・AIメッセージを返します。</summary>
    public async Task<IEnumerable<ChatPollMessage>> GetUpdatesAsync(string conversationId, DateTime? since)
    {
        var sinceStr = since?.ToString("yyyy-MM-dd HH:mm:ss")
                       ?? DateTime.UtcNow.AddMinutes(-30).ToString("yyyy-MM-dd HH:mm:ss");

        var rows = await _db.QueryAsync<ChatPollMessage>(@"
SELECT message_id AS MessageId,
       sender     AS Sender,
       content    AS Content,
       timestamp  AS Timestamp
FROM ai_messages
WHERE conversation_id = @Id
  AND sender IN ('agent', 'ai')
  AND timestamp > @Since
ORDER BY timestamp ASC",
            new { Id = conversationId, Since = sinceStr });

        return rows;
    }

    // ─────────────────────────────────────────────────────────
    // オペレーター機能
    // ─────────────────────────────────────────────────────────

    /// <summary>オペレーターが顧客に返信します。</summary>
    public async Task OperatorReplyAsync(string conversationId, string operatorId, string message)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var msgId = $"MSG-{Guid.NewGuid():N}"[..32];
        await SaveMessageAsync(msgId, conversationId, "agent", message, now);

        // handover を in_progress に
        await _db.ExecuteAsync(@"
UPDATE ai_handovers
SET status = 'in_progress', assigned_to_user_id = @OpId, assigned_at = COALESCE(assigned_at, @Now), updated_at = @Now
WHERE conversation_id = @CId AND status IN ('pending','assigned')",
            new { OpId = operatorId, Now = now, CId = conversationId });

        _logger.LogInformation("オペレーター返信 conv={CId}, operator={Op}", conversationId, operatorId);
    }

    /// <summary>エスカレーションを引き受けます（担当者アサイン）。</summary>
    public async Task<bool> AcceptHandoverAsync(string handoverId, string operatorId)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var affected = await _db.ExecuteAsync(@"
UPDATE ai_handovers
SET status = 'assigned', assigned_to_user_id = @OpId, assigned_at = @Now
WHERE handover_id = @HId AND status = 'pending'",
            new { OpId = operatorId, Now = now, HId = handoverId });
        return affected > 0;
    }

    /// <summary>エスカレーションを解決します。</summary>
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

        // 解決メッセージを保存
        var msgId = $"MSG-{Guid.NewGuid():N}"[..32];
        await SaveMessageAsync(msgId, conversationId, "agent",
            "対応が完了しました。ご利用ありがとうございました。またのご来店をお待ちしております。🚗", now);
    }

    /// <summary>顧客評価を保存します。</summary>
    public async Task SubmitFeedbackAsync(string conversationId, int rating, string? comment)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await _db.ExecuteAsync(@"
INSERT OR IGNORE INTO ai_feedback (feedback_id, conversation_id, rating, feedback_text, category, created_at)
VALUES (@FId, @CId, @Rating, @Comment, 'other', @Now)",
            new
            {
                FId = $"FB-{Guid.NewGuid():N}"[..28],
                CId = conversationId,
                Rating = rating,
                Comment = comment ?? "",
                Now = now
            });
    }

    // ─────────────────────────────────────────────────────────
    // オペレーター向けデータ取得
    // ─────────────────────────────────────────────────────────

    /// <summary>エスカレーション詳細を取得します（オペレーター画面用）。</summary>
    public async Task<OperatorHandoverDetail?> GetHandoverDetailAsync(string handoverId)
    {
        var row = await _db.QueryFirstOrDefaultAsync<OperatorHandoverDetail>(@"
SELECT h.handover_id       AS HandoverId,
       h.conversation_id   AS ConversationId,
       h.reason            AS Reason,
       h.priority          AS Priority,
       h.status            AS Status,
       h.handover_notes    AS Notes,
       h.escalated_at      AS EscalatedAt,
       h.assigned_at       AS AssignedAt,
       h.assigned_to_user_id AS AssignedToUserId,
       c.customer_id       AS CustomerId,
       cu.name             AS CustomerName,
       cu.tier_level       AS CustomerTier,
       cu.phone            AS CustomerPhone,
       cu.email            AS CustomerEmail
FROM ai_handovers h
INNER JOIN ai_conversations c ON h.conversation_id = c.conversation_id
LEFT JOIN customers cu ON c.customer_id = cu.customer_id
WHERE h.handover_id = @HId",
            new { HId = handoverId });

        return row;
    }

    /// <summary>未対応・対応中のエスカレーション一覧を返します（オペレーター一覧画面用）。</summary>
    public async Task<IEnumerable<OperatorHandoverDetail>> GetPendingHandoversAsync()
    {
        return await _db.QueryAsync<OperatorHandoverDetail>(@"
SELECT h.handover_id       AS HandoverId,
       h.conversation_id   AS ConversationId,
       h.reason            AS Reason,
       h.priority          AS Priority,
       h.status            AS Status,
       h.handover_notes    AS Notes,
       h.escalated_at      AS EscalatedAt,
       h.assigned_at       AS AssignedAt,
       h.assigned_to_user_id AS AssignedToUserId,
       c.customer_id       AS CustomerId,
       cu.name             AS CustomerName,
       cu.tier_level       AS CustomerTier,
       cu.phone            AS CustomerPhone,
       cu.email            AS CustomerEmail
FROM ai_handovers h
INNER JOIN ai_conversations c ON h.conversation_id = c.conversation_id
LEFT JOIN customers cu ON c.customer_id = cu.customer_id
WHERE h.status IN ('pending', 'assigned', 'in_progress')
ORDER BY
  CASE h.priority WHEN 'urgent' THEN 1 WHEN 'high' THEN 2 WHEN 'medium' THEN 3 ELSE 4 END,
  h.escalated_at ASC");
    }

    /// <summary>会話に紐づくエスカレーション詳細を返します。</summary>
    public async Task<OperatorHandoverDetail?> GetHandoverByConversationAsync(string conversationId)
    {
        return await _db.QueryFirstOrDefaultAsync<OperatorHandoverDetail>(@"
SELECT h.handover_id       AS HandoverId,
       h.conversation_id   AS ConversationId,
       h.reason            AS Reason,
       h.priority          AS Priority,
       h.status            AS Status,
       h.handover_notes    AS Notes,
       h.escalated_at      AS EscalatedAt,
       h.assigned_at       AS AssignedAt,
       h.assigned_to_user_id AS AssignedToUserId,
       c.customer_id       AS CustomerId,
       cu.name             AS CustomerName,
       cu.tier_level       AS CustomerTier,
       cu.phone            AS CustomerPhone,
       cu.email            AS CustomerEmail
FROM ai_handovers h
INNER JOIN ai_conversations c ON h.conversation_id = c.conversation_id
LEFT JOIN customers cu ON c.customer_id = cu.customer_id
WHERE h.conversation_id = @CId
ORDER BY h.escalated_at DESC
LIMIT 1",
            new { CId = conversationId });
    }

    /// <summary>対話メッセージ一覧を取得します。</summary>
    public async Task<IEnumerable<ConversationMessage>> GetMessagesAsync(string conversationId)
    {
        return await _db.QueryAsync<ConversationMessage>(@"
SELECT message_id AS MessageId,
       sender     AS Sender,
       content    AS Content,
       intent     AS Intent,
       timestamp  AS Timestamp
FROM ai_messages
WHERE conversation_id = @Id
ORDER BY timestamp ASC",
            new { Id = conversationId });
    }

    // ─────────────────────────────────────────────────────────
    // 内部: AI 応答生成（CLI ファースト → Claude API → テンプレート）
    // ─────────────────────────────────────────────────────────

    private async Task<string> GenerateResponseAsync(
        string userMessage, string intent, IEnumerable<(string Role, string Content)> history)
    {
        var vehicles = await GetAvailableVehicleSummaryAsync();

        // 1. CLI ファースト
        if (CliFirst)
        {
            try
            {
                var result = await CallCLIAsync(userMessage, intent, history, vehicles);
                if (!string.IsNullOrWhiteSpace(result)) return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CLI 呼び出し失敗。Claude API にフォールバックします (tool={Tool})", _cliConfig.DefaultTool);
            }
        }

        // 2. Claude API 直接呼び出し
        if (!string.IsNullOrWhiteSpace(ClaudeApiKey))
        {
            try
            {
                return await CallClaudeApiAsync(userMessage, intent, history, vehicles);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Claude API 呼び出し失敗。テンプレート応答にフォールバックします");
            }
        }

        // 3. テンプレートフォールバック
        return GetTemplateResponse(intent);
    }

    /// <summary>CLIServiceFactory 経由で AI 応答を生成します。</summary>
    private async Task<string> CallCLIAsync(
        string userMessage, string intent, IEnumerable<(string Role, string Content)> history, string vehicleSummary)
    {
        var cli = _cliFactory.GetService(_cliConfig.DefaultTool);
        var prompt = BuildCliPrompt(userMessage, intent, history, vehicleSummary);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(CliTimeoutSeconds));
        var raw = await cli.ExecuteAsync(prompt, workingDirectory: null, sessionId: null, allowedTools: [], ct: cts.Token);

        // CLI 出力から余分な説明テキストを除去（最初の実際の応答テキストのみ抽出）
        return ExtractCliResponseText(raw);
    }

    private string BuildCliPrompt(
        string userMessage, string intent, IEnumerable<(string Role, string Content)> history, string vehicleSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"あなたは{DealerName}のAIカスタマーサポートです。");
        sb.AppendLine($"営業時間: {BusinessHours}");
        sb.AppendLine($"在庫車両（抜粋）:\n{vehicleSummary}");
        sb.AppendLine();
        sb.AppendLine("ルール: 丁寧な敬語で200文字以内で回答。価格交渉・特別割引・在庫詳細は「担当者にお繋ぎします」と案内。");
        sb.AppendLine($"検出した意図: {intent}");
        sb.AppendLine();

        var historyList = history.Reverse().ToList();
        if (historyList.Count > 0)
        {
            sb.AppendLine("【直近の会話】");
            foreach (var (role, content) in historyList.TakeLast(6))
            {
                var label = role == "customer" ? "顧客" : role == "ai" ? "AI" : "担当者";
                sb.AppendLine($"{label}: {content}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"【顧客の現在のメッセージ】\n{userMessage}");
        sb.AppendLine();
        sb.AppendLine("上記に対するカスタマーサポート応答を200文字以内で返してください。説明や前置きは不要です。応答のみ出力してください。");
        return sb.ToString();
    }

    /// <summary>CLI 出力からカスタマーサポート応答テキストのみを抽出します。</summary>
    private static string ExtractCliResponseText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        // 複数行の場合は最初の非空行を返す（CLIが前置きを出力する場合を考慮）
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // JSON ブロックや「---」区切りを除いた最初の実テキストを返す
        foreach (var line in lines)
        {
            if (line.StartsWith("```") || line.StartsWith("---") || line.StartsWith("#")) continue;
            if (line.Length < 5) continue;
            return line;
        }

        return lines.FirstOrDefault() ?? raw.Trim();
    }

    private async Task<string> CallClaudeApiAsync(
        string userMessage, string intent, IEnumerable<(string Role, string Content)> history, string vehicleSummary)
    {
        var systemPrompt = BuildSystemPrompt(vehicleSummary);

        // 会話履歴を Claude のメッセージ形式に変換
        var messages = new List<object>();
        foreach (var (role, content) in history.Reverse())
        {
            messages.Add(new { role = role == "ai" ? "assistant" : (role == "agent" ? "assistant" : "user"), content });
        }
        messages.Add(new { role = "user", content = userMessage });

        var requestBody = new
        {
            model = ClaudeModel,
            max_tokens = ClaudeMaxTokens,
            system = systemPrompt,
            messages
        };

        var client = _httpClientFactory.CreateClient("ClaudeApiClient");
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", ClaudeApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await client.SendAsync(req);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? GetTemplateResponse(intent);
    }

    private string BuildSystemPrompt(string vehicleSummary)
    {
        return $@"あなたは{DealerName}のAIカスタマーサポートです。

## あなたの役割
- 車両購入・試乗・サービス（車検・修理・点検）のご相談対応
- 在庫車両のご案内
- 予約手続きのサポート
- 複雑なご要望や強い感情の場合は担当者への引き継ぎを提案

## 営業情報
- 営業時間: {BusinessHours}
- 予約: お電話またはWebフォームにてお受けします

## 現在の在庫車両（抜粋）
{vehicleSummary}

## 応答ルール
1. 丁寧な敬語で、200文字以内で簡潔に回答してください
2. 具体的な価格交渉や特別割引は「担当者にお繋ぎします」と案内してください
3. 在庫・装備の詳細確認は「担当者が詳しくご案内します」と案内してください
4. 予約希望の場合は「【予約】ボタンからご予約いただくか、お電話でもお承りします」と案内してください
5. 不明な点は「確認してご連絡いたします」と回答してください";
    }

    private async Task<string> GetAvailableVehicleSummaryAsync()
    {
        try
        {
            var vehicles = await _db.QueryAsync<(string Brand, string Model, string Grade, int Year, string FuelType, decimal Price)>(@"
SELECT brand, model, grade, year, fuel_type, price
FROM vehicles
WHERE status = 'available'
ORDER BY price ASC
LIMIT 8");

            if (!vehicles.Any()) return "（在庫情報はスタッフにお問い合わせください）";

            return string.Join("\n", vehicles.Select(v =>
                $"・{v.Brand} {v.Model} {v.Grade} ({v.Year}年式) [{v.FuelType}] ¥{v.Price:N0}"));
        }
        catch
        {
            return "（在庫情報はスタッフにお問い合わせください）";
        }
    }

    // ─────────────────────────────────────────────────────────
    // 内部: 意図・感情検出（ルールベース）
    // ─────────────────────────────────────────────────────────

    private static (string intent, bool needsHandover, string priority) DetectIntentAndEscalation(string message)
    {
        var m = message.ToLowerInvariant();

        // エスカレーション必須（即時）
        if (Contains(m, "苦情", "クレーム", "ひどい", "最悪", "怒", "詐欺", "訴える"))
            return ("complaint", true, "high");
        if (Contains(m, "担当者", "オペレーター", "人間", "スタッフ", "人に繋いで"))
            return ("human_agent", true, "medium");
        if (Contains(m, "高額", "値引き交渉", "特別価格", "大幅値引"))
            return ("high_value_deal", true, "medium");

        // 通常意図（エスカレーション不要）
        if (Contains(m, "試乗", "test drive")) return ("test_drive", false, "low");
        if (Contains(m, "予約", "申し込み", "booking")) return ("appointment_booking", false, "low");
        if (Contains(m, "キャンセル", "取り消し")) return ("appointment_cancel", false, "low");
        if (Contains(m, "価格", "値段", "いくら", "費用")) return ("price_inquiry", false, "low");
        if (Contains(m, "在庫", "車種", "どんな車")) return ("vehicle_inquiry", false, "low");
        if (Contains(m, "営業時間", "何時", "定休日")) return ("hours_inquiry", false, "low");
        if (Contains(m, "車検", "点検", "整備", "修理")) return ("service_inquiry", false, "low");
        if (Contains(m, "ローン", "分割", "金利", "支払")) return ("financing_inquiry", false, "low");
        if (Contains(m, "こんにちは", "はじめまして", "よろしく")) return ("greeting", false, "low");

        return ("general_inquiry", false, "low");
    }

    private static double EstimateSentiment(string message)
    {
        var m = message.ToLowerInvariant();
        double score = 0;
        if (Contains(m, "ありがとう", "嬉しい", "満足", "良かった", "助かった")) score += 0.5;
        if (Contains(m, "苦情", "クレーム", "ひどい", "最悪", "怒", "不満")) score -= 0.7;
        if (Contains(m, "問題", "困る", "変", "おかしい")) score -= 0.3;
        return Math.Max(-1.0, Math.Min(1.0, score));
    }

    private static bool Contains(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    // ─────────────────────────────────────────────────────────
    // 内部: テンプレート応答
    // ─────────────────────────────────────────────────────────

    private static string GetTemplateResponse(string intent) => intent switch
    {
        "greeting" => "いらっしゃいませ！どのようなご用件でしょうか？試乗・購入・サービスのご相談など、お気軽にどうぞ。😊",
        "test_drive" => "試乗は無料でご体験いただけます！希望車種とご都合の良い日時をお教えください。平日・土曜日に対応可能です。",
        "appointment_booking" => "ご予約を承ります。ご希望のサービス内容と日時をお教えください。折り返しご確認のご連絡を差し上げます。",
        "price_inquiry" => "車両価格は車種・グレードによって異なります。現在の在庫車両の詳細は担当者が丁寧にご案内いたします。",
        "vehicle_inquiry" => "只今、国産・輸入車の在庫を多数ご用意しております。ご希望の車種・予算をお教えいただければ最適な車をご提案いたします。",
        "hours_inquiry" => $"営業時間は月〜土曜日 9:00〜18:00 です。日曜・祝日は定休日となっております。お電話でのお問い合わせもお受けしております。",
        "service_inquiry" => "車検・点検・修理などのサービスについてお気軽にご相談ください。予約状況を確認の上、ご案内いたします。",
        "financing_inquiry" => "低金利のオートローンをご用意しております。頭金・ボーナス払い・残価設定型など、お客様に合ったプランをご提案いたします。",
        "appointment_cancel" => "予約のキャンセル・変更を承ります。ご予約番号とお名前をお教えください。",
        _ => "ご質問ありがとうございます。詳しい内容については担当者が丁寧にご案内いたします。他にご不明な点はございますか？"
    };

    private static List<string> GetQuickReplies(string intent) => intent switch
    {
        "greeting" => ["試乗の予約をしたい", "在庫車両を見たい", "車検・点検について", "営業時間を教えて"],
        "test_drive" => ["土曜日の午前を希望", "来週の平日を希望", "別の車種の試乗も聞く", "詳しく聞く"],
        "vehicle_inquiry" => ["試乗の予約をする", "価格を教えて", "担当者に相談する"],
        "price_inquiry" => ["見積もりを依頼する", "ローンについて聞く", "担当者に相談する"],
        "service_inquiry" => ["予約する", "費用を確認する", "担当者に相談する"],
        _ => ["他にも質問する", "担当者に相談する", "予約する"]
    };

    // ─────────────────────────────────────────────────────────
    // 内部: DB 操作
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
            new
            {
                MId = messageId,
                CId = conversationId,
                Sender = sender,
                Content = content,
                Intent = intent,
                Conf = confidence,
                Sent = sentiment,
                Now = now
            });
    }

    private async Task<IEnumerable<(string Role, string Content)>> GetRecentMessagesAsync(string conversationId, int limit)
    {
        var rows = await _db.QueryAsync(@"
SELECT sender, content
FROM ai_messages
WHERE conversation_id = @Id
ORDER BY timestamp DESC
LIMIT @Limit",
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
