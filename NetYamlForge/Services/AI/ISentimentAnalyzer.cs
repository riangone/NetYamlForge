namespace NetYamlForge.Services.AI;

/// <summary>
/// 感情分析サービスインターフェース
/// </summary>
public interface ISentimentAnalyzer
{
    /// <summary>
    /// テキストの感情を分析
    /// </summary>
    /// <param name="text">分析対象テキスト</param>
    /// <param name="projectId">プロジェクト ID</param>
    /// <returns>感情スコア (-1.0 〜 1.0)</returns>
    Task<SentimentResult> AnalyzeAsync(string text, string? projectId = null);

    /// <summary>
    /// 複数のテキストバッチ分析
    /// </summary>
    Task<List<SentimentResult>> AnalyzeBatchAsync(IEnumerable<string> texts, string? projectId = null);
}

/// <summary>
/// 感情分析結果
/// </summary>
public class SentimentResult
{
    /// <summary>
    /// 感情スコア (-1.0: 不満 〜 1.0: 満足)
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 感情ラベル (negative, neutral, positive)
    /// </summary>
    public string Label { get; set; } = "neutral";

    /// <summary>
    /// 信頼度
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 検出された感情キーワード
    /// </summary>
    public List<string> Keywords { get; set; } = new();
}
