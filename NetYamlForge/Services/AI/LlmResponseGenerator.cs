using System.Diagnostics;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI.Providers;

namespace NetYamlForge.Services.AI;

/// <summary>
/// LLM 応答生成サービス
/// </summary>
public class LlmResponseGenerator : IResponseGenerator
{
    private readonly ILlmProvider? _llmProvider;
    private readonly IKnowledgeBaseService _knowledgeBaseService;
    private readonly ILogger<LlmResponseGenerator> _logger;
    private readonly AiWindowConfig _config;

    public LlmResponseGenerator(
        ILlmProvider? llmProvider,
        IKnowledgeBaseService knowledgeBaseService,
        IOptions<AiWindowConfig> configOptions,
        ILogger<LlmResponseGenerator> logger)
    {
        _llmProvider = llmProvider;
        _knowledgeBaseService = knowledgeBaseService;
        _logger = logger;
        _config = configOptions.Value;
    }

    /// <inheritdoc />
    public async Task<AIResponse> GenerateResponseAsync(IntentResult intentResult, ConversationContext? conversationContext = null, string? projectId = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. ナレッジベース検索（高速）
            var knowledgeResult = await _knowledgeBaseService.SearchAsync(intentResult.Intent, intentResult.Entities, projectId);
            if (knowledgeResult != null)
            {
                // 使用回数を記録
                await _knowledgeBaseService.IncrementUsageAsync(knowledgeResult.KnowledgeId, projectId);

                stopwatch.Stop();
                return new AIResponse
                {
                    Message = knowledgeResult.Message,
                    MessageType = "text",
                    QuickReplies = knowledgeResult.QuickReplies,
                    GenerationMethod = "knowledge",
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // 2. LLM 応答生成
            if (_config.Llm.Enabled && _llmProvider != null)
            {
                var llmResponse = await GenerateWithLlmAsync(intentResult, conversationContext);
                stopwatch.Stop();
                return llmResponse;
            }

            // 3. デフォルトテンプレート
            stopwatch.Stop();
            return new AIResponse
            {
                Message = GetDefaultResponse(intentResult.Intent),
                MessageType = "text",
                QuickReplies = intentResult.QuickReplies,
                GenerationMethod = "template",
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "応答生成に失敗");
            return new AIResponse
            {
                Message = "申し訳ございませんが、一時的なエラーが発生しました。しばらくしてお試しください。",
                MessageType = "text",
                GenerationMethod = "error",
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <inheritdoc />
    public string GenerateWelcomeMessage(string channel, string? projectId = null)
    {
        var config = _config;
        
        return channel switch
        {
            "line" => """
                こんにちは！🚗 自動車ディーラー AI アシスタントです。
                
                以下のことをお手伝いできます：
                • 営業時間のご案内
                • サービス予約
                • 車両のお問い合わせ
                • 試乗の申し込み
                
                ご用件をお知らせください！
                """,
            "web" => """
                こんにちは！自動車ディーラー AI アシスタントへようこそ。
                
                営業時間、予約、車両のお問い合わせなど、お気軽にお尋ねください。
                """,
            _ => """
                こんにちは！自動車ディーラー AI アシスタントです。
                どのようなご用件でしょうか？
                """
        };
    }

    /// <inheritdoc />
    public string GenerateHandoverMessage(string reason, string? projectId = null)
    {
        return reason switch
        {
            "complaint" => "お客様のご不満をお聞きし、誠心誠意対応させていただきます。担当者がまいりますので、しばらくお待ちください。",
            "complex_inquiry" => "詳しいお問い合わせを承りました。専門スタッフが対応させていただきます。",
            "vip_customer" => "VIP 客様、特別に対応させていただきます。担当者がまいります。",
            _ => "より詳しいご案内をさせていただきます。担当者がまいりますので、少々お待ちください。"
        };
    }

    /// <summary>
    /// LLM で応答を生成
    /// </summary>
    private async Task<AIResponse> GenerateWithLlmAsync(IntentResult intentResult, ConversationContext? context)
    {
        if (_llmProvider == null)
            throw new InvalidOperationException("LLM プロバイダーが設定されていません");

        var prompt = BuildResponsePrompt(intentResult, context);
        var response = await _llmProvider.CompleteAsync(prompt);

        return new AIResponse
        {
            Message = response.Trim(),
            MessageType = "text",
            QuickReplies = intentResult.QuickReplies,
            GenerationMethod = "llm"
        };
    }

    /// <summary>
    /// 応答プロンプトを構築
    /// </summary>
    private string BuildResponsePrompt(IntentResult intentResult, ConversationContext? context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("あなたは親切で丁寧な自動車ディーラーの AI アシスタントです。");
        sb.AppendLine("顧客の問い合わせに対して、適切で丁寧な応答を生成してください。");
        sb.AppendLine();
        sb.AppendLine($"【判定された意図】{intentResult.Intent}");
        sb.AppendLine($"【置信度】{intentResult.Confidence:P0}");
        
        if (intentResult.Entities.Any())
        {
            sb.AppendLine("【抽出された情報】");
            foreach (var entity in intentResult.Entities)
            {
                sb.AppendLine($"  {entity.Key}: {entity.Value}");
            }
        }

        if (context != null && context.PreviousMessages.Any())
        {
            sb.AppendLine();
            sb.AppendLine("【会話履歴】");
            foreach (var msg in context.PreviousMessages.TakeLast(3))
            {
                sb.AppendLine($"  {msg.Sender}: {msg.Content}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("【応答の要件】");
        sb.AppendLine("- 丁寧な敬語を使用");
        sb.AppendLine("- 簡潔で分かりやすい表現");
        sb.AppendLine("- 必要な場合は追加情報を求める");
        sb.AppendLine("- 200 文字以内で回答");

        if (intentResult.NeedsMoreInfo)
        {
            sb.AppendLine();
            sb.AppendLine($"【必要な情報】{string.Join(", ", intentResult.RequiredFields)}");
            sb.AppendLine("これらの情報を顧客に尋ねてください。");
        }

        return sb.ToString();
    }

    /// <summary>
    /// デフォルトレスポンスを取得
    /// </summary>
    private string GetDefaultResponse(string intent)
    {
        return intent switch
        {
            "greeting" => "こんにちは！自動車ディーラー AI アシスタントです。どのようなご用件でしょうか？",
            "hours_inquiry" => """
                営業時間は以下の通りです：
                
                平日：9:00 - 19:00
                土日祝：9:00 - 18:00
                定休日：水曜日
                
                ご予約は 24 時間承っております。
                """,
            "price_inquiry" => "車両の価格や見積もりについては、具体的な車種やお見積り内容をお知らせください。詳しくご案内させていただきます。",
            "appointment_booking" => "ご予約を承ります。ご希望の日時とサービス内容（点検、修理、試乗など）をお知らせください。",
            "appointment_change" => "ご予約の変更を承ります。変更後のご希望日時をお知らせください。",
            "appointment_cancel" => "ご予約のキャンセルを承ります。キャンセルされるご予約の番号またはお名前をお知らせください。",
            "vehicle_inquiry" => "車両についてのお問い合わせですね。具体的な車種やご質問内容をお知らせください。",
            "service_inquiry" => "サービス内容についてのご案内をさせていただきます。どのようなサービスについてお知りになりたいでしょうか？",
            "complaint" => "この度はご不便をおかけし、誠に申し訳ございません。担当者が対応させていただきます。",
            "human_agent" => "担当者に接続いたします。少々お待ちください。",
            _ => "お問い合わせを承りました。詳しくお聞かせください。"
        };
    }
}

/// <summary>
/// AI 設定
/// </summary>
public class AiWindowConfig
{
    public LlmConfig Llm { get; set; } = new();
    public IntentConfig Intent { get; set; } = new();
    public HandoverConfig Handover { get; set; } = new();
}

/// <summary>
/// LLM 設定
/// </summary>
public class LlmConfig
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "qwen";
    public string Model { get; set; } = "qwen2.5-coder:7b";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// 意図分類設定
/// </summary>
public class IntentConfig
{
    public bool RuleBasedEnabled { get; set; } = true;
    public bool LlmEnabled { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.6;
}

/// <summary>
/// エスカレーション設定
/// </summary>
public class HandoverConfig
{
    public bool AutoEnabled { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.6;
    public double NegativeSentimentThreshold { get; set; } = -0.5;
    public bool VipAutoHandover { get; set; } = true;
}
