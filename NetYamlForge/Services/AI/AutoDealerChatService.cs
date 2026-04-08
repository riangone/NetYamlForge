// ファイル概要：auto-dealer-demo 専用 AI チャットサービス（BaseChatService 統合版）
// 共通ロジックは BaseChatService に集約。このクラスは差分のみを実装します。

using System.Data;
using System.Text;
using Dapper;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI.Providers;

namespace NetYamlForge.Services.AI;

/// <summary>
/// auto-dealer-demo AI チャットサービス。
/// 顧客向けチャット・スタッフ向けチャット・オペレーターハンドオーバーを担当します。
/// </summary>
public class AutoDealerChatService : BaseChatService
{
    private readonly string _dealerName;
    private readonly IIntentClassifier? _intentClassifier;
    private readonly ISlotFillingManager? _slotFilling;

    public AutoDealerChatService(
        IDbConnection db,
        CLIServiceFactory cliFactory,
        ILlmProvider llmProvider,
        SkillLoader skillLoader,
        ProjectScope projectScope,
        ILogger<AutoDealerChatService> logger,
        QueryParserService queryParser,
        QueryExecutionService queryExecutor,
        QueryResultFormatter queryFormatter,
        TaskQueueService taskQueue,
        ProgressTracker tracker,
        ChatHistoryService chatHistory,
        IConfiguration config,
        IIntentClassifier? intentClassifier = null,
        ISlotFillingManager? slotFilling = null)
        : base(db, cliFactory, llmProvider, skillLoader, projectScope, logger, queryParser, queryExecutor, queryFormatter,
               taskQueue, tracker, chatHistory, config, "auto-dealer-demo")
    {
        _dealerName = config["AiWindow:DealerName"] ?? "AI 窓口ディーラー";
        _intentClassifier = intentClassifier;
        _slotFilling = slotFilling;
    }

    // ─────────────────────────────────────────────────────────
    // BaseChatService abstract 実装
    // ─────────────────────────────────────────────────────────

    protected override string BuildSystemPrompt(string context, string? dbContextMarkdown = null)
    {
        bool isStaff = context == "staff";
        string systemPrompt;

        if (isStaff)
        {
            var staffPrompt = LoadPromptFromMd("auto-dealer", "_system-prompt-staff.md");
            var toolsDefinition = LoadPromptFromMd("auto-dealer", "_tools-definition.md");

            systemPrompt = staffPrompt;
            systemPrompt += Environment.NewLine + Environment.NewLine;
            systemPrompt += "# 🔧 ツール定義" + Environment.NewLine;
            systemPrompt += toolsDefinition;
        }
        else
        {
            // ✅ 修复：客户模式下，强化角色定义和用户身份
            var customerPrompt = LoadPromptFromMd("auto-dealer", "_system-prompt-customer.md");
            var toolsDefinition = LoadPromptFromMd("auto-dealer", "_tools-definition.md");

            systemPrompt = customerPrompt;
            systemPrompt += Environment.NewLine + Environment.NewLine;
            
            // 添加明确的角色定义和用户身份信息
            systemPrompt += "---" + Environment.NewLine + Environment.NewLine;
            systemPrompt += "# 🎯 現在のユーザー情報" + Environment.NewLine;
            systemPrompt += $"- ログインユーザー: customer1（顧客）" + Environment.NewLine;
            systemPrompt += $"- 権限レベル: 顧客（読み取り専用）" + Environment.NewLine;
            systemPrompt += $"- アクセス可能データ: 車両在庫・サービス予約（自分の分）" + Environment.NewLine;
            systemPrompt += $"- 応答スタイル: 丁寧な敬語で、具体的な情報をご案内" + Environment.NewLine;
            systemPrompt += Environment.NewLine;
            systemPrompt += "# 🔧 ツール定義" + Environment.NewLine;
            systemPrompt += toolsDefinition;
        }

        systemPrompt = systemPrompt
            .Replace("{current_datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{business_hours}", _businessHours)
            .Replace("{dealer_name}", _dealerName);

        if (!string.IsNullOrWhiteSpace(dbContextMarkdown))
        {
            systemPrompt += Environment.NewLine + "## DB 検索結果（参考）" + Environment.NewLine + dbContextMarkdown;
        }

        return systemPrompt;
    }

    protected override string GetWelcomeMessage(string? context) => context == "staff"
        ? $"こんにちは！{_dealerName}の AI 業務アシスタントです。🤝\nリード管理・予約確認・在庫照会など、業務に関することは何でもご相談ください！"
        : $"こんにちは！{_dealerName}の AI カスタマーサポートです。🚗\n試乗・ご購入・サービスのご相談は何でもどうぞ！";

    protected override List<string> GetQuickReplies(string context, string intent) => context == "staff"
        ? GetStaffQuickReplies(intent)
        : GetCustomerQuickReplies(intent);

    // ─────────────────────────────────────────────────────────
    // セッション管理
    // ─────────────────────────────────────────────────────────

    public async Task<ChatSessionResult> StartSessionAsync(
        string channel = "web", string? guestSessionId = null, string? customerId = null)
    {
        _logger.LogInformation("StartSessionAsync 開始。channel: {Channel}, customerId: {CustomerId}",
            channel, customerId ?? "(null)");

        var conversationId = $"CONV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await _db.ExecuteAsync(@"
INSERT INTO ai_conversations
  (conversation_id, channel, status, started_at, created_at, updated_at, guest_session_id, customer_id)
VALUES
  (@ConversationId, @Channel, 'active', @Now, @Now, @Now, @GuestSessionId, @CustomerId)",
            new
            {
                ConversationId = conversationId, Channel = channel, Now = now,
                GuestSessionId = (object?)guestSessionId ?? DBNull.Value,
                CustomerId = (object?)customerId ?? DBNull.Value
            });

        return new ChatSessionResult
        {
            ConversationId = conversationId,
            WelcomeMessage = GetWelcomeMessage(channel == "staff" ? "staff" : "customer")
        };
    }

    // ─────────────────────────────────────────────────────────
    // メッセージ処理（顧客）
    // ─────────────────────────────────────────────────────────

    public async Task<ChatMessageResult> SendMessageAsync(string conversationId, string customerMessage)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var (escalationIntent, needsHandover, priority) = DetectEscalation(customerMessage);
        var sentimentScore = EstimateSentiment(customerMessage);

        var history = await GetRecentMessagesAsync(conversationId, 10);
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "customer", customerMessage, now);

        // ✅ 从会话中获取真正的客户 ID
        var customerId = await _db.QueryFirstOrDefaultAsync<string>(
            "SELECT customer_id FROM ai_conversations WHERE conversation_id = @Id",
            new { Id = conversationId });

        if (needsHandover || sentimentScore < -0.5)
            return await HandleEscalationAsync(conversationId, customerMessage, escalationIntent, priority, sentimentScore, now, sw);

        // ✅ 試乗予約インテントの検出とSlot-fillingフロー
        var resolvedIntent = "general";
        var responseText = "";
        var navUrl = "";
        var navLabel = "";

        // 1. インテント分類を試みる
        if (_intentClassifier != null)
        {
            var intentResult = await _intentClassifier.ClassifyAsync(customerMessage, projectId: _projectName);
            resolvedIntent = intentResult.Intent;

            // 2. 試乗予約インテントの場合、Slot-fillingフローを実行
            if (resolvedIntent == "test_drive_booking" && _slotFilling != null)
            {
                var slotResult = await ProcessTestDriveSlotFillingAsync(conversationId, customerMessage);
                responseText = slotResult.ResponseText;
                navUrl = slotResult.NavUrl;
                navLabel = slotResult.NavLabel;

                var aiResponseTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, aiResponseTime, resolvedIntent, 0.9, sentimentScore);
                await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET last_intent = @Intent, last_confidence = 0.9, sentiment_score = @Sentiment, updated_at = @Now
WHERE conversation_id = @Id",
                    new { Intent = resolvedIntent, Sentiment = sentimentScore, Now = now, Id = conversationId });

                await _chatHistory.SaveMessageAsync(customerId ?? _projectName, customerMessage, "user",
                    provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);
                await _chatHistory.SaveMessageAsync(customerId ?? _projectName, responseText, "assistant",
                    provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);

                sw.Stop();
                return new ChatMessageResult
                {
                    ResponseText = responseText,
                    Intent = resolvedIntent,
                    SuggestHandover = false,
                    QuickReplies = GetCustomerQuickReplies(resolvedIntent),
                    ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
                    AiProvider = _defaultProvider,
                    MessageTimestamp = aiResponseTime
                };
            }
        }

        // 3. インテント分類が不要/失敗した場合は通常のLLMフロー
        if (string.IsNullOrEmpty(responseText))
        {
            var (aiResponseText, aiIntent, dataRows, aiNavUrl, aiNavLabel) =
                await GenerateAiResponseAsync(customerMessage, "customer", history);
            responseText = aiResponseText;
            resolvedIntent = aiIntent;
            navUrl = aiNavUrl ?? "";
            navLabel = aiNavLabel ?? "";
        }

        // ✅ AI 回复的消息时间戳
        var aiResponseTime2 = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, aiResponseTime2, resolvedIntent, 0.9, sentimentScore);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET last_intent = @Intent, last_confidence = 0.9, sentiment_score = @Sentiment, updated_at = @Now
WHERE conversation_id = @Id",
            new { Intent = resolvedIntent, Sentiment = sentimentScore, Now = now, Id = conversationId });

        await _chatHistory.SaveMessageAsync(customerId ?? _projectName, customerMessage, "user",
            provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);
        await _chatHistory.SaveMessageAsync(customerId ?? _projectName, responseText, "assistant",
            provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = responseText,
            Intent = resolvedIntent,
            SuggestHandover = false,
            QuickReplies = GetCustomerQuickReplies(resolvedIntent),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
            AiProvider = _defaultProvider,  // ✅ AI 提供商标识
            MessageTimestamp = aiResponseTime2  // ✅ 详细时间戳
        };
    }

    // ─────────────────────────────────────────────────────────
    // 試乗予約 Slot-filling フロー
    // ─────────────────────────────────────────────────────────

    private async Task<(string ResponseText, string? NavUrl, string? NavLabel)> ProcessTestDriveSlotFillingAsync(
        string conversationId, string customerMessage)
    {
        try
        {
            var scenario = SlotFillingManager.DetectScenarioFromMessage(customerMessage, "test_drive_booking");
            if (scenario != "test_drive" || _slotFilling == null)
            {
                return ("試乗予約をご希望ですね。ご希望の車種・日時をお知らせください。", null, null);
            }

            // ✅ 修正 1: 最初にセッションを取得（または作成）
            var session = await _slotFilling.GetSessionAsync(conversationId, scenario, _projectName);
            
            // ✅ 修正 2: 次に、メッセージからスロット値を抽出して更新
            await ExtractSlotValuesFromMessageAsync(conversationId, customerMessage, scenario);
            
            // ✅ 修正 3: 更新後のセッションを再取得
            session = await _slotFilling.GetSessionAsync(conversationId, scenario, _projectName);

            // ✅ デバッグログ：現在のslot状態を記録
            var collectedSlots = session.GetCollectedValues();
            _logger.LogInformation("試乗予約 Slot-filling: Conv={ConvId}, 収集済みSlots={Slots}, 完了={IsComplete}",
                conversationId, 
                string.Join(", ", collectedSlots.Select(kv => $"{kv.Key}={kv.Value}")),
                session.IsComplete);

            if (session.IsComplete)
            {
                var slots = session.GetCollectedValues();
                return await CompleteTestDriveBookingAsync(conversationId, slots);
            }

            var nextSlot = await _slotFilling.GetNextRequiredSlotAsync(conversationId, scenario, _projectName);
            if (nextSlot != null)
            {
                _logger.LogInformation("試乗予約: 次の質問スロット={Slot}, プロンプト={Prompt}", 
                    nextSlot.SlotName, nextSlot.Prompt);
                return ($"{nextSlot.Prompt}", null, null);
            }

            return ("試乗予約をご希望ですね！ 🚗\n\n試乗したい車種と、ご希望の日時をお知らせください。", null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "試乗予約Slot-fillingエラー");
            return ("試乗予約のご連絡ありがとうございます。車種・ご希望日時・お名前・ご連絡先をお知らせください。", null, null);
        }
    }

    private async Task ExtractSlotValuesFromMessageAsync(string conversationId, string message, string scenario)
    {
        if (_slotFilling == null) return;

        var lowerMessage = message.ToLowerInvariant();

        var datePatterns = new Dictionary<string, string>
        {
            { "明日", "tomorrow" },
            { "明後日", "day_after_tomorrow" },
            { "今日", "today" },
            { "来週", "next_week" },
            { "今週", "this_week" }
        };

        foreach (var (pattern, value) in datePatterns)
        {
            if (lowerMessage.Contains(pattern))
            {
                await _slotFilling.UpdateSlotAsync(conversationId, "preferred_date", value, _projectName);
                break;
            }
        }

        var dateMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{4})[-/](\d{1,2})[-/](\d{1,2})");
        if (dateMatch.Success)
        {
            await _slotFilling.UpdateSlotAsync(conversationId, "preferred_date", dateMatch.Value, _projectName);
        }

        var timePatterns = new Dictionary<string, string>
        {
            { "午前", "morning" },
            { "午後", "afternoon" },
            { "朝", "morning" },
            { "昼", "afternoon" },
            { "夕方", "evening" },
            { "夜", "evening" },
            { "10時", "10:00" },
            { "14時", "14:00" },
            { "2時", "14:00" },
            { "15時", "15:00" },
            { "3時", "15:00" }
        };

        foreach (var (pattern, value) in timePatterns)
        {
            if (lowerMessage.Contains(pattern))
            {
                await _slotFilling.UpdateSlotAsync(conversationId, "preferred_time", value, _projectName);
                break;
            }
        }

        var phoneMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{2,4}-\d{1,4}-\d{4})");
        if (phoneMatch.Success)
        {
            await _slotFilling.UpdateSlotAsync(conversationId, "customer_phone", phoneMatch.Value, _projectName);
        }

        var knownVehicles = new Dictionary<string, string>
        {
            { "プリウス", "プリウス PHV" },
            { "ランドクルーザー", "ランドクルーザー 300" },
            { "アルファード", "アルファード" },
            { "camry", "カムリ" },
            { "カローラ", "カローラ" },
            { "ヤリス", "ヤリス" },
            { "rav4", "RAV4" },
            { "ハイラックス", "ハイラックス" },
            { "クラウン", "クラウン" },
            { "スープラ", "スープラ" },
            { "gtr", "GT-R" },
            { "フィット", "フィット" },
            { "アクセラ", "アクセラ" },
            { "cx", "CXシリーズ" },
            { "インプレッサ", "インプレッサ" },
            { "レヴォーグ", "レヴォーグ" },
            // ✅ 添加制造商名称作为回退选项
            { "マツダ", "マツダ車" },
            { "トヨタ", "トヨタ車" },
            { "ホンダ", "ホンダ車" },
            { "日産", "日産車" },
            { "bmw", "BMW車" },
            { "メルセデス", "メルセデス・ベンツ" },
            { "ベンツ", "メルセデス・ベンツ" }
        };

        foreach (var (keyword, vehicleName) in knownVehicles)
        {
            if (lowerMessage.Contains(keyword))
            {
                await _slotFilling.UpdateSlotAsync(conversationId, "vehicle_model", vehicleName, _projectName);
                break;
            }
        }

        var namePatterns = new System.Text.RegularExpressions.Regex(@"(.+?)(?:です|と申します|でございます)");
        var nameMatch = namePatterns.Match(message);
        if (nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value.Trim()))
        {
            var candidateName = nameMatch.Groups[1].Value.Trim();
            if (candidateName.Length >= 2 && candidateName.Length <= 20)
            {
                await _slotFilling.UpdateSlotAsync(conversationId, "customer_name", candidateName, _projectName);
            }
        }
    }

    private async Task<(string ResponseText, string? NavUrl, string? NavLabel)> CompleteTestDriveBookingAsync(
        string conversationId, Dictionary<string, string> slots)
    {
        try
        {
            var vehicleName = slots.GetValueOrDefault("vehicle_model", "未指定");
            var preferredDate = slots.GetValueOrDefault("preferred_date", "未指定");
            var preferredTime = slots.GetValueOrDefault("preferred_time", "未指定");
            var customerName = slots.GetValueOrDefault("customer_name", "未入力");
            var customerPhone = slots.GetValueOrDefault("customer_phone", "未入力");

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var appointmentId = $"APT-{Guid.NewGuid():N}"[..16];

            await _db.ExecuteAsync(@"
INSERT INTO service_appointments
  (appointment_id, appointment_type, preferred_date, preferred_time, customer_name, phone, vehicle_id, status, created_at, updated_at)
VALUES
  (@AppointmentId, 'test_drive', @PreferredDate, @PreferredTime, @CustomerName, @Phone, NULL, 'pending', @Now, @Now)",
                new
                {
                    AppointmentId = appointmentId,
                    PreferredDate = preferredDate,
                    PreferredTime = preferredTime,
                    CustomerName = customerName,
                    Phone = customerPhone,
                    Now = now
                });

            var responseText = $"""
                試乗予約を承りました！ ✅

                **ご予約内容:**
                - 車種: {vehicleName}
                - 希望日: {preferredDate}
                - 時間: {preferredTime}
                - お名前: {customerName}
                - 電話番号: {customerPhone}

                予約番号: `{appointmentId}`

                担当者より折り返しご連絡させていただきます。
                当日は運転免許証をお持ちください。

                [予約詳細を見る](/{_projectName}/DynamicEntity/DetailPage?entity=service_appointments&id={appointmentId})
                """;

            return (responseText, $"/{_projectName}/DynamicEntity/DetailPage?entity=service_appointments&id={appointmentId}", "予約詳細を見る");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "試乗予約確定エラー");
            return ($"予約処理中にエラーが発生しました。お手数ですがお電話にてご連絡ください。\n📞 03-XXXX-XXXX", null, null);
        }
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

        var (responseText, entityLabel, dataRows, navUrl, navLabel) =
            await GenerateAiResponseAsync(staffMessage, "staff", history);

        // ✅ AI 回复的消息时间戳
        var aiResponseTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        
        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, aiResponseTime, entityLabel, 0.9, 0);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations SET last_intent = @Intent, updated_at = @Now WHERE conversation_id = @Id",
            new { Intent = entityLabel, Now = now, Id = conversationId });

        await _chatHistory.SaveMessageAsync(_projectName, staffMessage, "user",
            provider: _defaultProvider, chatContext: "dealer-staff", projectName: _projectName);
        await _chatHistory.SaveMessageAsync(_projectName, responseText, "assistant",
            provider: _defaultProvider, chatContext: "dealer-staff", projectName: _projectName);

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = responseText,
            Intent = entityLabel,
            SuggestHandover = false,
            QuickReplies = GetQuickReplies("staff", entityLabel),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
            DataRows = dataRows,
            NavigationUrl = navUrl,
            NavigationLabel = navLabel,
            AiProvider = _defaultProvider,  // ✅ AI 提供商标识
            MessageTimestamp = aiResponseTime  // ✅ 详细时间戳
        };
    }

    // ─────────────────────────────────────────────────────────
    // エスカレーション処理（dealer 固有）
    // ─────────────────────────────────────────────────────────

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

        // ✅ 升级消息也使用时间戳
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
    // オペレーター機能
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

    public async Task<IEnumerable<ConversationMessage>> GetMessagesAsync(string conversationId)
    {
        return await _db.QueryAsync<ConversationMessage>(@"
SELECT message_id AS MessageId, sender AS Sender, content AS Content, timestamp AS Timestamp, intent AS Intent
FROM ai_messages
WHERE conversation_id = @Id
ORDER BY timestamp ASC",
            new { Id = conversationId });
    }

    public async Task<IEnumerable<ConversationSummary>> GetUserRecentConversationsAsync(string userId, int limit = 10)
    {
        return await _db.QueryAsync<ConversationSummary>(@"
SELECT c.conversation_id AS ConversationId, c.channel AS Channel, c.status AS Status,
       c.started_at AS StartedAt, c.updated_at AS UpdatedAt
FROM ai_conversations c
WHERE c.customer_id = @UserId OR c.guest_session_id = @UserId
ORDER BY c.updated_at DESC
LIMIT @Limit",
            new { UserId = userId, Limit = limit });
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

    // ─────────────────────────────────────────────────────────
    // プロンプトファイル読み込み（共通ヘルパー）
    // ─────────────────────────────────────────────────────────

    private string LoadPromptFromMd(string skillDir, string fileName)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "skills", skillDir, fileName);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("プロンプトファイル {File} が見つかりません", filePath);
            return BuildFallbackPrompt(fileName);
        }

        var content = File.ReadAllText(filePath).Trim();
        if (content.StartsWith("---"))
        {
            var end = content.IndexOf("---", 3);
            if (end >= 0) content = content[(end + 3)..].Trim();
        }
        return content;
    }

    private string BuildFallbackPrompt(bool isStaff)
    {
        return isStaff
            ? $"あなたは{_dealerName}の社員向け AI 業務アシスタントです。リード管理・予約確認・在庫照会・顧客情報の照会など業務全般を支援します。\n現在の日時：{DateTime.Now:yyyy-MM-dd HH:mm}\n営業時間：{_businessHours}"
            : $"あなたは{_dealerName}の AI カスタマーサポートです。車両購入・試乗・サービスのご相談に対応します。丁寧な敬語で回答してください。\n現在の日時：{DateTime.Now:yyyy-MM-dd HH:mm}\n営業時間：{_businessHours}";
    }

    private string BuildFallbackPrompt(string fileName)
        => BuildFallbackPrompt(fileName.Contains("staff"));

    // ─────────────────────────────────────────────────────────
    // クイックリプライ（dealer 固有）
    // ─────────────────────────────────────────────────────────

    private List<string> GetCustomerQuickReplies(string intent) => intent switch
    {
        "vehicle_inquiry" => new List<string> { "在庫を確認", "試乗を予約", "価格を聞く" },
        "appointment" => new List<string> { "予約を変更", "予約をキャンセル", "新しい予約" },
        _ => new List<string> { "車両を探す", "試乗を予約する", "お問い合わせ" }
    };

    private List<string> GetStaffQuickReplies(string intent) => intent switch
    {
        "sales_leads" => new List<string> { "新規リード", "フォローアップ必要", "成約済み" },
        "customers" => new List<string> { "VIP 顧客", "未連絡顧客", "購入履歴" },
        _ => new List<string> { "顧客を検索", "リードを確認", "予約を確認" }
    };
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
    
    /// <summary>
    /// AI プロバイダー名（例：qwen, claude, gemini）
    /// </summary>
    public string? AiProvider { get; init; }
    
    /// <summary>
    /// メッセージ送信時刻（yyyy-MM-dd HH:mm:ss 形式）
    /// </summary>
    public string? MessageTimestamp { get; init; }
}

public record ChatPollMessage
{
    public string MessageId { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Content { get; init; } = "";
    public string Timestamp { get; init; } = "";
}

public class OperatorHandoverDetail
{
    public string HandoverId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
    public string EscalatedAt { get; set; } = "";
    public string? AssignedAt { get; set; }
    public string? AssignedToUserId { get; set; }
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerTier { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
}

public record ConversationMessage
{
    public string MessageId { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Content { get; init; } = "";
    public string? Intent { get; init; }
    public string Timestamp { get; init; } = "";
}

public record ConversationSummary
{
    public string ConversationId { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Status { get; init; } = "";
    public string StartedAt { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
}
