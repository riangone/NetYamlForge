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
    /// エンティティ抽出
    /// </summary>
    private Dictionary<string, string> ExtractEntities(string message, string intent)
    {
        var entities = new Dictionary<string, string>();

        // 日付抽出
        var datePatterns = new[] { "明日", "今日", "来週", "来月", "○月○日", "\\d{1,2}月\\d{1,2}日", "\\d{4}年\\d{1,2}月\\d{1,2}日" };
        foreach (var pattern in datePatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, pattern);
            if (match.Success)
            {
                entities["preferred_date"] = match.Value;
                break;
            }
        }

        // 時間抽出
        var timePatterns = new[] { "\\d{1,2}時", "\\d{1,2}:\\d{2}", "午前", "午後" };
        foreach (var pattern in timePatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, pattern);
            if (match.Success)
            {
                entities["preferred_time"] = match.Value;
                break;
            }
        }

        // 車種抽出
        if (intent.Contains("vehicle") || intent.Contains("appointment"))
        {
            var carModels = new[] { "カローラ", "クラウン", "プリウス", "RAV4", "ヤリス", "ハリアー" };
            foreach (var model in carModels)
            {
                if (message.Contains(model))
                {
                    entities["vehicle_model"] = model;
                    break;
                }
            }
        }

        return entities;
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
/// 意図ルール定義
/// </summary>
public class IntentRules
{
    public List<IntentRule> RulesList { get; } = new()
    {
        new IntentRule
        {
            Id = "greeting",
            Intent = "greeting",
            Patterns = new[] { "こんにちは", "こんばんは", "おはよう", "hello", "hi", "やあ" },
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
            Patterns = new[] { "営業時間", "何時まで", "何時から", "営業日", "休日", "定休日" },
            DefaultConfidence = 0.9,
            QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "予約を申し込む", ActionType = "postback", ActionValue = "appointment_booking" }
            }
        },
        new IntentRule
        {
            Id = "price_inquiry",
            Intent = "price_inquiry",
            Patterns = new[] { "価格", "値段", "いくら", "費用", "料金", "見積もり" },
            DefaultConfidence = 0.85
        },
        new IntentRule
        {
            Id = "appointment_booking",
            Intent = "appointment_booking",
            Patterns = new[] { "予約", "申し込む", "予約したい", "お願いしたい", "スケジュール" },
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
            Patterns = new[] { "変更", "変えたい", "スケジュール変更", "日時変更" },
            DefaultConfidence = 0.85
        },
        new IntentRule
        {
            Id = "appointment_cancel",
            Intent = "appointment_cancel",
            Patterns = new[] { "キャンセル", "取り消し", "やめたい" },
            DefaultConfidence = 0.9
        },
        new IntentRule
        {
            Id = "vehicle_inquiry",
            Intent = "vehicle_inquiry",
            Patterns = new[] { "車種", "車両", "在庫", "納期", "試乗", "カタログ" },
            DefaultConfidence = 0.8
        },
        new IntentRule
        {
            Id = "complaint",
            Intent = "complaint",
            Patterns = new[] { "苦情", "不満", "ひどい", "最悪", "抗議", "文句" },
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
            Patterns = new[] { "担当者", "オペレーター", "人", "人間", "カスタマーサービス" },
            DefaultConfidence = 0.9
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
