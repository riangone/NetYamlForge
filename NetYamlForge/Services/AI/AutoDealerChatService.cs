// ファイル概要：auto-dealer-demo 専用 AI チャットサービス（BaseChatService 統合版）
// 共通ロジックは BaseChatService に集約。このクラスは差分のみを実装します。

using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public async Task CloseConversationAsync(string conversationId)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET status = 'completed', ended_at = @Now, updated_at = @Now
WHERE conversation_id = @ConversationId",
            new { ConversationId = conversationId, Now = now });

        if (_slotFilling != null)
            await _slotFilling.ResetAsync(conversationId, _projectName);

        var messages = (await GetRecentMessagesAsync(conversationId, 20)).ToList();
        if (messages.Count > 2)
        {
            var lastIntent = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT last_intent FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });

            var summary = new
            {
                closed_at = now,
                message_count = messages.Count,
                last_intent = lastIntent
            };

            var contextJson = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT context_data FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });

            JsonObject context;
            if (string.IsNullOrWhiteSpace(contextJson))
            {
                context = new JsonObject();
            }
            else
            {
                try
                {
                    context = JsonNode.Parse(contextJson) as JsonObject ?? new JsonObject();
                }
                catch (JsonException)
                {
                    context = new JsonObject();
                }
            }

            context.Remove("slot_sessions");
            context["summary"] = JsonSerializer.SerializeToNode(summary);

            await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET context_data = @ContextData, updated_at = @Now
WHERE conversation_id = @ConversationId",
                new
                {
                    ContextData = context.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
                    Now = now,
                    ConversationId = conversationId
                });
        }
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

        // 0. アクティブなSlot-fillingセッションがあれば、インテント分類前に優先して継続
        if (_slotFilling != null)
        {
            var activeScenario = await _slotFilling.GetActiveScenarioAsync(conversationId);
            if (activeScenario != null)
            {
                // 今のメッセージからスロット値を抽出して更新
                await ExtractSlotValuesFromMessageAsync(conversationId, customerMessage, activeScenario);
                var activeSession = await _slotFilling.GetSessionAsync(conversationId, activeScenario, _projectName);

                _logger.LogInformation("アクティブSlot-fillingセッション継続: Conv={ConvId}, Scenario={Scenario}, Complete={Done}",
                    conversationId, activeScenario, activeSession.IsComplete);

                if (activeSession.IsComplete)
                {
                    var slots = activeSession.GetCollectedValues();
                    var (completionText, completionNavUrl, completionNavLabel) =
                        await CompleteScenarioAsync(conversationId, activeScenario, slots);
                    responseText = completionText;
                    navUrl = completionNavUrl ?? "";
                    navLabel = completionNavLabel ?? "";
                    resolvedIntent = MapScenarioToIntent(activeScenario);

                    var completionTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, completionTime, resolvedIntent, 0.9, sentimentScore);
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
                        MessageTimestamp = completionTime,
                        NavigationUrl = navUrl,
                        NavigationLabel = navLabel
                    };
                }

                // スロット値が更新されたか確認（更新があれば次のスロットを聞く、なければLLMで回答してから継続）
                var collectedAfter = activeSession.GetCollectedValues();
                var nextSlot = await _slotFilling.GetNextRequiredSlotAsync(conversationId, activeScenario, _projectName);
                if (collectedAfter.Count > 0 && nextSlot != null)
                {
                    // 何らかのスロット値が収集済み → 次の質問を返す
                    resolvedIntent = MapScenarioToIntent(activeScenario);
                    responseText = nextSlot.Prompt;

                    var slotTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, slotTime, resolvedIntent, 0.9, sentimentScore);
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
                        MessageTimestamp = slotTime
                    };
                }
                // スロット値が取れなかった（「どんな車がある？」等の脱線質問）→ LLMで回答し、後続で継続プロンプトを付加
            }
        }

        // 1. インテント分類を試みる
        if (_intentClassifier != null)
        {
            var intentResult = await _intentClassifier.ClassifyAsync(customerMessage, projectId: _projectName);
            resolvedIntent = intentResult.Intent;

            // 2. Slot-filling 対象インテントの場合、Slot-fillingフローを実行
            var slotScenario = MapIntentToScenario(resolvedIntent);
            if (slotScenario != null && _slotFilling != null)
            {
                var slotResult = await ProcessSlotFillingAsync(conversationId, customerMessage, slotScenario);
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

            // アクティブなSlot-fillingセッションがある場合、LLM回答の後に次の質問を付加
            if (_slotFilling != null)
            {
                var stillActive = await _slotFilling.GetActiveScenarioAsync(conversationId);
                if (stillActive != null)
                {
                    var nextSlot = await _slotFilling.GetNextRequiredSlotAsync(conversationId, stillActive, _projectName);
                    if (nextSlot != null)
                    {
                        responseText += $"\n\n---\n{BuildScenarioContinuation(stillActive)}{nextSlot.Prompt}";
                        resolvedIntent = MapScenarioToIntent(stillActive);
                    }
                }
            }
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

    private static string? MapIntentToScenario(string intent) => intent switch
    {
        "test_drive_booking" => "test_drive",
        "estimate_request" => "estimate",
        "service_booking" => "appointment_service",
        "trade_inquiry" => "trade_in",
        _ => null
    };

    private static string MapScenarioToIntent(string scenario) => scenario switch
    {
        "test_drive" => "test_drive_booking",
        "estimate" => "estimate_request",
        "appointment_service" => "service_booking",
        "trade_in" => "trade_inquiry",
        _ => scenario
    };

    private static string BuildScenarioContinuation(string scenario) => scenario switch
    {
        "test_drive" => "引き続き試乗予約を承ります。",
        "estimate" => "引き続き見積もり依頼を承ります。",
        "appointment_service" => "引き続きサービス予約を承ります。",
        "trade_in" => "引き続き下取り査定のご依頼を承ります。",
        _ => "引き続きご予約を承ります。"
    };

    private async Task<(string ResponseText, string? NavUrl, string? NavLabel)> ProcessSlotFillingAsync(
        string conversationId, string customerMessage, string scenario)
    {
        try
        {
            if (_slotFilling == null)
            {
                return ("ご希望内容を承りました。必要な情報を順にお伺いします。", null, null);
            }

            var mappedScenario = SlotFillingManager.DetectScenarioFromMessage(customerMessage, MapScenarioToIntent(scenario));
            if (!string.IsNullOrEmpty(mappedScenario))
                scenario = mappedScenario!;

            // ✅ 修正 1: 最初にセッションを取得（または作成）
            var session = await _slotFilling.GetSessionAsync(conversationId, scenario, _projectName);
            
            // ✅ 修正 2: 次に、メッセージからスロット値を抽出して更新
            await ExtractSlotValuesFromMessageAsync(conversationId, customerMessage, scenario);
            
            // ✅ 修正 3: 更新後のセッションを再取得
            session = await _slotFilling.GetSessionAsync(conversationId, scenario, _projectName);

            // ✅ デバッグログ：現在のslot状態を記録
            var collectedSlots = session.GetCollectedValues();
            _logger.LogInformation("Slot-filling: Conv={ConvId}, Scenario={Scenario}, 収集済みSlots={Slots}, 完了={IsComplete}",
                conversationId, 
                scenario,
                string.Join(", ", collectedSlots.Select(kv => $"{kv.Key}={kv.Value}")),
                session.IsComplete);

            if (session.IsComplete)
            {
                var slots = session.GetCollectedValues();
                return await CompleteScenarioAsync(conversationId, scenario, slots);
            }

            var nextSlot = await _slotFilling.GetNextRequiredSlotAsync(conversationId, scenario, _projectName);
            if (nextSlot != null)
            {
                _logger.LogInformation("Slot-filling: 次の質問スロット={Slot}, プロンプト={Prompt}", 
                    nextSlot.SlotName, nextSlot.Prompt);
                return ($"{nextSlot.Prompt}", null, null);
            }

            return ("ご希望内容を承りました。必要な情報を順にお伺いします。", null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slot-fillingエラー");
            return ("ご連絡ありがとうございます。必要な情報（車種・ご希望日時・お名前・ご連絡先など）をお知らせください。", null, null);
        }
    }

    /// <summary>
    /// AI を使用してメッセージからスロット値を抽出
    /// 正規表現や辞書の代わりに LLM で自然言語理解を行う
    /// </summary>
    private async Task ExtractSlotValuesFromMessageAsync(string conversationId, string message, string scenario)
    {
        if (_slotFilling == null) return;

        try
        {
            // シナリオに応じて抽出するスロットを定義
            var slotsToExtract = scenario switch
            {
                "test_drive" => "vehicle_model, preferred_date, preferred_time, customer_name, customer_phone",
                "estimate" => "vehicle_model, grade, budget, customer_name, customer_phone",
                "appointment_service" => "service_type, preferred_date, preferred_time, customer_name, customer_phone",
                "trade_in" => "vehicle_model, vehicle_year, mileage, customer_name, customer_phone",
                _ => "customer_name, customer_phone"
            };

            // AI に抽出を依頼するプロンプト
            var extractionPrompt = $@"あなたは情報抽出アシスタントです。以下のメッセージから、指定されたスロットの値を抽出してください。

メッセージ: {message}

抽出するスロット: {slotsToExtract}

以下の JSON 形式のみで返してください。値がないスロットは null にしてください。
{{
  ""vehicle_model"": ""車種名"",
  ""preferred_date"": ""日付"",
  ""preferred_time"": ""時間"",
  ""customer_name"": ""名前"",
  ""customer_phone"": ""電話番号"",
  ""service_type"": ""サービス種類"",
  ""grade"": ""グレード"",
  ""budget"": ""予算"",
  ""vehicle_year"": ""車両年"",
  ""mileage"": ""走行距離""
}}

ルール:
- 日本語の日付表現（明日、来週、等）はそのまま抽出
- 時間表現（午前10時、午後2時、等）もそのまま抽出  
- 名前は敬語表現（です、と申します、等）を除いた部分のみを抽出
- 電話番号は数字とハイフンをそのまま抽出
- 見つからない値は null にしてください
- JSON のみ出力し、他の説明は不要です";

            var response = await _llmProvider.CompleteAsync(extractionPrompt, System.Threading.CancellationToken.None);
            
            // JSON をパースしてスロットを更新
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var extracted = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr);
                
                if (extracted != null)
                {
                    var updated = false;
                    foreach (var kvp in extracted)
                    {
                        if (kvp.Value.ValueKind == JsonValueKind.String)
                        {
                            var value = kvp.Value.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                await _slotFilling.UpdateSlotAsync(conversationId, kvp.Key, value, _projectName);
                                updated = true;
                                _logger.LogInformation("AIスロット抽出成功: Scenario={Scenario}, Slot={Slot}, Value={Value}",
                                    scenario, kvp.Key, value);
                            }
                        }
                    }
                    
                    if (updated)
                    {
                        _logger.LogInformation("スロット更新完了: Scenario={Scenario}", scenario);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AIスロット抽出に失敗しました");
            // AI に失敗した場合は何もしない（既存のセッションを保持）
        }
    }


    private async Task<(string ResponseText, string? NavUrl, string? NavLabel)> CompleteScenarioAsync(
        string conversationId, string scenario, Dictionary<string, string> slots) => scenario switch
    {
        "test_drive" => await CompleteTestDriveBookingAsync(conversationId, slots),
        "estimate" => await CompleteEstimateRequestAsync(conversationId, slots),
        "appointment_service" => await CompleteServiceBookingAsync(conversationId, slots),
        "trade_in" => await CompleteTradeInRequestAsync(conversationId, slots),
        _ => ("ご依頼を承りました。担当者よりご連絡いたします。", null, null)
    };

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

            var customerId = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT customer_id FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });

            var dateTimeStr = $"{preferredDate} {preferredTime}";

            await _db.ExecuteAsync(@"
INSERT INTO service_appointments
  (appointment_id, customer_id, appointment_type, preferred_date, customer_request, status, created_at, updated_at)
VALUES
  (@AppointmentId, @CustomerId, 'test_drive', @PreferredDate, @CustomerRequest, 'pending', @Now, @Now)",
                new
                {
                    AppointmentId = appointmentId,
                    CustomerId = customerId ?? "CUST-UNKNOWN",
                    PreferredDate = dateTimeStr,
                    CustomerRequest = $"お名前: {customerName} / 電話: {customerPhone} / 希望車種: {vehicleName}",
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

    private async Task<(string ResponseText, string? NavUrl, string? NavLabel)> CompleteEstimateRequestAsync(
        string conversationId, Dictionary<string, string> slots)
    {
        try
        {
            var vehicleName = slots.GetValueOrDefault("vehicle_model", "未指定");
            var grade = slots.GetValueOrDefault("grade", "未指定");
            var customerName = slots.GetValueOrDefault("customer_name", "未入力");
            var customerPhone = slots.GetValueOrDefault("customer_phone", "未入力");

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var leadId = $"LEAD-{Guid.NewGuid():N}"[..16];

            var customerId = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT customer_id FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });

            await _db.ExecuteAsync(@"
INSERT INTO sales_leads
  (lead_id, customer_id, vehicle_interest, status, source_conversation_id, lead_source, created_at, updated_at)
VALUES
  (@LeadId, @CustomerId, @VehicleInterest, 'new', @ConversationId, 'ai_conversation', @Now, @Now)",
                new
                {
                    LeadId = leadId,
                    CustomerId = customerId ?? "CUST-UNKNOWN",
                    VehicleInterest = vehicleName,
                    ConversationId = conversationId,
                    Now = now
                });

            var responseText = $"""
                見積もりリクエストを承りました。✅

                **ご依頼内容:**
                - 車種: {vehicleName}
                - グレード: {grade}
                - お名前: {customerName}
                - 電話番号: {customerPhone}

                担当者より {customerName} 様にご連絡いたします。
                """;

            return (responseText, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "見積もり依頼確定エラー");
            return ("見積もり依頼の処理中にエラーが発生しました。お手数ですがお電話にてご連絡ください。", null, null);
        }
    }

    private async Task<(string ResponseText, string? NavUrl, string? NavLabel)> CompleteServiceBookingAsync(
        string conversationId, Dictionary<string, string> slots)
    {
        try
        {
            var serviceType = slots.GetValueOrDefault("service_type", "未指定");
            var vehicleName = slots.GetValueOrDefault("vehicle_model", "未指定");
            var preferredDate = slots.GetValueOrDefault("preferred_date", "未指定");
            var preferredTime = slots.GetValueOrDefault("preferred_time", "未指定");
            var customerName = slots.GetValueOrDefault("customer_name", "未入力");
            var customerPhone = slots.GetValueOrDefault("customer_phone", "未入力");

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var appointmentId = $"APT-{Guid.NewGuid():N}"[..16];

            var customerId = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT customer_id FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });

            var dateTimeStr = $"{preferredDate} {preferredTime}";

            await _db.ExecuteAsync(@"
INSERT INTO service_appointments
  (appointment_id, customer_id, appointment_type, preferred_date, customer_request, status, created_at, updated_at)
VALUES
  (@AppointmentId, @CustomerId, 'service', @PreferredDate, @CustomerRequest, 'pending', @Now, @Now)",
                new
                {
                    AppointmentId = appointmentId,
                    CustomerId = customerId ?? "CUST-UNKNOWN",
                    PreferredDate = dateTimeStr,
                    CustomerRequest = $"サービス種別: {serviceType} / 車種: {vehicleName} / お名前: {customerName} / 電話: {customerPhone}",
                    Now = now
                });

            var responseText = $"""
                サービス予約を承りました。✅

                **ご予約内容:**
                - サービス: {serviceType}
                - 車種: {vehicleName}
                - 希望日: {preferredDate}
                - 時間: {preferredTime}
                - お名前: {customerName}
                - 電話番号: {customerPhone}

                予約番号: `{appointmentId}`
                """;

            return (responseText, $"/{_projectName}/DynamicEntity/DetailPage?entity=service_appointments&id={appointmentId}", "予約詳細を見る");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サービス予約確定エラー");
            return ("サービス予約の処理中にエラーが発生しました。お手数ですがお電話にてご連絡ください。", null, null);
        }
    }

    private async Task<(string ResponseText, string? NavUrl, string? NavLabel)> CompleteTradeInRequestAsync(
        string conversationId, Dictionary<string, string> slots)
    {
        try
        {
            var vehicleBrand = slots.GetValueOrDefault("vehicle_brand", "未指定");
            var vehicleName = slots.GetValueOrDefault("vehicle_model", "未指定");
            var vehicleYear = slots.GetValueOrDefault("vehicle_year", "未指定");
            var mileage = slots.GetValueOrDefault("mileage", "未指定");
            var customerName = slots.GetValueOrDefault("customer_name", "未入力");
            var customerPhone = slots.GetValueOrDefault("customer_phone", "未入力");

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var leadId = $"LEAD-{Guid.NewGuid():N}"[..16];

            var customerId = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT customer_id FROM ai_conversations WHERE conversation_id = @Id",
                new { Id = conversationId });

            await _db.ExecuteAsync(@"
INSERT INTO sales_leads
  (lead_id, customer_id, vehicle_interest, status, source_conversation_id, lead_source, created_at, updated_at)
VALUES
  (@LeadId, @CustomerId, @VehicleInterest, 'new', @ConversationId, 'ai_conversation', @Now, @Now)",
                new
                {
                    LeadId = leadId,
                    CustomerId = customerId ?? "CUST-UNKNOWN",
                    VehicleInterest = vehicleName,
                    ConversationId = conversationId,
                    Now = now
                });

            var responseText = $"""
                下取り査定のご依頼を承りました。✅

                **ご依頼内容:**
                - メーカー: {vehicleBrand}
                - 車種: {vehicleName}
                - 年式: {vehicleYear}
                - 走行距離: {mileage}
                - お名前: {customerName}
                - 電話番号: {customerPhone}

                担当者より {customerName} 様にご連絡いたします。
                """;

            return (responseText, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下取り査定確定エラー");
            return ("下取り査定の処理中にエラーが発生しました。お手数ですがお電話にてご連絡ください。", null, null);
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

        var responseText = "";
        var entityLabel = "general";
        List<Dictionary<string, string>>? dataRows = null;
        string? navUrl = null;
        string? navLabel = null;

        var staffIntent = DetectStaffAnalysisIntent(staffMessage);
        if (staffIntent == "priority_leads")
        {
            responseText = await GenerateLeadPriorityReportAsync();
            entityLabel = staffIntent;
        }
        else if (staffIntent == "today_followup")
        {
            responseText = await GenerateTodayFollowupReportAsync();
            entityLabel = staffIntent;
        }
        else if (staffIntent == "appointment_summary")
        {
            responseText = await GenerateAppointmentSummaryAsync();
            entityLabel = staffIntent;
        }
        else
        {
            (responseText, entityLabel, dataRows, navUrl, navLabel) =
                await GenerateAiResponseAsync(staffMessage, "staff", history);
        }

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

    private static string DetectStaffAnalysisIntent(string message) => message switch
    {
        var m when m.Contains("フォロー") || m.Contains("連絡") => "today_followup",
        var m when m.Contains("リード") && (m.Contains("優先") || m.Contains("今日")) => "priority_leads",
        var m when m.Contains("予約") && (m.Contains("今日") || m.Contains("明日")) => "appointment_summary",
        _ => "general"
    };

    private async Task<string> GenerateLeadPriorityReportAsync()
    {
        var leads = await _db.QueryAsync<(string LeadId, string CustomerId, int LeadScore, string Status, string? VehicleInterest)>(@"
SELECT lead_id AS LeadId, customer_id AS CustomerId, lead_score AS LeadScore,
       status AS Status, vehicle_interest AS VehicleInterest
FROM sales_leads
ORDER BY lead_score DESC
LIMIT 10");

        var sb = new StringBuilder();
        sb.AppendLine("# 📊 優先リードレポート");
        sb.AppendLine();
        sb.AppendLine("| 優先度 | リードID | 顧客ID | 車種 | スコア | ステータス |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var lead in leads)
        {
            var priority = lead.LeadScore >= 80 ? "高" : lead.LeadScore >= 60 ? "中" : "低";
            sb.AppendLine($"| {priority} | {lead.LeadId} | {lead.CustomerId} | {lead.VehicleInterest ?? "-"} | {lead.LeadScore} | {lead.Status} |");
        }

        sb.AppendLine();
        sb.AppendLine("**推奨アクション:** 高優先度リードから順に当日中にフォローしてください。");
        return sb.ToString();
    }

    private async Task<string> GenerateTodayFollowupReportAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd HH:mm:ss");
        var leads = await _db.QueryAsync<(string LeadId, string CustomerId, int LeadScore, string? VehicleInterest, string? LastContactAt)>(@"
SELECT lead_id AS LeadId, customer_id AS CustomerId, lead_score AS LeadScore,
       vehicle_interest AS VehicleInterest, last_contact_at AS LastContactAt
FROM sales_leads
WHERE last_contact_at IS NULL OR last_contact_at <= @Cutoff
ORDER BY lead_score DESC
LIMIT 20",
            new { Cutoff = cutoff });

        var sb = new StringBuilder();
        sb.AppendLine("# 📌 本日フォローアップ対象");
        sb.AppendLine();
        sb.AppendLine("| 優先度 | リードID | 顧客ID | 車種 | 最終連絡日 |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var lead in leads)
        {
            var priority = lead.LeadScore >= 80 ? "高" : lead.LeadScore >= 60 ? "中" : "低";
            var lastContact = string.IsNullOrWhiteSpace(lead.LastContactAt) ? "未連絡" : lead.LastContactAt;
            sb.AppendLine($"| {priority} | {lead.LeadId} | {lead.CustomerId} | {lead.VehicleInterest ?? "-"} | {lastContact} |");
        }

        sb.AppendLine();
        sb.AppendLine("**推奨アクション:** 7 日以上未連絡の顧客を優先的にフォローしてください。");
        return sb.ToString();
    }

    private async Task<string> GenerateAppointmentSummaryAsync()
    {
        var start = DateTime.UtcNow.Date.ToString("yyyy-MM-dd HH:mm:ss");
        var end = DateTime.UtcNow.Date.AddDays(2).ToString("yyyy-MM-dd HH:mm:ss");

        var appointments = await _db.QueryAsync<(string AppointmentId, string CustomerId, string AppointmentType, string PreferredDate, string Status)>(@"
SELECT appointment_id AS AppointmentId, customer_id AS CustomerId,
       appointment_type AS AppointmentType, preferred_date AS PreferredDate, status AS Status
FROM service_appointments
WHERE preferred_date >= @Start AND preferred_date < @End
ORDER BY preferred_date ASC",
            new { Start = start, End = end });

        var sb = new StringBuilder();
        sb.AppendLine("# 📅 本日〜明日の予約サマリー");
        sb.AppendLine();
        sb.AppendLine("| 予約ID | 顧客ID | 種別 | 予約日時 | ステータス |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var appt in appointments)
        {
            sb.AppendLine($"| {appt.AppointmentId} | {appt.CustomerId} | {appt.AppointmentType} | {appt.PreferredDate} | {appt.Status} |");
        }

        sb.AppendLine();
        sb.AppendLine("**推奨アクション:** 予約前日・当日の顧客へリマインド連絡を実施してください。");
        return sb.ToString();
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
        "vehicle_inquiry" => new List<string> { "在庫を確認", "試乗を予約", "見積もりを依頼" },
        "test_drive_booking" => new List<string> { "別の車種に変更", "日時を変更", "キャンセル" },
        "estimate_request" => new List<string> { "ローンで計算", "現金購入で計算", "下取り査定も依頼" },
        "service_booking" => new List<string> { "予約を変更", "他のサービスを追加", "費用の目安を確認" },
        "trade_inquiry" => new List<string> { "査定を依頼", "新車への乗り換えを検討", "現金で売却" },
        "appointment" => new List<string> { "予約を変更", "キャンセル", "新しい予約" },
        "escalation" => new List<string> { "担当者に繋ぐ", "折り返し連絡を希望" },
        _ => new List<string> { "車両を探す", "試乗を予約", "見積もりを依頼" }
    };

    private List<string> GetStaffQuickReplies(string intent) => intent switch
    {
        "priority_leads" => new List<string> { "全リードを見る", "未対応のみ表示", "本日の予約確認" },
        "today_followup" => new List<string> { "フォローアップ完了にする", "全顧客リスト" },
        "appointment_summary" => new List<string> { "予約詳細を見る", "スタッフ割り当て" },
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
