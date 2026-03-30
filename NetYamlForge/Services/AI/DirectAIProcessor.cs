using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI.Providers;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 直接 AI 処理サービス - 意図分類を介さずに AI が直接対話を処理
/// </summary>
public interface IDirectAIProcessor
{
    /// <summary>
    /// ユーザーメッセージを直接処理して応答を生成
    /// </summary>
    Task<AIProcessingResult> ProcessAsync(string message, ConversationContext? context = null, string? projectId = null);

    /// <summary>
    /// バッチ処理（複数メッセージ）
    /// </summary>
    Task<List<AIProcessingResult>> ProcessBatchAsync(List<string> messages, string? projectId = null);
}

/// <summary>
/// AI 処理結果
/// </summary>
public class AIProcessingResult
{
    /// <summary>応答メッセージ</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>抽出されたエンティティ</summary>
    public Dictionary<string, string> Entities { get; set; } = new();

    /// <summary>感情スコア (-1.0 〜 1.0)</summary>
    public double SentimentScore { get; set; }

    /// <summary>感情ラベル</summary>
    public string SentimentLabel { get; set; } = "neutral";

    /// <summary>担当者エスカレーションが必要か</summary>
    public bool NeedsHandover { get; set; }

    /// <summary>エスカレーション理由</summary>
    public string? HandoverReason { get; set; }

    /// <summary>優先度</summary>
    public string Priority { get; set; } = "normal";

    /// <summary>対象部門</summary>
    public string? TargetDepartment { get; set; }

    /// <summary>クイック返信ボタン</summary>
    public List<QuickReplyButton> QuickReplies { get; set; } = new();

    /// <summary>追加情報が必要か</summary>
    public bool NeedsMoreInfo { get; set; }

    /// <summary>必要な情報フィールド</summary>
    public List<string> RequiredFields { get; set; } = new();

    /// <summary>使用した AI モデル</summary>
    public string AiModel { get; set; } = "unknown";

    /// <summary>処理メソッド</summary>
    public string Method { get; set; } = "direct-ai";
}

/// <summary>
/// 直接 AI 処理サービスの実装
/// </summary>
public class DirectAIProcessor : IDirectAIProcessor
{
    private readonly ILlmProvider? _llmProvider;
    private readonly AiWindowConfig _config;
    private readonly ILogger<DirectAIProcessor> _logger;
    private readonly ISentimentAnalyzer? _sentimentAnalyzer;

    public DirectAIProcessor(
        ILlmProvider? llmProvider,
        IOptions<AiWindowConfig> configOptions,
        ILogger<DirectAIProcessor> logger,
        ISentimentAnalyzer? sentimentAnalyzer = null)
    {
        _llmProvider = llmProvider;
        _config = configOptions.Value;
        _logger = logger;
        _sentimentAnalyzer = sentimentAnalyzer;
    }

    /// <inheritdoc />
    public async Task<AIProcessingResult> ProcessAsync(string message, ConversationContext? context = null, string? projectId = null)
    {
        _logger.LogDebug("直接 AI 処理開始：{MessageLength} 文字", message.Length);

        // 1. 感情分析（利用可能な場合）
        double sentimentScore = 0;
        string sentimentLabel = "neutral";

        if (_sentimentAnalyzer != null)
        {
            var sentiment = await _sentimentAnalyzer.AnalyzeAsync(message);
            sentimentScore = sentiment.Score;
            sentimentLabel = sentiment.Label;
        }

        // 2. AI による直接処理
        if (_config.Intent.LlmEnabled && _llmProvider != null)
        {
            try
            {
                return await ProcessWithLlmAsync(message, context, sentimentScore, sentimentLabel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM 処理に失敗、フォールバック応答を返します");
            }
        }

        // 3. フォールバック：シンプルなルールベース応答
        return CreateFallbackResponse(message, sentimentScore, sentimentLabel);
    }

    /// <inheritdoc />
    public async Task<List<AIProcessingResult>> ProcessBatchAsync(List<string> messages, string? projectId = null)
    {
        var results = new List<AIProcessingResult>();

        foreach (var message in messages)
        {
            var result = await ProcessAsync(message, null, projectId);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// LLM を使用した直接処理
    /// </summary>
    private async Task<AIProcessingResult> ProcessWithLlmAsync(
        string message,
        ConversationContext? context,
        double sentimentScore,
        string sentimentLabel)
    {
        if (_llmProvider == null)
            throw new InvalidOperationException("LLM プロバイダーが設定されていません");

        var prompt = BuildDirectProcessingPrompt(message, context, sentimentScore, sentimentLabel);
        var response = await _llmProvider.CompleteAsync(prompt);

        return ParseLlmResponse(response, message, sentimentScore, sentimentLabel);
    }

    /// <summary>
    /// 直接処理プロンプトを構築
    /// </summary>
    private string BuildDirectProcessingPrompt(
        string message,
        ConversationContext? context,
        double sentimentScore,
        string sentimentLabel)
    {
        var sb = new StringBuilder();

        sb.AppendLine("あなたは自動車ディーラーの AI アシスタントです。顧客のメッセージに対して、以下のステップで直接応答を生成してください：");
        sb.AppendLine();
        sb.AppendLine("【処理ステップ】");
        sb.AppendLine("1. 顧客の意図と要望を理解する");
        sb.AppendLine("2. 必要な情報を抽出する（日付、時間、車種、予算など）");
        sb.AppendLine("3. 適切な応答メッセージを生成する");
        sb.AppendLine("4. 担当者へのエスカレーションが必要か判断する");
        sb.AppendLine("5. クイック返信ボタンを提案する（必要な場合）");
        sb.AppendLine();
        sb.AppendLine("【エスカレーション基準】");
        sb.AppendLine("- 顧客が明らかに不満を持っている（感情スコアが -0.5 未満）");
        sb.AppendLine("- 複雑な問い合わせで AI の回答が不十分な場合");
        sb.AppendLine("- 顧客が「担当者」と明言した場合");
        sb.AppendLine("- 苦情、クレームの場合");
        sb.AppendLine("- 緊急の対応が必要な場合");
        sb.AppendLine();
        sb.AppendLine("【応答のトーン】");
        sb.AppendLine("- 丁寧で親しみやすい口調");
        sb.AppendLine("- 顧客の感情に配慮（不満の場合は謝罪から入る）");
        sb.AppendLine("- 具体的で分かりやすい説明");
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

        sb.AppendLine("【顧客の感情】");
        sb.AppendLine($"スコア：{sentimentScore:F2} (-1.0=非常に不満，0=中立，1.0=非常に満足)");
        sb.AppendLine($"ラベル：{sentimentLabel}");
        sb.AppendLine();

        sb.AppendLine("【顧客メッセージ】");
        sb.AppendLine(message);
        sb.AppendLine();
        sb.AppendLine("以下の JSON 形式で回答してください：");
        sb.AppendLine("{");
        sb.AppendLine("  \"message\": \"顧客への応答メッセージ\",");
        sb.AppendLine("  \"entities\": { \"抽出されたエンティティ\": \"値\" },");
        sb.AppendLine("  \"needs_handover\": true/false,");
        sb.AppendLine("  \"handover_reason\": \"エスカレーション理由（必要な場合）\",");
        sb.AppendLine("  \"priority\": \"low|normal|high|urgent\",");
        sb.AppendLine("  \"target_department\": \"sales|service|parts|general\"（必要な場合）,");
        sb.AppendLine("  \"quick_replies\": [{\"label\": \"ボタン表示名\", \"action_type\": \"postback|link\", \"action_value\": \"値\"}],");
        sb.AppendLine("  \"needs_more_info\": true/false,");
        sb.AppendLine("  \"required_fields\": [\"必要な情報フィールド名\"]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// LLM レスポンスをパース
    /// </summary>
    private AIProcessingResult ParseLlmResponse(string response, string originalMessage, double sentimentScore, string sentimentLabel)
    {
        try
        {
            // JSON 部分を抽出
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<LlmDirectResponseDto>(json);

                if (parsed != null)
                {
                    return new AIProcessingResult
                    {
                        Message = parsed.Message ?? "申し訳ございませんが、もう一度詳しくお聞かせください。",
                        Entities = parsed.Entities ?? new Dictionary<string, string>(),
                        SentimentScore = sentimentScore,
                        SentimentLabel = sentimentLabel,
                        NeedsHandover = parsed.NeedsHandover,
                        HandoverReason = parsed.HandoverReason,
                        Priority = parsed.Priority ?? "normal",
                        TargetDepartment = parsed.TargetDepartment,
                        QuickReplies = parsed.QuickReplies ?? new List<QuickReplyButton>(),
                        NeedsMoreInfo = parsed.NeedsMoreInfo,
                        RequiredFields = parsed.RequiredFields ?? new List<string>(),
                        AiModel = _config.Llm.Model,
                        Method = "direct-ai-llm"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM レスポンスのパースに失敗");
        }

        // パース失敗時のデフォルト
        return new AIProcessingResult
        {
            Message = "お問い合わせいただき、ありがとうございます。どのようなご用件でしょうか？",
            SentimentScore = sentimentScore,
            SentimentLabel = sentimentLabel,
            NeedsHandover = false,
            AiModel = _config.Llm.Model,
            Method = "direct-ai-fallback"
        };
    }

    /// <summary>
    /// フォールバック応答（LLM 利用不可時）
    /// </summary>
    private AIProcessingResult CreateFallbackResponse(string message, double sentimentScore, string sentimentLabel)
    {
        var lowerMessage = message.ToLowerInvariant();
        var result = new AIProcessingResult
        {
            SentimentScore = sentimentScore,
            SentimentLabel = sentimentLabel,
            Method = "direct-ai-rule"
        };

        // シンプルなルールベース応答
        if (ContainsAny(lowerMessage, "こんにちは", "こんばんは", "おはよう", "hello", "hi"))
        {
            result.Message = "こんにちは！NetYamlForge へようこそ。どのようなご用件でしょうか？";
            result.QuickReplies = new List<QuickReplyButton>
            {
                new() { Label = "営業時間を聞く", ActionType = "postback", ActionValue = "hours_inquiry" },
                new() { Label = "予約を申し込む", ActionType = "postback", ActionValue = "appointment_booking" }
            };
        }
        else if (ContainsAny(lowerMessage, "営業時間", "何時まで", "何時から"))
        {
            result.Message = "営業時間は平日 9:00〜18:00、土日祝日 9:00〜17:00 となっております。年末年始・お盆期間は別途ご案内いたします。";
        }
        else if (ContainsAny(lowerMessage, "予約", "申し込む"))
        {
            result.Message = "ご予約を承ります。ご希望の日時とご用件（試乗、点検、見積もりなど）をお知らせください。";
            result.NeedsMoreInfo = true;
            result.RequiredFields = new List<string> { "preferred_date", "preferred_time", "appointment_type" };
        }
        else if (sentimentScore < -0.5)
        {
            result.Message = "ご不便をおかけして誠に申し訳ございません。詳しく状況を伺い、早急に対応させていただきます。";
            result.NeedsHandover = true;
            result.HandoverReason = "customer_unsatisfied";
            result.Priority = "high";
        }
        else if (ContainsAny(lowerMessage, "担当者", "責任者", "クレーム", "苦情"))
        {
            result.Message = "担当者が対応いたします。少々お待ちください。";
            result.NeedsHandover = true;
            result.HandoverReason = "human_requested";
            result.Priority = "high";
        }
        else
        {
            result.Message = "お問い合わせいただき、ありがとうございます。詳しくお聞かせください。";
            result.NeedsMoreInfo = true;
        }

        return result;
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
    private class LlmDirectResponseDto
    {
        public string? Message { get; set; }
        public Dictionary<string, string>? Entities { get; set; }
        public bool NeedsHandover { get; set; }
        public string? HandoverReason { get; set; }
        public string? Priority { get; set; }
        public string? TargetDepartment { get; set; }
        public List<QuickReplyButton>? QuickReplies { get; set; }
        public bool NeedsMoreInfo { get; set; }
        public List<string>? RequiredFields { get; set; }
    }
}
