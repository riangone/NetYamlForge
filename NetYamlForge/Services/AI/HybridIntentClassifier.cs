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
        // 1. ルールベースマッチング（高速）
        var ruleResult = TryRuleMatching(message, conversationContext);
        if (ruleResult != null && ruleResult.Confidence >= _config.Intent.ConfidenceThreshold)
        {
            _logger.LogDebug("ルールマッチ：{Intent} (置信度：{Confidence})", ruleResult.Intent, ruleResult.Confidence);
            return ruleResult;
        }

        // 2. LLM 分析（高精度）
        if (_config.Intent.LlmEnabled && _llmProvider != null)
        {
            try
            {
                var llmResult = await ClassifyWithLlmAsync(message, conversationContext);
                _logger.LogDebug("LLM 分類：{Intent} (置信度：{Confidence})", llmResult.Intent, llmResult.Confidence);
                return llmResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM 分類に失敗、ルールベースにフォールバック");
            }
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
    private IntentResult? TryRuleMatching(string message, ConversationContext? context)
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
                        Entities = ExtractEntities(message, rule.Intent),
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
        sb.AppendLine("- appointment_booking: 予約の申し込み");
        sb.AppendLine("- appointment_change: 予約の変更");
        sb.AppendLine("- appointment_cancel: 予約のキャンセル");
        sb.AppendLine("- vehicle_inquiry: 車両の問い合わせ");
        sb.AppendLine("- service_inquiry: サービス内容の問い合わせ");
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
    private Dictionary<string, string> ExtractEntities(string message, string intent)
    {
        var entities = new Dictionary<string, string>();
        var lowerMessage = message.ToLowerInvariant();

        // ─────────────────────────────────────────────
        // 日付・時間抽出
        // ─────────────────────────────────────────────
        var datePatterns = new[] { "明日", "今日", "来週", "来月", "再来週", "再来月", "先週", "先月", "○月○日", "\\d{1,2}月\\d{1,2}日", "\\d{4}年\\d{1,2}月\\d{1,2}日" };
        foreach (var pattern in datePatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, pattern);
            if (match.Success)
            {
                entities["preferred_date"] = match.Value;
                break;
            }
        }

        // 時間抽出（「10 時」「14:30」「午後 3 時」など）
        var timePatterns = new[] { "\\d{1,2}時", "\\d{1,2}:\\d{2}", "\\d{1,2}時\\d{1,2}分", "午前\\d{1,2}時", "午後\\d{1,2}時", "朝", "昼", "夜", "午前", "午後" };
        foreach (var pattern in timePatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, pattern);
            if (match.Success)
            {
                entities["preferred_time"] = match.Value;
                break;
            }
        }

        // 期間（「今週中」「来週末」「3 月まで」など）
        var periodPatterns = new[] { "今週中", "来週中", "今月中", "来月中", "今週末", "来週末", "\\d{1,2}月まで", "\\d{1,2}月中" };
        foreach (var pattern in periodPatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, pattern);
            if (match.Success)
            {
                entities["preferred_period"] = match.Value;
                break;
            }
        }

        // ─────────────────────────────────────────────
        // 車両関連エンティティ
        // ─────────────────────────────────────────────
        // 車種抽出（日本市場の主要車種）
        var carBrands = new Dictionary<string, string[]>
        {
            ["トヨタ"] = ["カローラ", "クラウン", "プリウス", "RAV4", "ヤリス", "ハリアー", "アルファード", "ヴェルファイア", "ランドクルーザー", "ハイラックス", "C-HR", "アクア", "シエンタ", "ノア", "ヴォクシー", "エスティマ", "カムリ", "86", "スープラ", "ミライ"],
            ["ホンダ"] = ["フィット", "アクア", "シビック", "アコード", "CR-V", "ヴェゼル", "N-BOX", "N-VAN", "オデッセイ", "ステップワゴン", "フリード", "インサイト"],
            ["日産"] = ["ノート", "デイズ", "ルークス", "エクストレイル", "セレナ", "エルグランド", "GT-R", "フェアレディ Z", "リーフ", "サクラ", "アリア"],
            ["マツダ"] = ["アクセラ", "アテンザ", "CX-5", "CX-30", "ロードスター", "デミオ", "CX-3", "CX-8", "MX-30"],
            ["スバル"] = ["インプレッサ", "レヴォーグ", "アウトバック", "フォレスター", "XV", "BRZ", "レガシィ"],
            ["スズキ"] = ["スイフト", "アルト", "ワゴン R", "スペーシア", "ジムニー", "ハスラー", "ソリオ", "エスクード"],
            ["ダイハツ"] = ["ミライース", "タント", "ムーヴ", "ウェイク", "キャスト", "ロッキー", "タフト"],
            ["BMW"] = ["3 シリーズ", "5 シリーズ", "X1", "X3", "X5", "i3", "iX"],
            ["メルセデス"] = ["C クラス", "E クラス", "S クラス", "GLC", "GLE", "EQC"],
            ["アウディ"] = ["A3", "A4", "A6", "Q3", "Q5", "Q7", "e-tron"],
            ["フォルクスワーゲン"] = ["ゴルフ", "ポロ", "T-ROC", "トゥアレグ", "ID.4"]
        };

        foreach (var brand in carBrands)
        {
            if (message.Contains(brand.Key))
            {
                entities["vehicle_brand"] = brand.Key;
            }
            foreach (var model in brand.Value)
            {
                if (message.Contains(model))
                {
                    entities["vehicle_model"] = model;
                    entities["vehicle_brand"] = brand.Key;
                    break;
                }
            }
        }

        // 車両タイプ
        var vehicleTypes = new Dictionary<string, string[]>
        {
            ["セダン"] = ["セダン"],
            ["SUV"] = ["SUV", "クロスオーバー", "クロカン"],
            ["ミニバン"] = ["ミニバン", "ワンボックス"],
            ["ワゴン"] = ["ワゴン", "ステーションワゴン"],
            ["ハッチバック"] = ["ハッチバック", "5 ドア"],
            ["クーペ"] = ["クーペ", "2 ドア"],
            ["オープンカー"] = ["オープンカー", "カブリオレ", "ロードスター"],
            ["軽自動車"] = ["軽", "軽自動車", "K カー"],
            ["トラック"] = ["トラック", "トレーラー"],
            ["バン"] = ["バン", "コミューター"]
        };

        foreach (var type in vehicleTypes)
        {
            foreach (var keyword in type.Value)
            {
                if (message.Contains(keyword))
                {
                    entities["vehicle_type"] = type.Key;
                    break;
                }
            }
        }

        // 車両状態
        if (ContainsAny(lowerMessage, "新車", "ニューカー", "new"))
            entities["vehicle_condition"] = "new";
        else if (ContainsAny(lowerMessage, "中古", "USED", "未使用", "展示車", "試乗車"))
            entities["vehicle_condition"] = "used";

        // 車両カラー
        var colors = new[] { "白", "黒", "銀", "グレー", "赤", "青", "緑", "黄", "橙", "茶", "紫", "ピンク", "ゴールド", "パール", "メタリック", "マット" };
        foreach (var color in colors)
        {
            if (message.Contains(color))
            {
                entities["vehicle_color"] = color;
                break;
            }
        }

        // ─────────────────────────────────────────────
        // 価格・予算エンティティ
        // ─────────────────────────────────────────────
        // 予算範囲抽出（「300 万」「50 万円以内」「100 万〜200 万」など）
        var budgetPattern = @"(\d{1,3}(?:,\d{3})*|\d{1,8})\s*(万|万円|円|MAN|まん)";
        var budgetMatch = System.Text.RegularExpressions.Regex.Match(message, budgetPattern);
        if (budgetMatch.Success)
        {
            entities["budget_amount"] = budgetMatch.Value;
            
            // 予算上限か
            if (message.Contains("以内") || message.Contains("以下") || message.Contains("max") || message.Contains("MAX"))
                entities["budget_type"] = "max";
            // 予算下限か
            else if (message.Contains("以上") || message.Contains("min") || message.Contains("MIN"))
                entities["budget_type"] = "min";
            // 予算範囲か（「〜」や「-」で接続）
            else if (message.Contains("〜") || message.Contains("-") || message.Contains("から") || message.Contains("まで"))
                entities["budget_type"] = "range";
            else
                entities["budget_type"] = "exact";
        }

        // 頭金
        var downPaymentPattern = @"頭金\s*(\d{1,3}(?:,\d{3})*|\d{1,8})\s*(万|万円|円)";
        var downPaymentMatch = System.Text.RegularExpressions.Regex.Match(message, downPaymentPattern);
        if (downPaymentMatch.Success)
            entities["down_payment"] = downPaymentMatch.Value;

        // 月々支払い
        var monthlyPattern = @"月々\s*(\d{1,3}(?:,\d{3})*|\d{1,8})\s*(万|万円|円)|月額|毎月";
        var monthlyMatch = System.Text.RegularExpressions.Regex.Match(message, monthlyPattern);
        if (monthlyMatch.Success)
            entities["monthly_payment"] = monthlyMatch.Value;

        // ─────────────────────────────────────────────
        // 支払い方法エンティティ
        // ─────────────────────────────────────────────
        if (ContainsAny(lowerMessage, "ローン", "分割", "クレジット"))
            entities["payment_method"] = "loan";
        else if (ContainsAny(lowerMessage, "現金", "一括", "全額"))
            entities["payment_method"] = "cash";
        else if (ContainsAny(lowerMessage, "リース", "サブスク"))
            entities["payment_method"] = "lease";
        else if (ContainsAny(lowerMessage, "残価設定"))
            entities["payment_method"] = "residual";

        // ─────────────────────────────────────────────
        // 下取り・買取エンティティ
        // ─────────────────────────────────────────────
        if (ContainsAny(message, "下取り", "乗り換え", "trade"))
            entities["has_trade_in"] = "true";
        
        // 現在の車両
        var currentCarPatterns = new[] { "現在.*乗ってる", "今の車", "愛車", "乗り換え" };
        foreach (var pattern in currentCarPatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, pattern);
            if (match.Success)
                entities["current_vehicle"] = "true";
        }

        // ─────────────────────────────────────────────
        // 顧客属性エンティティ
        // ─────────────────────────────────────────────
        // 初回購入か
        if (ContainsAny(message, "初めて", "初回", "初めての車", "初心者", "ペーパードライバー"))
            entities["is_first_purchase"] = "true";

        // 法人か
        if (ContainsAny(message, "法人", "会社", "業務用", "仕事用", "商用"))
            entities["customer_type"] = "business";
        else if (ContainsAny(message, "個人", "自宅用", "通勤", "通学", "家族"))
            entities["customer_type"] = "personal";

        // 用途
        var useCases = new Dictionary<string, string[]>
        {
            ["通勤"] = ["通勤", "通勤", "仕事用", "ビジネス"],
            ["通学"] = ["通学", "学校", "大学", "高校"],
            ["家族"] = ["家族", "子供", "子ども", "育児"],
            ["レジャー"] = ["レジャー", "旅行", "キャンプ", "スキー", "ゴルフ"],
            ["送货"] = ["配送", "配達", "運送", "荷物"]
        };

        foreach (var useCase in useCases)
        {
            if (ContainsAny(message, useCase.Value))
            {
                entities["vehicle_use"] = useCase.Key;
                break;
            }
        }

        // ─────────────────────────────────────────────
        // 地域・店舗エンティティ
        // ─────────────────────────────────────────────
        var prefectures = new[] { "東京", "神奈川", "埼玉", "千葉", "大阪", "京都", "兵庫", "愛知", "名古屋", "福岡", "北海道", "沖縄" };
        foreach (var pref in prefectures)
        {
            if (message.Contains(pref))
            {
                entities["location"] = pref;
                break;
            }
        }

        // ─────────────────────────────────────────────
        // サービス类型エンティティ
        // ─────────────────────────────────────────────
        var serviceTypes = new Dictionary<string, string[]>
        {
            ["車検"] = ["車検", " shaken"],
            ["点検"] = ["点検", "検査"],
            ["整備"] = ["整備", "メンテナンス"],
            ["修理"] = ["修理", "修復", "直す"],
            ["オイル交換"] = ["オイル交換", "エンジンオイル"],
            ["タイヤ交換"] = ["タイヤ交換", "タイヤ"]
        };

        foreach (var service in serviceTypes)
        {
            if (ContainsAny(message, service.Value))
            {
                entities["service_type"] = service.Key;
                break;
            }
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
        new IntentRule
        {
            Id = "vehicle_inquiry",
            Intent = "vehicle_inquiry",
            Patterns = new[] { "車種", "車両", "在庫", "納期", "試乗", "カタログ", "車について", "クルマ", "自動車", "新車", "中古車" },
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
        new IntentRule
        {
            Id = "test_drive_booking",
            Intent = "test_drive_booking",
            Patterns = new[] { "試乗", "テストドライブ", "運転してみたい", "乗り心地", "実際に乗る" },
            DefaultConfidence = 0.9,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "試乗予約する", ActionType = "link", ActionValue = "/appointments/new?type=test_drive" }
            }
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
