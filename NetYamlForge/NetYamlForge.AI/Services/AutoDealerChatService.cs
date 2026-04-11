// ファイル概要：auto-dealer-demo 専用 AI チャットサービス（BaseChatService 統合版）
// 共通ロジックは BaseChatService に集約。このクラスは差分のみを実装します。

using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services.Providers;
using NetYamlForge.AI.Infrastructure;

namespace NetYamlForge.AI.Services;

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
        IAIProjectContext projectContext,
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
        : base(db, cliFactory, llmProvider, skillLoader, projectContext, logger, queryParser, queryExecutor, queryFormatter,
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
            
            // 応答スタイル定義
            systemPrompt += "---" + Environment.NewLine + Environment.NewLine;
            systemPrompt += "# 🎯 応答ルール" + Environment.NewLine;
            systemPrompt += $"- 権限レベル: 顧客（読み取り専用）" + Environment.NewLine;
            systemPrompt += $"- アクセス可能データ: 車両在庫・サービス予約（自分の分）" + Environment.NewLine;
            systemPrompt += $"- 応答スタイル: 丁寧な敬語で、具体的な情報をご案内" + Environment.NewLine;
            systemPrompt += $"- 応答言語: 必ず日本語で回答してください" + Environment.NewLine;
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
    // UI コンポーネント生成（Slot-filling専用）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Slot-filling の現在の段階に応じた UI コンポーネントを生成
    /// </summary>
    private List<UiComponent>? BuildSlotFillingComponents(string scenario, SlotSession session, List<string> missingSlots)
    {
        if (missingSlots.Count == 0) return null;

        var nextSlot = missingSlots[0];

        return nextSlot switch
        {
            "vehicle_model" => BuildVehicleSelectionComponent(),
            "preferred_date" => BuildDatePickerComponent(),
            "preferred_time" => BuildTimeSelectionComponent(),
            "customer_name" => BuildNameInputComponent(),
            "customer_phone" => BuildPhoneInputComponent(),
            "service_type" => BuildServiceTypeSelectionComponent(),
            "grade" => BuildGradeInputComponent(),
            _ => null
        };
    }

    /// <summary>
    /// 車種選択コンポーネント
    /// </summary>
    private List<UiComponent> BuildVehicleSelectionComponent()
    {
        var vehicles = GetAvailableVehicles();

        var vehicleIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "プリウス", "🚗" }, { "ランドクルーザー", "🚙" }, { "アルファード", "🚐" },
            { "CR-V", "🚙" }, { "フィット", "🚗" }, { "アリア", "⚡" },
            { "フォレスター", "🚙" }, { "ヴェゼル", "🚙" }, { "ヤリス", "🚗" },
            { "カローラ", "🚗" }, { "カムリ", "🚗" }, { "RAV4", "🚙" },
            { "ハリアー", "🚙" }, { "セレナ", "🚐" }, { "ステップワゴン", "🚐" },
            { "ノア", "🚐" }, { "ヴォクシー", "🚐" }, { "アクア", "🚗" },
        };

        var options = vehicles.Count > 0
            ? vehicles.Select(v => new SelectOption(
                Label: $"{vehicleIcons.GetValueOrDefault(v, "🚗")} {v}",
                Value: v,
                Description: null,
                Icon: vehicleIcons.GetValueOrDefault(v, "🚗")
            )).ToList()
            : new List<SelectOption>
            {
                new("🚗 プリウス", "プリウス"),
                new("🚙 ランドクルーザー", "ランドクルーザー"),
                new("🚐 アルファード", "アルファード"),
                new("🚙 CR-V", "CR-V"),
                new("🚗 フィット", "フィット"),
                new("⚡ アリア", "アリア"),
                new("🚙 フォレスター", "フォレスター"),
                new("🚙 ヴェゼル", "ヴェゼル"),
            };

        return new List<UiComponent>
        {
            new SingleSelectGroup(
                Title: "どの車種の試乗をご希望ですか？",
                Options: options,
                SubmitLabel: "この車種で試乗"
            )
        };
    }

    /// <summary>
    /// 日付選択コンポーネント
    /// </summary>
    private List<UiComponent> BuildDatePickerComponent()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        var maxDate = DateTime.Today.AddMonths(1);

        return new List<UiComponent>
        {
            new DateTimePicker(
                Title: "ご希望の日付を選択してください",
                Mode: "date",
                MinDate: tomorrow.ToString("yyyy-MM-dd"),
                MaxDate: maxDate.ToString("yyyy-MM-dd"),
                SubmitLabel: "この日にちで決定"
            )
        };
    }

    /// <summary>
    /// 時間帯選択コンポーネント
    /// </summary>
    private List<UiComponent> BuildTimeSelectionComponent()
    {
        return new List<UiComponent>
        {
            new SingleSelectGroup(
                Title: "ご希望の時間帯を選択してください",
                Options: new List<SelectOption>
                {
                    new("🌅 午前9時", "09:00"),
                    new("🌅 午前10時", "10:00"),
                    new("🌅 午前11時", "11:00"),
                    new("☀️ 午後1時", "13:00"),
                    new("☀️ 午後2時", "14:00"),
                    new("☀️ 午後3時", "15:00"),
                    new("🌇 午後4時", "16:00"),
                    new("🌇 午後5時", "17:00"),
                },
                SubmitLabel: "この時間で決定"
            )
        };
    }

    /// <summary>
    /// 氏名入力コンポーネント
    /// </summary>
    private List<UiComponent> BuildNameInputComponent()
    {
        return new List<UiComponent>
        {
            new TextSuggestions(
                Placeholder: "お名前を入力してください",
                Suggestions: new List<string>()
            )
        };
    }

    /// <summary>
    /// 電話番号入力コンポーネント
    /// </summary>
    private List<UiComponent> BuildPhoneInputComponent()
    {
        return new List<UiComponent>
        {
            new TextSuggestions(
                Placeholder: "電話番号を入力してください（例: 090-1234-5678）",
                Suggestions: new List<string>()
            )
        };
    }

    /// <summary>
    /// サービス種別選択コンポーネント
    /// </summary>
    private List<UiComponent> BuildServiceTypeSelectionComponent()
    {
        return new List<UiComponent>
        {
            new SingleSelectGroup(
                Title: "どのようなご用件でしょうか？",
                Options: new List<SelectOption>
                {
                    new("🔧 車検", "車検"),
                    new("🔍 定期点検", "点検"),
                    new("🛢️ オイル交換", "オイル交換"),
                    new("🔴 タイヤ交換", "タイヤ交換"),
                    new("🔧 修理", "修理"),
                    new("🎨 板金塗装", "板金塗装"),
                },
                SubmitLabel: "このサービスで予約"
            )
        };
    }

    /// <summary>
    /// グレード入力コンポーネント（見積もり用）
    /// </summary>
    private List<UiComponent> BuildGradeInputComponent()
    {
        return new List<UiComponent>
        {
            new SingleSelectGroup(
                Title: "グレードを選択してください",
                Options: new List<SelectOption>
                {
                    new("S", "S"),
                    new("G", "G"),
                    new("X", "X"),
                    new("Z", "Z"),
                    new("プレミアム", "プレミアム"),
                    new("スタンダード", "スタンダード"),
                },
                SubmitLabel: "このグレードで見積もる"
            )
        };
    }

    /// <summary>
    /// 在庫車両一覧を取得（キャッシュ付き）
    /// </summary>
    private List<string> GetAvailableVehicles()
    {
        try
        {
            var vehicles = _db.Query<dynamic>(@"
                SELECT DISTINCT model, brand, maker FROM vehicles
                WHERE status = 'available' OR status = 'in_stock' OR status = 'test_drive'
                ORDER BY brand, model
            ").Select(v =>
            {
                var model = (string)v.model;
                var brand = (string?)v.brand;
                return !string.IsNullOrEmpty(brand) ? $"{brand} {model}" : model;
            }).ToList();
            return vehicles;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Slot-filling 用の次の質問プロンプトをテンプレートから生成
    /// </summary>
    private string GetSlotPromptMessage(string scenario, string slotName, Dictionary<string, string> filledSlots)
    {
        var scenarioName = scenario switch
        {
            "test_drive" => "試乗予約",
            "estimate" => "見積もり依頼",
            "appointment_service" => "サービス予約",
            "trade_in" => "下取り査定",
            _ => "ご予約"
        };

        return (scenario, slotName) switch
        {
            // 試乗予約
            ("test_drive", "vehicle_model") => "試乗予約をご案内します 🚗\n\nどの車種の試乗をご希望ですか？下のリストから選んでください。",
            ("test_drive", "preferred_date") => $"{filledSlots.GetValueOrDefault("vehicle_model", "")}の試乗ですね！承ります。\n\nご希望の日付を選択してください。",
            ("test_drive", "preferred_time") => $"承知しました。{FormatDateDisplay(filledSlots.GetValueOrDefault("preferred_date", ""))}ですね！\n\nご希望の時間帯を選択してください。",
            ("test_drive", "customer_name") => "ありがとうございます。\n\nご予約のため、お名前を入力してください。",
            ("test_drive", "customer_phone") => $"{filledSlots.GetValueOrDefault("customer_name", "")} 様ですね。ありがとうございます。\n\nご連絡先電話番号を入力してください。\n（例：090-1234-5678）",

            // 見積もり依頼
            ("estimate", "vehicle_model") => "見積もりをご案内します 💰\n\nどの車種の御見積もりをご希望ですか？",
            ("estimate", "grade") => "グレードを選択してください。",
            ("estimate", "customer_name") => "見積もりのため、お名前を入力してください。",
            ("estimate", "customer_phone") => "ご連絡先電話番号を入力してください。",

            // サービス予約
            ("appointment_service", "service_type") => "サービス予約をご案内します 🔧\n\nどのようなご用件でしょうか？",
            ("appointment_service", "vehicle_model") => "お車の車種を教えてください。",
            ("appointment_service", "preferred_date") => "ご希望の日付を選択してください。",
            ("appointment_service", "preferred_time") => "ご希望の時間帯を選択してください。",
            ("appointment_service", "customer_name") => "ご予約のため、お名前を入力してください。",
            ("appointment_service", "customer_phone") => "ご連絡先電話番号を入力してください。",

            // 下取り査定
            ("trade_in", "vehicle_brand") => "下取り査定をご案内します 🚗\n\nお車のメーカーを教えてください。",
            ("trade_in", "vehicle_model") => "車種を教えてください。",
            ("trade_in", "vehicle_year") => "初度登録年を教えてください。",
            ("trade_in", "mileage") => "走行距離を教えてください。",
            ("trade_in", "customer_name") => "査定のため、お名前を入力してください。",
            ("trade_in", "customer_phone") => "ご連絡先電話番号を入力してください。",

            _ => "続けて教えてください。"
        };
    }

    /// <summary>
    /// 完了コンポーネント（クイックリプライ）
    /// </summary>
    private static List<UiComponent> BuildCompletionCardComponents(string scenario, Dictionary<string, string> slots)
    {
        return scenario switch
        {
            "test_drive" => new List<UiComponent>
            {
                new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("予約を変更", "予約を変更したい"),
                        new("キャンセル", "予約をキャンセルしたい"),
                        new("詳細を見る", "予約詳細を確認したい"),
                    })
            },
            "estimate" => new List<UiComponent>
            {
                new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("ローンで計算", "ローンで見積もりたい"),
                        new("現金で購入", "現金で購入したい"),
                        new("試乗も予約", "試乗も予約したい"),
                    })
            },
            "appointment_service" => new List<UiComponent>
            {
                new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("予約を変更", "予約を変更したい"),
                        new("キャンセル", "予約をキャンセルしたい"),
                    })
            },
            _ => new List<UiComponent>()
        };
    }

    private static string FormatDateDisplay(string dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return "";
        if (dateStr == "明日") return "明日";
        if (dateStr == "今日") return "今日";
        if (dateStr == "明後日") return "明後日";
        if (dateStr == "来週") return "来週";
        if (DateTime.TryParse(dateStr, out var dt))
        {
            var dayOfWeek = "日月火水木金土"[(int)dt.DayOfWeek];
            return $"{dt.Month}月{dt.Day}日（{dayOfWeek}）";
        }
        return dateStr;
    }

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

    public async Task<ChatMessageResult> SendMessageAsync(string conversationId, string customerMessage, string? providerOverride = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        _logger.LogInformation("[SendMessage] 開始: Conv={ConvId}, Message={Message}", conversationId, customerMessage);

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
        List<Dictionary<string, string>>? dataRows = null;
        var navUrl = "";
        var navLabel = "";

        // 0. アクティブなSlot-fillingセッションの確認、または新規作成
        _logger.LogInformation("🔍 Slot-fillingチェック: _slotFilling={SlotFilling}, _intentClassifier={IntentClassifier}",
            _slotFilling != null, _intentClassifier != null);

        // ✅ Slot-filling セッション継続中は意図分類をスキップ（LLM 不要 → 高速）
        if (_slotFilling != null)
        {
            var activeScenario = await _slotFilling.GetActiveScenarioAsync(conversationId);
            _logger.LogInformation("📋 アクティブシナリオ: {Scenario}", activeScenario ?? "null");

            // ✅ 既存セッション継続中の場合はルールベースで高速処理
            if (activeScenario != null)
            {
                _logger.LogInformation("🔀 [FastPath] Slot-filling 処理開始: Scenario={Scenario}, Message={Message}", activeScenario, customerMessage.Substring(0, Math.Min(30, customerMessage.Length)));

                // ✅ Slot-filling 中は全てルールベース抽出（LLM 不要）
                await FallbackSlotExtractionAsync(conversationId, customerMessage, activeScenario);

                // 更新後のセッションを取得
                var activeSession = await _slotFilling.GetSessionAsync(conversationId, activeScenario, _projectName);

                _logger.LogInformation("Slot-fillingセッション: Conv={ConvId}, Scenario={Scenario}, 収集済み=[{Collected}], 完了={IsComplete}",
                    conversationId,
                    activeScenario,
                    string.Join(", ", activeSession.GetCollectedValues().Select(kv => $"{kv.Key}='{kv.Value}'")),
                    activeSession.IsComplete);

                // ✅ 全スロットが埋まったら、コードで予約確定処理
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

                    // 完了コンポーネント: 基本クイックリプライ + 予約確認カード
                    var completionComponents = BuildCompletionCardComponents(activeScenario, slots);
                    completionComponents.Add(new CardCarousel(
                        Title: "✅ 予約が確定しました",
                        Items: new List<CardItem>
                        {
                            new CardItem(
                                Id: "confirmation",
                                Title: "予約確認",
                                Subtitle: completionText.Split('\n').FirstOrDefault()?.Trim(),
                                BadgeLabel: "確定",
                                BadgeStyle: "success",
                                Actions: !string.IsNullOrEmpty(completionNavUrl)
                                    ? new List<CardAction> { new("詳細を見る", "url", completionNavUrl) }
                                    : null
                            )
                        }
                    ));

                    await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, completionTime, resolvedIntent, 0.9, sentimentScore, completionComponents);
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
                        NavigationLabel = navLabel,
                        Components = completionComponents
                    };
                }
                
                // ✅ スロット未完成の場合、テンプレート応答 + UIコンポーネントで高速応答（LLM不要）
                var collectedSlots = activeSession.GetCollectedValues();
                var missingSlotNames = await _slotFilling.GetMissingRequiredSlotNamesAsync(conversationId, activeScenario, _projectName);

                if (missingSlotNames.Count > 0)
                {
                    var nextSlot = missingSlotNames[0];
                    responseText = GetSlotPromptMessage(activeScenario, nextSlot, collectedSlots);
                    resolvedIntent = MapScenarioToIntent(activeScenario);

                    // UI コンポーネント生成
                    var slotComponents = BuildSlotFillingComponents(activeScenario, activeSession, missingSlotNames);

                    var slotPromptTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, slotPromptTime, resolvedIntent, 0.9, sentimentScore, slotComponents);

                    await _chatHistory.SaveMessageAsync(customerId ?? _projectName, customerMessage, "user",
                        provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);
                    await _chatHistory.SaveMessageAsync(customerId ?? _projectName, responseText, "assistant",
                        provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);

                    sw.Stop();
                    _logger.LogInformation("[FastSlotFilling] テンプレート応答: Scenario={Scenario}, NextSlot={Slot}, Time={Time}ms",
                        activeScenario, nextSlot, sw.ElapsedMilliseconds);

                    return new ChatMessageResult
                    {
                        ResponseText = responseText,
                        Intent = resolvedIntent,
                        SuggestHandover = false,
                        QuickReplies = GetCustomerQuickReplies(resolvedIntent),
                        ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
                        AiProvider = _defaultProvider,
                        MessageTimestamp = slotPromptTime,
                        NavigationUrl = "",
                        NavigationLabel = "",
                        Components = slotComponents
                    };
                }

                // フォールバック: 不明な場合は通常LLMフローへ
                _logger.LogInformation("Slot-filling: 次のスロットが不明なためLLMに委譲: Conv={ConvId}", conversationId);
            }
            else
            {
                // ✅ アクティブセッションなし → 意図分類で新規セッションを開始
                _logger.LogInformation("🔍 アクティブセッションなし、意図分類で新規セッション確認");
            }
        }

        // 1. インテント分類（アクティブセッションがない場合のみ実行）
        if (_intentClassifier != null && string.IsNullOrEmpty(responseText))
        {
            var intentResult = await _intentClassifier.ClassifyAsync(customerMessage, projectId: _projectName);
            resolvedIntent = intentResult.Intent;

            // Slot-filling対象インテントの場合、新規セッションを開始
            var slotScenario = MapIntentToScenario(resolvedIntent);
            if (slotScenario != null)
            {
                _logger.LogInformation("🚀 Slot-filling: 新規セッション開始: Scenario={Scenario}", slotScenario);
                await _slotFilling!.GetSessionAsync(conversationId, slotScenario, _projectName);
                resolvedIntent = MapScenarioToIntent(slotScenario);

                // セッション作成後の処理は次のループで
                // 初回応答は LLM で生成
            }
        }

        // 3. インテント分類が不要/失敗した場合は通常のLLMフロー
        if (string.IsNullOrEmpty(responseText))
        {
            // アクティブなSlot-fillingセッションがある場合、historyにSlot状態を注入
            var historyForAI = history.ToList();
            
            if (_slotFilling != null)
            {
                var stillActive = await _slotFilling.GetActiveScenarioAsync(conversationId);
                if (stillActive != null)
                {
                    var collectedSlots = await _slotFilling.GetCollectedSlotsAsync(conversationId, _projectName);
                    var nextSlot = await _slotFilling.GetNextRequiredSlotAsync(conversationId, stillActive, _projectName);
                    
                    // 槽位状态消息を构建
                    var slotStatusMessage = BuildSlotStatusMessage(collectedSlots, nextSlot, stillActive);
                    if (!string.IsNullOrEmpty(slotStatusMessage))
                    {
                        // Insert(0) にすることで、BuildPromptWithHistory の Reverse() 後に末尾（最新）として現れる
                        historyForAI.Insert(0, ("system", slotStatusMessage));
                        _logger.LogInformation("Slot-filling: AIに状態メッセージを注入: {Message}", slotStatusMessage);
                    }
                }
            }
            
            var (aiResponseText, aiIntent, aiDataRows, aiNavUrl, aiNavLabel) =
                await GenerateAiResponseAsync(customerMessage, "customer", historyForAI, providerOverride);
            responseText = aiResponseText;
            resolvedIntent = aiIntent;
            dataRows = aiDataRows;
            navUrl = aiNavUrl ?? "";
            navLabel = aiNavLabel ?? "";
        }

        // ✅ AI 回复的消息时间戳
        var aiResponseTime2 = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var usedProvider = providerOverride ?? _defaultProvider;

        // コンポーネントを先に構築してDBに保存
        var components = BuildComponents(resolvedIntent, dataRows, "customer");

        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, aiResponseTime2, resolvedIntent, 0.9, sentimentScore, components);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET last_intent = @Intent, last_confidence = 0.9, sentiment_score = @Sentiment, updated_at = @Now
WHERE conversation_id = @Id",
            new { Intent = resolvedIntent, Sentiment = sentimentScore, Now = now, Id = conversationId });

        await _chatHistory.SaveMessageAsync(customerId ?? _projectName, customerMessage, "user",
            provider: usedProvider, chatContext: "dealer-customer", projectName: _projectName);
        await _chatHistory.SaveMessageAsync(customerId ?? _projectName, responseText, "assistant",
            provider: usedProvider, chatContext: "dealer-customer", projectName: _projectName);

        sw.Stop();
        return new ChatMessageResult
        {
            ResponseText = responseText,
            Intent = resolvedIntent,
            SuggestHandover = false,
            QuickReplies = GetCustomerQuickReplies(resolvedIntent),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
            DataRows = dataRows,
            NavigationUrl = navUrl,
            NavigationLabel = navLabel,
            AiProvider = usedProvider,
            MessageTimestamp = aiResponseTime2,
            Components = components
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

    /// <summary>
    /// AI に渡す槽位状态消息を构建
    /// </summary>
    private static string BuildSlotStatusMessage(Dictionary<string, string> collectedSlots, SlotRequest? nextSlot, string scenario)
    {
        var sb = new System.Text.StringBuilder();

        var scenarioName = scenario switch
        {
            "test_drive" => "試乗予約",
            "estimate" => "見積もり依頼",
            "appointment_service" => "サービス予約",
            "trade_in" => "下取り査定",
            _ => "予約"
        };

        sb.AppendLine($"## 📋 {scenarioName} - 情報収集状況（システム指示）");
        sb.AppendLine();

        if (collectedSlots.Count > 0)
        {
            sb.AppendLine("✅ **既に収集済みの情報:**");
            foreach (var slot in collectedSlots)
            {
                var displayName = slot.Key switch
                {
                    "vehicle_model" => "車種",
                    "preferred_date" => "希望日",
                    "preferred_time" => "時間帯",
                    "customer_name" => "お名前",
                    "customer_phone" => "電話番号",
                    "service_type" => "サービス種別",
                    "grade" => "グレード",
                    _ => slot.Key
                };
                sb.AppendLine($"- {displayName}: {slot.Value}");
            }
            sb.AppendLine();
        }

        if (nextSlot != null)
        {
            var remainingSlots = GetRemainingSlotNames(nextSlot);
            sb.AppendLine("🎯 **次のアクション（必須）:**");
            sb.AppendLine($"ユーザーに以下の質問を**そのまま**伝えてください：");
            sb.AppendLine();
            sb.AppendLine($"> **{nextSlot.Prompt}**");
            sb.AppendLine();
            sb.AppendLine($"**重要ルール**:");
            sb.AppendLine($"1. 上記の質問だけをユーザーに伝えてください");
            sb.AppendLine($"2. 他の情報を一緒に聞かないでください");
            sb.AppendLine($"3. まだ收集していない情報: {remainingSlots}");
            sb.AppendLine($"4. ユーザーが他のことを聞いても、まずはこの質問に答えてもらってください");
            sb.AppendLine($"5. 短く丁寧に返信してください");
        }
        else
        {
            sb.AppendLine("✅ 全ての情報が収集済みです。");
            sb.AppendLine("**次のアクション**: 収集した情報で予約を確定し、確認メッセージを返してください。");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 残りの槽位名を取得
    /// </summary>
    private static string GetRemainingSlotNames(SlotRequest nextSlot)
    {
        // 次の槽位の名前を返す（簡易実装）
        return nextSlot.SlotName switch
        {
            "vehicle_model" => "車種",
            "preferred_date" => "希望日",
            "preferred_time" => "時間帯",
            "customer_name" => "お名前",
            "customer_phone" => "電話番号",
            "service_type" => "サービス種別",
            "grade" => "グレード",
            _ => nextSlot.SlotName
        };
    }
    
    /// <summary>
    /// スロット値の検証（AI 抽出の誤りを防ぐ）
    /// </summary>
    private static bool ValidateSlotValue(string slotName, string value, out string validatedValue)
    {
        validatedValue = value;
        
        return slotName switch
        {
            // 電話番号は数字とハイフンのみ、7-13 桁の数字に相当する形式
            "customer_phone" => ValidatePhoneNumber(value, out validatedValue),
            
            // 名前は日本語文字、2-20 文字、数字のみや特殊文字は拒否
            "customer_name" => ValidateCustomerName(value, out validatedValue),
            
            // 日付は自然言語（明日、来週など）または日付形式
            "preferred_date" => !string.IsNullOrWhiteSpace(value) && value.Length <= 50,
            
            // 時間は「午前 10 時」「午後 2 時」などの形式
            "preferred_time" => !string.IsNullOrWhiteSpace(value) && value.Length <= 50,
            
            // 車種は一般的な車種名
            "vehicle_model" => !string.IsNullOrWhiteSpace(value) && value.Length <= 50,
            
            _ => true
        };
    }
    
    /// <summary>
    /// 電話番号の検証
    /// </summary>
    private static bool ValidatePhoneNumber(string value, out string validatedValue)
    {
        validatedValue = value;
        
        // 数字のみの抽出
        var digits = System.Text.RegularExpressions.Regex.Replace(value, @"[^\d]", "");
        
        // 7-11 桁の数字（日本電話番号）
        if (digits.Length >= 7 && digits.Length <= 11)
        {
            // ハイフン形式にフォーマット
            if (digits.Length == 10)
            {
                validatedValue = $"{digits.Substring(0, 3)}-{digits.Substring(3, 4)}-{digits.Substring(7, 3)}";
            }
            else if (digits.Length == 11)
            {
                validatedValue = $"{digits.Substring(0, 4)}-{digits.Substring(4, 4)}-{digits.Substring(8, 3)}";
            }
            else
            {
                validatedValue = value; // 元の値を保持
            }
            return true;
        }
        
        // ハイフン付き形式（090-1234-5678）
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{2,4}-\d{2,4}-\d{4}$"))
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// 顧客名前の検証
    /// </summary>
    private static bool ValidateCustomerName(string value, out string validatedValue)
    {
        validatedValue = value;
        
        // 2-20 文字
        if (value.Length < 2 || value.Length > 20)
        {
            return false;
        }
        
        // 数字のみの場合は拒否
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d+$"))
        {
            return false;
        }
        
        // 日本語文字（ひらがな・カタカナ・漢字）または英字を含む
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}a-zA-Z]"))
        {
            return true;
        }
        
        return false;
    }

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
            var missingSlots = session.GetMissingSlots();
            _logger.LogInformation("Slot-filling: Conv={ConvId}, Scenario={Scenario}, 収集済みSlots=[{Collected}], 未収集Slots=[{Missing}], 完了={IsComplete}",
                conversationId,
                scenario,
                string.Join(", ", collectedSlots.Select(kv => $"{kv.Key}='{kv.Value}'")),
                string.Join(", ", missingSlots.Select(s => s.Name)),
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

            // AI に抽出を依頼するシステムプロンプト（systemPromptOverride として渡す）
            var systemPromptOverride = $@"あなたは厳格な情報抽出アシスタントです。ユーザーのメッセージに**明示的かつ具体的に記述されている**情報のみを抽出してください。

抽出対象スロット: {slotsToExtract}

**絶対に守るべきルール:**
- 必ず JSON 形式のみで出力してください（コードブロック不要、JSONのみ）
- ユーザーのメッセージに**明記されていない**値は必ず null にしてください
- 推測・補完・デフォルト値の設定は**絶対禁止**です
- 「〜したい」「〜お願いします」などの意思表明のみのメッセージでは、全スロットを null にしてください
- 名前は敬語表現（です、と申します、等）を除いた部分のみを抽出
- 電話番号は数字とハイフンをそのまま抽出
- 日付・時間の日本語表現（明日、来週、午前10時等）はそのまま抽出

**正しい抽出例:**
- 「試乗したいです」→ 全スロット null（具体的な情報なし）
- 「プリウスを試乗したいです」→ vehicle_model: ""プリウス""、他は null
- 「明日の午前10時にプリウスを試乗したい、田中です」→ vehicle_model: ""プリウス""、preferred_date: ""明日""、preferred_time: ""午前10時""、customer_name: ""田中""、customer_phone: null";

            // ユーザーメッセージ部分には実際のメッセージのみを渡す
            var userPrompt = $"メッセージ: {message}";

            // ✅ systemPromptOverride を明示的に渡す
            _logger.LogInformation("[SlotExtraction] AI抽出開始: Scenario={Scenario}, SystemPromptLength={SysLen}, UserPrompt={Prompt}",
                scenario, systemPromptOverride.Length, userPrompt);

            var response = await _llmProvider.CompleteAsync(
                userPrompt,
                System.Threading.CancellationToken.None,
                systemPromptOverride);

            _logger.LogInformation("[SlotExtraction] AI抽出完了: Scenario={Scenario}, ResponseLength={Length}, Response={Response}",
                scenario, response?.Length ?? 0, response ?? "(null)");

            // JSON をパースしてスロットを更新
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            var aiExtracted = false;

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                
                try
                {
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
                                    // 🔒 槽位値の検証
                                    if (!ValidateSlotValue(kvp.Key, value, out var validatedValue))
                                    {
                                        _logger.LogWarning("AI抽出: スロット値の検証失敗、スキップ: Slot={Slot}, Value={Value}",
                                            kvp.Key, value);
                                        continue;
                                    }
                                    
                                    await _slotFilling.UpdateSlotAsync(conversationId, kvp.Key, validatedValue, _projectName);
                                    updated = true;
                                    aiExtracted = true;
                                    _logger.LogInformation("AIスロット抽出成功: Scenario={Scenario}, Slot={Slot}, Value={Value}",
                                        scenario, kvp.Key, validatedValue);
                                }
                            }
                        }

                        if (updated)
                        {
                            _logger.LogInformation("スロット更新完了: Scenario={Scenario}", scenario);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "[SlotExtraction] AI抽出JSONパース失敗: Scenario={Scenario}, Response={Response}",
                        scenario, response);
                }
            }
            
            // AI抽出が失敗した場合、簡易ルールベースの抽出を試みる
            if (!aiExtracted)
            {
                _logger.LogInformation("[SlotExtraction] AI抽出失敗のため、ルールベース抽出を実行: Scenario={Scenario}, Message={Message}",
                    scenario, message);
                
                await FallbackSlotExtractionAsync(conversationId, message, scenario);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SlotExtraction] AIスロット抽出に失敗しました: Scenario={Scenario}, Message={Message}",
                scenario, message);
            // AI に失敗した場合はルールベース抽出を試みる
            await FallbackSlotExtractionAsync(conversationId, message, scenario);
        }
    }
    
    /// <summary>
    /// ルールベースのスロット値抽出（AI 抽出失敗時のフォールバック）
    /// </summary>
    private async Task FallbackSlotExtractionAsync(string conversationId, string message, string scenario)
    {
        if (_slotFilling == null) return;
        
        try
        {
            var extracted = false;
            
            if (scenario == "test_drive" || scenario == "estimate" || scenario == "vehicle_inquiry")
            {
                // 車種名の簡易抽出（一般的な車種キーワード）
                var vehicleModels = new[] {
                    "プリウス PHV", "プリウス", "ヤリス", "クラウン", "カローラ", "カムリ",
                    "RAV4", "ハリアー", "ランドクルーザー 300", "ランドクルーザー",
                    "アルファード", "ヴェゼル", "フィット", "アクア", "ノア", "ヴォクシー",
                    "セレナ", "ステップワゴン", "CR-V", "アリア", "フォレスター"
                };
                foreach (var model in vehicleModels)
                {
                    if (message.Contains(model))
                    {
                        await _slotFilling.UpdateSlotAsync(conversationId, "vehicle_model", model, _projectName);
                        _logger.LogInformation("[Fallback] 車種抽出成功: Model={Model}", model);
                        extracted = true;
                        break;
                    }
                }
            }
            
            // 名前の抽出（「〜です」「〜と申します」パターン、または純粋な日本語名）
            if (scenario == "test_drive" || scenario == "estimate" || scenario == "appointment_service" || scenario == "trade_in")
            {
                var namePattern = System.Text.RegularExpressions.Regex.Match(message, @"(.+?)です$");
                if (namePattern.Success && !namePattern.Value.Contains("試乗") && !namePattern.Value.Contains("予約") && !namePattern.Value.Contains("見積"))
                {
                    var name = namePattern.Groups[1].Value.Trim();
                    if (name.Length >= 2 && name.Length <= 20)
                    {
                        await _slotFilling.UpdateSlotAsync(conversationId, "customer_name", name, _projectName);
                        _logger.LogInformation("[Fallback] 名前抽出成功: Name={Name}", name);
                        extracted = true;
                    }
                }
                // 日本語名パターン（ひらがな・カタカナ・漢字）
                else if (System.Text.RegularExpressions.Regex.IsMatch(message, @"^[\u3040-\u309f\u30a0-\u30ff\u4e00-\u9faf\s・]{2,20}$"))
                {
                    var name = message.Trim();
                    await _slotFilling.UpdateSlotAsync(conversationId, "customer_name", name, _projectName);
                    _logger.LogInformation("[Fallback] 名前抽出成功（日本語名）: Name={Name}", name);
                    extracted = true;
                }
            }
            
            // 電話番号の抽出
            var phonePattern = System.Text.RegularExpressions.Regex.Match(message, @"(\d{2,4}-\d{2,4}-\d{4})");
            if (phonePattern.Success)
            {
                await _slotFilling.UpdateSlotAsync(conversationId, "customer_phone", phonePattern.Groups[1].Value, _projectName);
                _logger.LogInformation("[Fallback] 電話番号抽出成功: Phone={Phone}", phonePattern.Groups[1].Value);
                extracted = true;
            }
            
            // 日付の抽出
            if (message.Contains("明日") || message.Contains("明後日") || message.Contains("今日") || message.Contains("来週"))
            {
                var dateWords = new[] { "明日", "明後日", "今日", "来週" };
                foreach (var dateWord in dateWords)
                {
                    if (message.Contains(dateWord))
                    {
                        await _slotFilling.UpdateSlotAsync(conversationId, "preferred_date", dateWord, _projectName);
                        _logger.LogInformation("[Fallback] 日付抽出成功: Date={Date}", dateWord);
                        extracted = true;
                        break;
                    }
                }
            }
            
            // 時間の抽出
            if (message.Contains("午前") || message.Contains("午後"))
            {
                var timePattern = System.Text.RegularExpressions.Regex.Match(message, @"(午前|午後)\s*\d{1,2}\s*時");
                if (timePattern.Success)
                {
                    await _slotFilling.UpdateSlotAsync(conversationId, "preferred_time", timePattern.Value, _projectName);
                    _logger.LogInformation("[Fallback] 時間抽出成功: Time={Time}", timePattern.Value);
                    extracted = true;
                }
            }

            // 時間フォーマットの抽出（HH:MM、ボタン選択から）
            if (!extracted || scenario == "test_drive")
            {
                var timeMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{1,2}):(\d{2})");
                if (timeMatch.Success)
                {
                    var timeStr = $"{int.Parse(timeMatch.Groups[1].Value):D2}:{timeMatch.Groups[2].Value}";
                    await _slotFilling.UpdateSlotAsync(conversationId, "preferred_time", timeStr, _projectName);
                    _logger.LogInformation("[Fallback] 時間抽出成功（HH:MM）: Time={Time}", timeStr);
                    extracted = true;
                }
            }

            // 日付フォーマットの抽出（YYYY-MM-DD、日付ピッカーから）
            if (scenario == "test_drive" || scenario == "appointment_service")
            {
                var dateMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{4}-\d{2}-\d{2})");
                if (dateMatch.Success)
                {
                    await _slotFilling.UpdateSlotAsync(conversationId, "preferred_date", dateMatch.Groups[1].Value, _projectName);
                    _logger.LogInformation("[Fallback] 日付抽出成功（YYYY-MM-DD）: Date={Date}", dateMatch.Groups[1].Value);
                    extracted = true;
                }
            }
            
            if (extracted)
            {
                _logger.LogInformation("[Fallback] ルールベース抽出完了: Extracted={Extracted}", extracted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Fallback] ルールベース抽出エラー");
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

    public async Task<ChatMessageResult> SendStaffMessageAsync(string conversationId, string staffMessage, string? providerOverride = null)
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
                await GenerateAiResponseAsync(staffMessage, "staff", history, providerOverride);
        }

        var aiResponseTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var usedProvider = providerOverride ?? _defaultProvider;

        await SaveMessageAsync($"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai", responseText, aiResponseTime, entityLabel, 0.9, 0);
        await _db.ExecuteAsync(@"
UPDATE ai_conversations SET last_intent = @Intent, updated_at = @Now WHERE conversation_id = @Id",
            new { Intent = entityLabel, Now = now, Id = conversationId });

        await _chatHistory.SaveMessageAsync(_projectName, staffMessage, "user",
            provider: usedProvider, chatContext: "dealer-staff", projectName: _projectName);
        await _chatHistory.SaveMessageAsync(_projectName, responseText, "assistant",
            provider: usedProvider, chatContext: "dealer-staff", projectName: _projectName);

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
            AiProvider = usedProvider,
            MessageTimestamp = aiResponseTime
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
SELECT message_id AS MessageId, sender AS Sender, content AS Content, timestamp AS Timestamp, intent AS Intent, components_json AS ComponentsJson
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

    // プロンプト MD ファイルのインメモリキャッシュ（起動後は変更なし前提）
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _promptCache = new();

    private string LoadPromptFromMd(string skillDir, string fileName)
    {
        var cacheKey = $"{skillDir}/{fileName}";

        // キャッシュヒット → ディスクアクセスなしで即時返却
        if (_promptCache.TryGetValue(cacheKey, out var cached))
            return cached;

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

        // キャッシュに保存（以降はメモリから取得）
        _promptCache[cacheKey] = content;
        _logger.LogDebug("プロンプトキャッシュ登録: {Key} ({Chars} chars)", cacheKey, content.Length);
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
    // UI コンポーネント生成（BaseChatService override）
    // ─────────────────────────────────────────────────────────

    protected override List<UiComponent>? BuildComponents(
        string intent,
        List<Dictionary<string, string>>? dataRows,
        string? context)
    {
        var components = new List<UiComponent>();

        // スタッフモードはデータ行がある場合のみカードカルーセルを表示
        if (context == "staff")
        {
            if (dataRows?.Count > 0)
            {
                components.Add(new CardCarousel(
                    Title: $"検索結果（{dataRows.Count}件）",
                    Items: dataRows.Select(r => new CardItem(
                        Id: r.GetValueOrDefault("id", ""),
                        Title: r.GetValueOrDefault("name", r.GetValueOrDefault("vehicle_name",
                               r.GetValueOrDefault("customer_name", r.GetValueOrDefault("title", "")))),
                        Subtitle: BuildStaffCardSubtitle(r, intent),
                        BadgeLabel: r.GetValueOrDefault("status", ""),
                        BadgeStyle: GetBadgeStyle(r.GetValueOrDefault("status", ""))
                    )).ToList()
                ));
            }
            return components.Count > 0 ? components : null;
        }

        // ──────────────────────────────────────────────
        // 意図別コンポーネント生成
        // ──────────────────────────────────────────────

        switch (intent)
        {
            // 車両検索結果 → カルーセル
            case "vehicle_search" or "vehicles" when dataRows?.Count > 0:
            {
                components.Add(new CardCarousel(
                    Title: $"検索結果（{dataRows.Count}件）",
                    Items: dataRows.Select(r => new CardItem(
                        Id: r.GetValueOrDefault("id", ""),
                        Title: r.GetValueOrDefault("name", r.GetValueOrDefault("vehicle_name", "")),
                        Subtitle: $"¥{r.GetValueOrDefault("price", "")}万" +
                                  (r.ContainsKey("year") ? $" · {r["year"]}年" : ""),
                        ImageUrl: r.GetValueOrDefault("image_url", ""),
                        BadgeLabel: r.GetValueOrDefault("status", r.GetValueOrDefault("stock_status", "")),
                        BadgeStyle: GetBadgeStyle(r.GetValueOrDefault("status", "")),
                        Actions: new List<CardAction>
                        {
                            new("詳細", "postback", $"車両ID {r.GetValueOrDefault("id", "")} の詳細を教えて"),
                            new("試乗予約", "postback", $"車両ID {r.GetValueOrDefault("id", "")} を試乗予約したい"),
                        }
                    )).ToList()
                ));
                break;
            }

            // 試乗・来店予約 → 日時ピッカー
            case "appointment_booking" or "test_drive_booking":
            {
                components.Add(new DateTimePicker(
                    Title: "ご希望の日時を選択してください",
                    Mode: "datetime",
                    MinDate: DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
                    MaxDate: DateTime.Today.AddMonths(2).ToString("yyyy-MM-dd")
                ));
                break;
            }

            // 価格帯絞り込み → レンジスライダー
            case "price_filter":
            {
                components.Add(new RangeSlider(
                    Title: "ご予算の範囲を選択してください",
                    Min: 50, Max: 1000, Step: 10, Unit: "万円",
                    SubmitLabel: "この価格帯で探す"
                ));
                break;
            }

            // YES/NO 確認
            case "confirm_booking":
            {
                components.Add(new ConfirmPrompt(
                    Question: "この内容で予約を確定しますか？",
                    ConfirmLabel: "はい、確定します",
                    CancelLabel: "いいえ、変更する"
                ));
                break;
            }

            // 複数選択（メーカー選択等）
            case "brand_selection":
            {
                components.Add(new MultiSelectGroup(
                    Title: "ご希望のメーカーを選択してください（複数可）",
                    Options: new List<SelectOption>
                    {
                        new("トヨタ", "toyota", Icon: "🚗"),
                        new("ホンダ", "honda", Icon: "🚗"),
                        new("日産", "nissan", Icon: "🚗"),
                        new("スバル", "subaru", Icon: "🚗"),
                        new("マツダ", "mazda", Icon: "🚗"),
                        new("三菱", "mitsubishi", Icon: "🚗"),
                    },
                    SubmitLabel: "このメーカーで探す",
                    Min: 1
                ));
                break;
            }

            // 予約一覧
            case "appointments" when dataRows?.Count > 0:
            {
                components.Add(new CardCarousel(
                    Title: $"予約一覧（{dataRows.Count}件）",
                    Items: dataRows.Select(r => new CardItem(
                        Id: r.GetValueOrDefault("id", ""),
                        Title: $"{r.GetValueOrDefault("service_type", "予約")} - {r.GetValueOrDefault("start_time", "")}",
                        Subtitle: r.GetValueOrDefault("vehicle_name", ""),
                        BadgeLabel: r.GetValueOrDefault("status", ""),
                        BadgeStyle: GetBadgeStyle(r.GetValueOrDefault("status", ""))
                    )).ToList()
                ));
                break;
            }

            // 満足度調査
            case "survey":
            {
                components.Add(new RatingWidget(
                    Title: "サービスの満足度をお聞かせください",
                    MaxStars: 5
                ));
                break;
            }

            // ヘルプ
            case "help":
            {
                components.Add(new TextSuggestions(
                    Placeholder: "何でもお気軽にお尋ねください",
                    Suggestions: new List<string>
                    {
                        "在庫車両を探したい",
                        "試乗予約をしたい",
                        "車の下取り査定",
                        "ローン・支払い相談"
                    }
                ));
                break;
            }

            // ──────────────────────────────────────────────
            // 汎用：挨拶・初期メニュー → クイックリプライグループ
            // ──────────────────────────────────────────────
            case "greeting":
            {
                components.Add(new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("在庫車両を探したい", "在庫車両を探したい", Icon: "🚗", Style: "primary"),
                        new("試乗予約をしたい", "試乗予約をしたい", Icon: "📅", Style: "success"),
                        new("車の下取り査定", "車の下取り査定", Icon: "💰"),
                        new("ローン・支払い相談", "ローン・支払い相談", Icon: "🏦"),
                    }
                ));
                break;
            }

            // 車両問い合わせ
            case "vehicle_inquiry":
            {
                components.Add(new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("在庫を確認", "在庫を確認", Icon: "📋", Style: "primary"),
                        new("試乗を予約", "試乗を予約", Icon: "📅", Style: "success"),
                        new("見積もりを依頼", "見積もりを依頼", Icon: "💴"),
                    }
                ));
                break;
            }

            // 見積もり依頼
            case "estimate_request":
            {
                components.Add(new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("ローンで計算", "ローンで計算", Icon: "🏦", Style: "primary"),
                        new("現金購入で計算", "現金購入で計算", Icon: "💵"),
                        new("下取り査定も依頼", "下取り査定も依頼", Icon: "🔄"),
                    }
                ));
                break;
            }

            // サービス予約
            case "service_booking":
            {
                components.Add(new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("予約を変更", "予約を変更", Icon: "✏️"),
                        new("他のサービスを追加", "他のサービスを追加", Icon: "➕"),
                        new("費用の目安を確認", "費用の目安を確認", Icon: "💰"),
                    }
                ));
                break;
            }

            // 下取り相談
            case "trade_inquiry":
            {
                components.Add(new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("査定を依頼", "査定を依頼", Icon: "🔍", Style: "primary"),
                        new("新車への乗り換えを検討", "新車への乗り換えを検討", Icon: "🚗"),
                        new("現金で売却", "現金で売却", Icon: "💵"),
                    }
                ));
                break;
            }

            // 予約変更・キャンセル
            case "appointment":
            {
                components.Add(new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("予約を変更", "予約を変更", Icon: "✏️"),
                        new("キャンセル", "キャンセル", Icon: "❌", Style: "danger"),
                        new("新しい予約", "新しい予約", Icon: "📅", Style: "success"),
                    }
                ));
                break;
            }

            // エスカレーション
            case "escalation":
            {
                components.Add(new QuickReplyGroup(
                    Items: new List<QuickReplyItem>
                    {
                        new("担当者に繋ぐ", "担当者に繋ぐ", Icon: "👤", Style: "primary"),
                        new("折り返し連絡を希望", "折り返し連絡を希望", Icon: "📞"),
                    }
                ));
                break;
            }
        }

        // ──────────────────────────────────────────────
        // 汎用：データ行がある場合、自動的にクイックリプライを追加
        // ──────────────────────────────────────────────
        if (dataRows?.Count > 0 && !components.Any(c => c is CardCarousel))
        {
            // 詳細表を見るなどのリンク付きボタン
            components.Add(new QuickReplyGroup(
                Items: new List<QuickReplyItem>
                {
                    new("一覧でもっと見る", "一覧ページを見たい", Icon: "📋"),
                    new("別の条件で探す", "検索条件を変更したい", Icon: "🔍"),
                }
            ));
        }

        // ──────────────────────────────────────────────
        // フォールバック：その他全てのケースでデフォルトクイックリプライ
        // ──────────────────────────────────────────────
        if (components.Count == 0)
        {
            // デフォルトのクイックリプライを表示
            components.Add(new QuickReplyGroup(
                Items: new List<QuickReplyItem>
                {
                    new("車両を探す", "車両を探す", Icon: "🚗", Style: "primary"),
                    new("試乗予約", "試乗予約", Icon: "📅", Style: "success"),
                    new("お問い合わせ", "お問い合わせ", Icon: "💬"),
                }
            ));
        }

        return components.Count > 0 ? components : null;
    }

    /// <summary>
    /// スタッフ向けカードのサブタイトルをインテントに応じて組み立てる
    /// </summary>
    private static string BuildStaffCardSubtitle(Dictionary<string, string> r, string intent) => intent switch
    {
        "vehicle_search" or "vehicles" =>
            (r.ContainsKey("price") ? $"¥{r["price"]}万" : "")
            + (r.ContainsKey("year") ? $" · {r["year"]}年" : "")
            + (r.ContainsKey("mileage") ? $" · {r["mileage"]}km" : ""),
        "sales_leads" or "leads" =>
            (r.GetValueOrDefault("email", ""))
            + (r.ContainsKey("phone") ? $" · {r["phone"]}" : ""),
        "appointments" =>
            r.GetValueOrDefault("start_time", "")
            + (r.ContainsKey("vehicle_name") ? $" · {r["vehicle_name"]}" : ""),
        _ => string.Join(" · ", r.Where(kv =>
                kv.Key is not "id" and not "name" and not "title"
                    and not "vehicle_name" and not "customer_name" and not "status")
            .Take(2).Select(kv => kv.Value))
    };

    /// <summary>
    /// ステータスに応じたバッジスタイルを返す
    /// </summary>
    private string? GetBadgeStyle(string? status)
    {
        if (string.IsNullOrEmpty(status)) return null;

        return status.ToLower() switch
        {
            "在庫あり" or "available" or "active" => "success",
            "予約済み" or "reserved" or "pending" => "warning",
            "完売" or "sold" or "inactive" => "danger",
            _ => null
        };
    }

    // ─────────────────────────────────────────────────────────
    // クイックリプライ（dealer 固有）
    // ─────────────────────────────────────────────────────────

    private List<string> GetCustomerQuickReplies(string intent) => intent switch
    {
        "greeting" => new()
        {
            "在庫車両を探したい",
            "試乗予約をしたい",
            "車の下取り査定",
            "ローン・支払い相談"
        },
        "vehicle_inquiry" => new List<string> { "在庫を確認", "試乗を予約", "見積もりを依頼" },
        "vehicle_search" => new()
        {
            "価格帯で絞り込む",
            "メーカーで絞り込む",
            "SUVだけ見たい",
            "試乗できる車を見たい"
        },
        "test_drive_booking" => new List<string> { "別の車種に変更", "日時を変更", "キャンセル" },
        "appointment_booking" => new()
        {
            "今週末に予約したい",
            "来週以降で希望を出す",
            "電話で予約する"
        },
        "estimate_request" => new List<string> { "ローンで計算", "現金購入で計算", "下取り査定も依頼" },
        "service_booking" => new List<string> { "予約を変更", "他のサービスを追加", "費用の目安を確認" },
        "trade_inquiry" => new List<string> { "査定を依頼", "新車への乗り換えを検討", "現金で売却" },
        "appointment" => new List<string> { "予約を変更", "キャンセル", "新しい予約" },
        "escalation" => new List<string> { "担当者に繋ぐ", "折り返し連絡を希望" },
        _ => new List<string> { "車両を探す", "試乗予約", "お問い合わせ" }
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

    /// <summary>
    /// 構造化UIコンポーネント
    /// </summary>
    public List<UiComponent>? Components { get; init; }
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
    public string? ComponentsJson { get; init; }
}

public record ConversationSummary
{
    public string ConversationId { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Status { get; init; } = "";
    public string StartedAt { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
}
