using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Services;

/// <summary>
/// 感情分析サービス（ルールベース）
/// </summary>
public class SentimentAnalyzer : ISentimentAnalyzer
{
    private readonly ILogger<SentimentAnalyzer> _logger;

    // 感情キーワード
    private static readonly Dictionary<string, double> PositiveWords = new()
    {
        { "嬉しい", 0.8 }, { "楽しい", 0.7 }, { "素晴らしい", 0.9 },
        { "良い", 0.5 }, { "いい", 0.5 }, { "満足", 0.8 },
        { "ありがとう", 0.6 }, { "感謝", 0.7 }, { "助かる", 0.6 },
        { "素敵", 0.7 }, { "最高", 0.9 }, { "期待", 0.4 }
    };

    private static readonly Dictionary<string, double> NegativeWords = new()
    {
        { "怒り", -0.9 }, { "腹立つ", -0.8 }, { "最悪", -1.0 },
        { "悪い", -0.5 }, { "だめ", -0.6 }, { "不満", -0.7 },
        { "困る", -0.6 }, { "残念", -0.5 }, { "がっかり", -0.7 },
        { "ひどい", -0.8 }, { "許せない", -0.9 }, { "苦情", -0.8 },
        { "遅い", -0.4 }, { "高い", -0.3 }, { "心配", -0.4 }
    };

    public SentimentAnalyzer(ILogger<SentimentAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SentimentResult> AnalyzeAsync(string text, string? projectId = null)
    {
        var result = new SentimentResult
        {
            Score = 0.0,
            Label = "neutral",
            Confidence = 0.5
        };

        if (string.IsNullOrEmpty(text))
            return Task.FromResult(result);

        try
        {
            var positiveScore = 0.0;
            var negativeScore = 0.0;
            var keywords = new List<string>();

            // 正の感情スコア
            foreach (var word in PositiveWords.Keys)
            {
                if (text.Contains(word))
                {
                    positiveScore += PositiveWords[word];
                    keywords.Add(word);
                }
            }

            // 負の感情スコア
            foreach (var word in NegativeWords.Keys)
            {
                if (text.Contains(word))
                {
                    negativeScore += Math.Abs(NegativeWords[word]);
                    keywords.Add(word);
                }
            }

            // 総合スコアを計算 (-1.0 〜 1.0)
            var totalScore = positiveScore - negativeScore;
            var maxScore = Math.Max(positiveScore, negativeScore);

            if (maxScore > 0)
            {
                // スコアを正規化
                result.Score = Math.Max(-1.0, Math.Min(1.0, totalScore / Math.Max(1, keywords.Count)));
                result.Confidence = Math.Min(1.0, maxScore / 2.0);

                // ラベルを決定
                if (result.Score > 0.3)
                    result.Label = "positive";
                else if (result.Score < -0.3)
                    result.Label = "negative";
                else
                    result.Label = "neutral";
            }

            result.Keywords = keywords;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "感情分析に失敗、デフォルト値を返します");
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<List<SentimentResult>> AnalyzeBatchAsync(IEnumerable<string> texts, string? projectId = null)
    {
        var results = new List<SentimentResult>();
        foreach (var text in texts)
        {
            results.Add(await AnalyzeAsync(text, projectId));
        }
        return results;
    }
}
