using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI.Providers;

namespace NetYamlForge.Services.AI;

/// <summary>
/// ハイブリッド意図分類器（ルールベース + LLM）
/// </summary>
public class HybridIntentClassifier : IIntentClassifier
{
    private readonly ILlmProvider? _llmProvider;
    private readonly IntentRules _rules;
    private readonly ILogger<HybridIntentClassifier> _logger;
    private readonly AiWindowConfig _config;

    public HybridIntentClassifier(
        ILlmProvider? llmProvider,
        IOptions<AiWindowConfig> configOptions,
        ILogger<HybridIntentClassifier> logger)
    {
        _llmProvider = llmProvider;
        _logger = logger;
        _config = configOptions.Value;
        _rules = new IntentRules();
    }

    /// <inheritdoc />
    public async Task<IntentResult> ClassifyAsync(string message, ConversationContext? conversationContext = null, string? projectId = null)
    {
        // 1. LLM 分析（高精度、文脈理解）
        if (_config.Intent.LlmEnabled && _llmProvider != null)
        {
            try
            {
                var llmResult = await ClassifyWithLlmAsync(message, conversationContext);
                if (llmResult.Confidence >= _config.Intent.ConfidenceThreshold)
                {
                    _logger.LogDebug("LLM 分類：{Intent} (置信度：{Confidence})", llmResult.Intent, llmResult.Confidence);
                    return llmResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM 分類に失敗、ルールベースにフォールバック");
            }
        }

        // 2. ルールベースフォールバック（高速・安定）
        var ruleResult = await TryRuleMatchingAsync(message, conversationContext);
        if (ruleResult != null)
        {
            _logger.LogDebug("ルールマッチ：{Intent} (置信度：{Confidence})", ruleResult.Intent, ruleResult.Confidence);
            return ruleResult;
        }

        // 3. フォールバック：デフォルト意図
        return new IntentResult
        {
            Intent = "general_inquiry",
            Confidence = 0.3,
            Method = "fallback",
            NeedsMoreInfo = true,
            RequiredFields = { "詳細な問い合わせ内容" }
        };
    }

    /// <inheritdoc />
    public Task ClearCacheAsync(string? projectId = null)
    {
        // キャッシュクリア（必要に応じて実装）
        return Task.CompletedTask;
    }

    /// <summary>
    /// ルールベースマッチング
    /// </summary>
    private async Task<IntentResult?> TryRuleMatchingAsync(string message, ConversationContext? context)
    {
        var lowerMessage = message.ToLowerInvariant();

        foreach (var rule in _rules.RulesList)
        {
            foreach (var pattern in rule.Patterns)
            {
                if (lowerMessage.Contains(pattern.ToLowerInvariant()))
                {
                    var result = new IntentResult
                    {
                        Intent = rule.Intent,
                        Confidence = rule.DefaultConfidence,
                        Method = "rule",
                        MatchedRuleId = rule.Id,
                        Entities = await ExtractEntitiesAsync(message, rule.Intent),
                        QuickReplies = rule.QuickReplies
                    };

                    // 文脈を考慮して置信度を調整
                    if (context != null && context.CurrentIntent == rule.Intent)
                    {
                        result.Confidence = Math.Min(1.0, result.Confidence + 0.1);
                    }

                    return result;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// LLM を使用した意図分類
    /// </summary>
    private async Task<IntentResult> ClassifyWithLlmAsync(string message, ConversationContext? context)
    {
        if (_llmProvider == null)
            throw new InvalidOperationException("LLM プロバイダーが設定されていません");

        var prompt = BuildClassificationPrompt(message, context);
        var response = await _llmProvider.CompleteAsync(prompt);

        return ParseLlmResponse(response, message);
    }

    /// <summary>
    /// 分類プロンプトを構築
    /// </summary>
    private string BuildClassificationPrompt(string message, ConversationContext? context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("あなたは自動車ディーラーの AI アシスタントです。顧客のメッセージから意図を分類してください。");
        sb.AppendLine();
        sb.AppendLine("【利用可能なインテント】");
        sb.AppendLine("- greeting: 挨拶");
        sb.AppendLine("- hours_inquiry: 営業時間の問い合わせ");
        sb.AppendLine("- price_inquiry: 価格の問い合わせ");
        sb.AppendLine("- estimate_request: 見積もり依頼");
        sb.AppendLine("- appointment_booking: 予約の申し込み");
        sb.AppendLine("- appointment_change: 予約の変更");
        sb.AppendLine("- appointment_cancel: 予約のキャンセル");
        sb.AppendLine("- vehicle_inquiry: 車両の問い合わせ");
        sb.AppendLine("- service_inquiry: サービス内容の問い合わせ");
        sb.AppendLine("- service_booking: サービス・車検予約");
        sb.AppendLine("- trade_inquiry: 下取り・査定の問い合わせ");
        sb.AppendLine("- complaint: 苦情");
        sb.AppendLine("- human_agent: 担当者への接続希望");
        sb.AppendLine("- general_inquiry: その他の問い合わせ");
        sb.AppendLine();

        if (context != null && context.PreviousMessages.Any())
        {
            sb.AppendLine("【会話履歴】");
            foreach (var msg in context.PreviousMessages.TakeLast(5))
            {
                sb.AppendLine($"{msg.Sender}: {msg.Content}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("【顧客メッセージ】");
        sb.AppendLine(message);
        sb.AppendLine();
        sb.AppendLine("以下の JSON 形式で回答してください：");
        sb.AppendLine("{");
        sb.AppendLine("  \"intent\": \"インテント名\",");
        sb.AppendLine("  \"confidence\": 0.0-1.0 の数値，");
        sb.AppendLine("  \"entities\": { \"キー\": \"値\" },");
        sb.AppendLine("  \"sentiment\": -1.0(不満) 〜 1.0(満足) の数値，");
        sb.AppendLine("  \"needs_more_info\": true/false,");
        sb.AppendLine("  \"required_fields\": [\"必要な情報フィールド\"]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// LLM レスポンスをパース
    /// </summary>
    private IntentResult ParseLlmResponse(string response, string originalMessage)
    {
        try
        {
            // JSON 部分を抽出
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<LlmResponseDto>(json);

                if (parsed != null)
                {
                    return new IntentResult
                    {
                        Intent = parsed.Intent ?? "general_inquiry",
                        Confidence = parsed.Confidence,
                        Method = "llm",
                        Entities = parsed.Entities ?? new Dictionary<string, string>(),
                        SentimentScore = parsed.Sentiment,
                        NeedsMoreInfo = parsed.NeedsMoreInfo,
                        RequiredFields = parsed.RequiredFields ?? new List<string>(),
                        SuggestHandover = parsed.Intent == "complaint" || parsed.Intent == "human_agent"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM レスポンスのパースに失敗、デフォルトレスポンスを返します");
        }

        // パース失敗時のデフォルト
        return new IntentResult
        {
            Intent = "general_inquiry",
            Confidence = 0.5,
            Method = "llm",
            NeedsMoreInfo = true
        };
    }

    /// <summary>
    /// エンティティ抽出 - 自動車販売向け拡張版
    /// </summary>
    /// <summary>
    /// AI を使用してメッセージからエンティティを抽出
    /// 正規表現や辞書の代わりに LLM で自然言語理解を行う
    /// </summary>
    private async Task<Dictionary<string, string>> ExtractEntitiesAsync(string message, string intent)
    {
        // LLM が利用可能な場合は AI で抽出
        if (_llmProvider != null && _config.Intent.LlmEnabled)
        {
            try
            {
                var prompt = $@"あなたは情報抽出アシスタントです。以下のメッセージからエンティティを抽出してください。

メッセージ: {message}
インテント: {intent}

以下の JSON 形式で返してください。該当しない値は null にしてください。

{{
  ""vehicle_brand"": ""トヨタ/ホンダ/日産等"",
  ""vehicle_model"": ""カローラ/フィット/ノート等"",
  ""vehicle_type"": ""セダン/SUV/ミニバン/軽自動車等"",
  ""vehicle_condition"": ""新車/中古車"",
  ""vehicle_color"": ""白/黒/銀等"",
  ""preferred_date"": ""明日/2024-03-15/来週等"",
  ""preferred_time"": ""午前10時/午後2時/14:30等"",
  ""preferred_period"": ""今週中/来月末等"",
  ""budget_amount"": ""300万円/50万等"",
  ""budget_type"": ""max/min/range/exact"",
  ""monthly_payment"": ""月々5万円等"",
  ""down_payment"": ""頭金100万円等"",
  ""payment_method"": ""ローン/現金/リース/残価設定"",
  ""service_type"": ""車検/点検/整備/修理/オイル交換/タイヤ交換"",
  ""customer_type"": ""個人/法人"",
  ""vehicle_use"": ""通勤/通学/家族/レジャー/配送"",
  ""is_first_purchase"": ""true/false"",
  ""has_trade_in"": ""true/false"",
  ""current_vehicle"": ""true/false"",
  ""location"": ""東京/大阪/名古屋等""
}}

ルール:
- 日本語、中国語、英語の表現を理解して抽出
- 日付・時間は自然言語表現をそのまま保持
- 金額は「万円」「円」等单位を含めて抽出
- 車種はメーカー名とモデル名の両方を抽出
- 推測ではなく、明確に言及されている情報のみを抽出
- JSON のみ出力し、他の説明は不要です";

                var response = await _llmProvider.CompleteAsync(prompt, System.Threading.CancellationToken.None);

                if (string.IsNullOrWhiteSpace(response))
                    return new Dictionary<string, string>();

                var jsonStart = response.IndexOf('{');
                var jsonEnd = response.LastIndexOf('}');

                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var extracted = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonStr);
                    return extracted ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI エンティティ抽出に失敗しました。フォールバックします");
            }
        }

        // フォールバック：基本的なパターンマッチング（LLM 使用不可時）
        return ExtractEntitiesFallback(message);
    }

    /// <summary>
    /// エンティティ抽出のフォールバック（ルールベース）
    /// </summary>
    private static Dictionary<string, string> ExtractEntitiesFallback(string message)
    {
        var entities = new Dictionary<string, string>();
        var lowerMessage = message.ToLowerInvariant();

        // 日付表現の抽出
        var dateKeywords = new[] { "明日", "今日", "昨日", "来週", "今週", "先週" };
        foreach (var keyword in dateKeywords)
        {
            if (lowerMessage.Contains(keyword))
            {
                entities["preferred_date"] = keyword;
                break;
            }
        }

        // 日付パターン（YYYY-MM-DD）
        var dateMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{4})[-/](\d{1,2})[-/](\d{1,2})");
        if (dateMatch.Success)
        {
            entities["preferred_date"] = dateMatch.Value;
        }

        // 時間表現の抽出
        var timeMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{1,2})時");
        if (timeMatch.Success)
        {
            entities["preferred_time"] = timeMatch.Groups[1].Value + "時";
        }

        return entities;
    }


    /// <summary>
    /// 複数のキーワードのいずれかが含まれているかチェック
    /// </summary>
    private static bool ContainsAny(string message, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (message.Contains(keyword))
                return true;
        }
        return false;
    }

    /// <summary>
    /// LLM レスポンス DTO
    /// </summary>
    private class LlmResponseDto
    {
        public string? Intent { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, string>? Entities { get; set; }
        public double Sentiment { get; set; }
        public bool NeedsMoreInfo { get; set; }
        public List<string>? RequiredFields { get; set; }
    }
}

/// <summary>
/// 意図ルール定義 - 自動車販売向け拡張版
/// </summary>
public class IntentRules
{
    public List<IntentRule> RulesList { get; } = new()
    {
        // ─────────────────────────────────────────────
        // 基本挨拶・営業情報
        // ─────────────────────────────────────────────
        new IntentRule
        {
            Id = "greeting",
            Intent = "greeting",
            Patterns = new[] { "こんにちは", "こんばんは", "おはよう", "hello", "hi", "やあ", "はじめまして", "お世話になります", "よろしくお願いします" },
            DefaultConfidence = 0.95,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "営業時間を聞く", ActionType = "postback", ActionValue = "hours_inquiry" },
                new() { Label = "予約を申し込む", ActionType = "postback", ActionValue = "appointment_booking" }
            }
        },
        new IntentRule
        {
            Id = "hours_inquiry",
            Intent = "hours_inquiry",
            Patterns = new[] { "営業時間", "何時まで", "何時から", "営業日", "休日", "定休日", "やってる", "開いてる", "閉まってる", "年末年始", "GW", "お盆" },
            DefaultConfidence = 0.9,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "予約を申し込む", ActionType = "postback", ActionValue = "appointment_booking" }
            }
        },
        new IntentRule
        {
            Id = "location_inquiry",
            Intent = "location_inquiry",
            Patterns = new[] { "住所", "場所", "行き方", "アクセス", "地図", "最寄り駅", "駐車場", "どこにある", "道順", "ナビ" },
            DefaultConfidence = 0.9,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "地図を見る", ActionType = "link", ActionValue = "/map" }
            }
        },

        // ─────────────────────────────────────────────
        // 車両関連（詳細な意図分類）
        // ─────────────────────────────────────────────
        // ⚠️ 重要: test_drive_booking は vehicle_inquiry より先に定義する
        // 「試乗」キーワードが両方のルールにある場合、先に定義された方が優先される
        new IntentRule
        {
            Id = "test_drive_booking",
            Intent = "test_drive_booking",
            Patterns = new[] { "試乗", "テストドライブ", "運転してみたい", "乗り心地", "実際に乗る", "実際に乗ってみたい", "試乗予約", "試乗したい" },
            DefaultConfidence = 0.9,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "試乗予約する", ActionType = "link", ActionValue = "/appointments/new?type=test_drive" }
            }
        },
        new IntentRule
        {
            Id = "service_booking",
            Intent = "service_booking",
            Patterns = new[] { "車検", "点検", "オイル交換", "タイヤ", "修理", "整備", "板金", "サービス予約", "メンテナンス", "故障", "部品交換" },
            DefaultConfidence = 0.88
        },
        new IntentRule
        {
            Id = "estimate_request",
            Intent = "estimate_request",
            Patterns = new[] { "見積もり", "見積", "価格を知りたい", "いくら", "費用", "金額", "予算", "価格表", "値段", "ローン", "月々" },
            DefaultConfidence = 0.85
        },
        new IntentRule
        {
            Id = "trade_inquiry",
            Intent = "trade_inquiry",
            Patterns = new[] { "下取り", "買取", "売却", "乗り換え", "査定", "いくらで売れる", "廃車" },
            DefaultConfidence = 0.85,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "無料査定を予約", ActionType = "link", ActionValue = "/appointments/new?type=appraisal" }
            }
        },
        new IntentRule
        {
            Id = "vehicle_inquiry",
            Intent = "vehicle_inquiry",
            Patterns = new[] { "車種", "車両", "在庫", "納期", "カタログ", "車について", "クルマ", "自動車", "新車", "中古車" },
            DefaultConfidence = 0.8,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "在庫を見る", ActionType = "link", ActionValue = "/vehicles" },
                new() { Label = "試乗を予約", ActionType = "postback", ActionValue = "test_drive_booking" }
            }
        },
        new IntentRule
        {
            Id = "vehicle_comparison",
            Intent = "vehicle_comparison",
            Patterns = new[] { "どちらがいい", "どっち", "比較", "違い", "比べて", "迷ってる", "おすすめは" },
            DefaultConfidence = 0.75
        },
        new IntentRule
        {
            Id = "vehicle_availability",
            Intent = "vehicle_availability",
            Patterns = new[] { "在庫ありますか", "あります", "納車", "いつ頃", "待ち", "即納", "展示車" },
            DefaultConfidence = 0.85
        },

        // ─────────────────────────────────────────────
        // 価格・見積もり・支払い
        // ─────────────────────────────────────────────
        new IntentRule
        {
            Id = "price_inquiry",
            Intent = "price_inquiry",
            Patterns = new[] { "価格", "値段", "いくら", "費用", "料金", "見積もり", "値引き", "割引", "値上げ", "値下げ", "予算", "総額", "本体価格", "車両価格" },
            DefaultConfidence = 0.85,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "見積もり依頼", ActionType = "link", ActionValue = "/estimate/new" }
            }
        },
        new IntentRule
        {
            Id = "finance_inquiry",
            Intent = "finance_inquiry",
            Patterns = new[] { "ローン", "金利", "分割", "頭金", "月々", "ボーナス払い", "残価設定", "リース", "ファイナンス" },
            DefaultConfidence = 0.85,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "ローンシミュレーション", ActionType = "link", ActionValue = "/finance/simulator" }
            }
        },
        new IntentRule
        {
            Id = "option_inquiry",
            Intent = "option_inquiry",
            Patterns = new[] { "オプション", "装着", "装備", "メーカーオプション", "ディーラーオプション", "ナビ", "ドラレコ", "ETC" },
            DefaultConfidence = 0.8
        },

        // ─────────────────────────────────────────────
        // 予約関連
        // ─────────────────────────────────────────────
        new IntentRule
        {
            Id = "appointment_booking",
            Intent = "appointment_booking",
            Patterns = new[] { "予約", "申し込む", "予約したい", "お願いしたい", "スケジュール", "都合", "空いてる", "予約取る" },
            DefaultConfidence = 0.85,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "明日の午前中", ActionType = "postback", ActionValue = "tomorrow_morning" },
                new() { Label = "明日の午後", ActionType = "postback", ActionValue = "tomorrow_afternoon" }
            }
        },
        new IntentRule
        {
            Id = "appointment_change",
            Intent = "appointment_change",
            Patterns = new[] { "変更", "変えたい", "スケジュール変更", "日時変更", "延期", "早めたい", "遅くしたい" },
            DefaultConfidence = 0.85
        },
        new IntentRule
        {
            Id = "appointment_cancel",
            Intent = "appointment_cancel",
            Patterns = new[] { "キャンセル", "取り消し", "やめたい", "中止", "予約をキャンセル" },
            DefaultConfidence = 0.9
        },
        new IntentRule
        {
            Id = "appointment_status",
            Intent = "appointment_status",
            Patterns = new[] { "予約確認", "予約状況", "予約してる", "予約してるか", "予約内容" },
            DefaultConfidence = 0.85
        },

        // ─────────────────────────────────────────────
        // サービス・メンテナンス
        // ─────────────────────────────────────────────
        new IntentRule
        {
            Id = "service_inquiry",
            Intent = "service_inquiry",
            Patterns = new[] { "車検", "点検", "整備", "メンテナンス", "オイル交換", "タイヤ交換", "修理", "故障", "異常", "警告灯" },
            DefaultConfidence = 0.85,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "車検予約", ActionType = "link", ActionValue = "/appointments/new?type=inspection" },
                new() { Label = "定期点検", ActionType = "link", ActionValue = "/appointments/new?type=maintenance" }
            }
        },
        new IntentRule
        {
            Id = "parts_inquiry",
            Intent = "parts_inquiry",
            Patterns = new[] { "パーツ", "部品", "用品", "アクセサリー", "タイヤ", "バッテリー", "純正部品" },
            DefaultConfidence = 0.8
        },
        new IntentRule
        {
            Id = "warranty_inquiry",
            Intent = "warranty_inquiry",
            Patterns = new[] { "保証", " warranty", "無償", "有償", "期間", "距離制限", "延長保証" },
            DefaultConfidence = 0.8
        },

        // ─────────────────────────────────────────────
        // 顧客サポート・苦情
        // ─────────────────────────────────────────────
        new IntentRule
        {
            Id = "complaint",
            Intent = "complaint",
            Patterns = new[] { "苦情", "不満", "ひどい", "最悪", "抗議", "文句", "困ってる", "問題", "トラブル", "納得いかない", "おかしい" },
            DefaultConfidence = 0.8,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "担当者に接続", ActionType = "postback", ActionValue = "human_agent" }
            }
        },
        new IntentRule
        {
            Id = "human_agent",
            Intent = "human_agent",
            Patterns = new[] { "担当者", "オペレーター", "人", "人間", "カスタマーサービス", "スタッフ", "社員", "電話", "直接話したい" },
            DefaultConfidence = 0.9
        },
        new IntentRule
        {
            Id = "callback_request",
            Intent = "callback_request",
            Patterns = new[] { "折り返し", "連絡ください", "電話ください", "回線してください", "こちらから連絡" },
            DefaultConfidence = 0.85
        },

        // ─────────────────────────────────────────────
        // 購入プロセス
        // ─────────────────────────────────────────────
        new IntentRule
        {
            Id = "purchase_intent",
            Intent = "purchase_intent",
            Patterns = new[] { "買いたい", "購入", "契約", "注文", "申し込み", "決めたい", "即決" },
            DefaultConfidence = 0.9,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "見積もり依頼", ActionType = "link", ActionValue = "/estimate/new" },
                new() { Label = "契約手続き", ActionType = "link", ActionValue = "/contracts/new" }
            }
        },
        new IntentRule
        {
            Id = "contract_status",
            Intent = "contract_status",
            Patterns = new[] { "契約状況", "進捗", "いつ頃", "納車日", "交付", "車検証" },
            DefaultConfidence = 0.8
        },

        // ─────────────────────────────────────────────
        // その他
        // ─────────────────────────────────────────────
        new IntentRule
        {
            Id = "campaign_inquiry",
            Intent = "campaign_inquiry",
            Patterns = new[] { "キャンペーン", "特典", "プレゼント", "値引きキャンペーン", "フェア", "イベント", "限定" },
            DefaultConfidence = 0.85
        },
        new IntentRule
        {
            Id = "document_request",
            Intent = "document_request",
            Patterns = new[] { "資料", "パンフレット", "カタログ", "送って", "郵送", "請求" },
            DefaultConfidence = 0.85
        },
        new IntentRule
        {
            Id = "thank_you",
            Intent = "thank_you",
            Patterns = new[] { "ありがとう", "感謝", "助かった", "よかった", "分かりました", "了解" },
            DefaultConfidence = 0.95
        },
        new IntentRule
        {
            Id = "goodbye",
            Intent = "goodbye",
            Patterns = new[] { "さようなら", "じゃあね", "また", "失礼します", "バイバイ", "終わる" },
            DefaultConfidence = 0.95
        }
    };
}

/// <summary>
/// 意図ルール
/// </summary>
public class IntentRule
{
    public string Id { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string[] Patterns { get; set; } = Array.Empty<string>();
    public double DefaultConfidence { get; set; }
    public List<QuickReplyButton> QuickReplies { get; set; } = new();
}
