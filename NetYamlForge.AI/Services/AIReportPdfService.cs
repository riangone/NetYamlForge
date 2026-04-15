using NetYamlForge.AI.Infrastructure;
using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Services;

/// <summary>
/// AI 分析レポート PDF サービス
/// </summary>
public interface IAIReportPdfService
{
    /// <summary>
    /// 日次レポート PDF を生成
    /// </summary>
    Task<byte[]> GenerateDailyReportAsync(DateTime date, string? projectId = null);

    /// <summary>
    /// 週次レポート PDF を生成
    /// </summary>
    Task<byte[]> GenerateWeeklyReportAsync(DateTime startDate, DateTime endDate, string? projectId = null);

    /// <summary>
    /// 月次レポート PDF を生成
    /// </summary>
    Task<byte[]> GenerateMonthlyReportAsync(int year, int month, string? projectId = null);
}

/// <summary>
/// レポートデータ
/// </summary>
public class AIReportData
{
    public DateTime ReportDate { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public ReportSummary Summary { get; set; } = new();
    public List<IntentStats> IntentStats { get; set; } = new();
    public List<ChannelStats> ChannelStats { get; set; } = new();
    public List<SentimentStats> SentimentStats { get; set; } = new();
    public List<HandoverStats> HandoverStats { get; set; } = new();
    public List<TrendData> DailyTrends { get; set; } = new();
}

public class ReportSummary
{
    public int TotalConversations { get; set; }
    public int AResolved { get; set; }
    public int HandedOver { get; set; }
    public double AResponseRate { get; set; }
    public double AvgResponseTime { get; set; }
    public double AvgSatisfaction { get; set; }
}

public class IntentStats
{
    public string Intent { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

public class ChannelStats
{
    public string Channel { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

public class SentimentStats
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

public class HandoverStats
{
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

public class TrendData
{
    public DateTime Date { get; set; }
    public int Conversations { get; set; }
    public int Resolved { get; set; }
    public int HandedOver { get; set; }
}

/// <summary>
/// AI 分析レポート PDF サービス実装
/// </summary>
public class AIReportPdfService : IAIReportPdfService
{
    private readonly IAIDbConnectionFactory _dbConnectionFactory;
    private readonly IAIProjectContext _projectContext;
    private readonly ILogger<AIReportPdfService> _logger;
    private const string DefaultProjectId = "auto-dealer-demo";

    public AIReportPdfService(
        IAIDbConnectionFactory dbConnectionFactory,
        IAIProjectContext projectContext,
        ILogger<AIReportPdfService> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _projectContext = projectContext;
        _logger = logger;
    }

    private string ResolveProject(string? projectId)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
            return projectId;
        if (_projectContext.IsSet)
            return _projectContext.ProjectName;
        return DefaultProjectId;
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateDailyReportAsync(DateTime date, string? projectId = null)
    {
        var project = ResolveProject(projectId);
        var reportData = await CollectReportDataAsync(date, date.AddDays(1).AddSeconds(-1), project);
        reportData.ReportType = "日次レポート";
        
        return await GeneratePdfAsync(reportData);
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateWeeklyReportAsync(DateTime startDate, DateTime endDate, string? projectId = null)
    {
        var project = ResolveProject(projectId);
        var reportData = await CollectReportDataAsync(startDate, endDate, project);
        reportData.ReportType = "週次レポート";
        
        return await GeneratePdfAsync(reportData);
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateMonthlyReportAsync(int year, int month, string? projectId = null)
    {
        var project = ResolveProject(projectId);
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddSeconds(-1);
        
        var reportData = await CollectReportDataAsync(startDate, endDate, project);
        reportData.ReportType = $"{year}年{month}月次レポート";
        
        return await GeneratePdfAsync(reportData);
    }

    /// <summary>
    /// レポートデータ収集
    /// </summary>
    private async Task<AIReportData> CollectReportDataAsync(DateTime startDate, DateTime endDate, string project)
    {
        var data = new AIReportData
        {
            ReportDate = startDate,
            Summary = new ReportSummary()
        };

        using var db = _dbConnectionFactory.CreateConnection(project);
        db.Open();

        // 基本統計
        var stats = await db.QueryFirstOrDefaultAsync(@"
            SELECT 
                COUNT(*) as total,
                SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END) as resolved,
                SUM(CASE WHEN status = 'escalated' THEN 1 ELSE 0 END) as handed_over,
                AVG(last_confidence) as avg_confidence,
                AVG(sentiment_score) as avg_sentiment
            FROM ai_conversations
            WHERE started_at BETWEEN @StartDate AND @EndDate",
            new { StartDate = startDate, EndDate = endDate });

        if (stats != null)
        {
            data.Summary.TotalConversations = stats.total;
            data.Summary.AResolved = stats.resolved;
            data.Summary.HandedOver = stats.handed_over;
            data.Summary.AResponseRate = stats.total > 0 ? (double)stats.resolved / stats.total * 100 : 0;
            data.Summary.AvgSatisfaction = stats.avg_sentiment ?? 0;
        }

        // インテント別統計
        var intentStats = await db.QueryAsync(@"
            SELECT 
                COALESCE(last_intent, 'unknown') as intent,
                COUNT(*) as count
            FROM ai_conversations
            WHERE started_at BETWEEN @StartDate AND @EndDate
            GROUP BY last_intent
            ORDER BY count DESC",
            new { StartDate = startDate, EndDate = endDate });

        foreach (var stat in intentStats)
        {
            data.IntentStats.Add(new IntentStats
            {
                Intent = stat.intent,
                Count = stat.count,
                Percentage = data.Summary.TotalConversations > 0 
                    ? (double)stat.count / data.Summary.TotalConversations * 100 : 0
            });
        }

        // チャネル別統計
        var channelStats = await db.QueryAsync(@"
            SELECT channel, COUNT(*) as count
            FROM ai_conversations
            WHERE started_at BETWEEN @StartDate AND @EndDate
            GROUP BY channel
            ORDER BY count DESC",
            new { StartDate = startDate, EndDate = endDate });

        foreach (var stat in channelStats)
        {
            data.ChannelStats.Add(new ChannelStats
            {
                Channel = stat.channel,
                Count = stat.count,
                Percentage = data.Summary.TotalConversations > 0 
                    ? (double)stat.count / data.Summary.TotalConversations * 100 : 0
            });
        }

        // 感情分析統計
        var sentimentStats = await db.QueryAsync(@"
            SELECT 
                CASE 
                    WHEN sentiment_score > 0.3 THEN 'positive'
                    WHEN sentiment_score < -0.3 THEN 'negative'
                    ELSE 'neutral'
                END as label,
                COUNT(*) as count
            FROM ai_conversations
            WHERE started_at BETWEEN @StartDate AND @EndDate
            AND sentiment_score IS NOT NULL
            GROUP BY label",
            new { StartDate = startDate, EndDate = endDate });

        foreach (var stat in sentimentStats)
        {
            data.SentimentStats.Add(new SentimentStats
            {
                Label = stat.label,
                Count = stat.count,
                Percentage = data.Summary.AResolved > 0 
                    ? (double)stat.count / data.Summary.AResolved * 100 : 0
            });
        }

        // エスカレーション統計
        var handoverStats = await db.QueryAsync(@"
            SELECT reason, COUNT(*) as count
            FROM ai_handovers
            WHERE escalated_at BETWEEN @StartDate AND @EndDate
            GROUP BY reason
            ORDER BY count DESC",
            new { StartDate = startDate, EndDate = endDate });

        foreach (var stat in handoverStats)
        {
            data.HandoverStats.Add(new HandoverStats
            {
                Reason = stat.reason,
                Count = stat.count,
                Percentage = data.Summary.HandedOver > 0 
                    ? (double)stat.count / data.Summary.HandedOver * 100 : 0
            });
        }

        // 日次トレンド
        var trends = await db.QueryAsync(@"
            SELECT 
                DATE(started_at) as date,
                COUNT(*) as conversations,
                SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END) as resolved,
                SUM(CASE WHEN status = 'escalated' THEN 1 ELSE 0 END) as handed_over
            FROM ai_conversations
            WHERE started_at BETWEEN @StartDate AND @EndDate
            GROUP BY DATE(started_at)
            ORDER BY date",
            new { StartDate = startDate, EndDate = endDate });

        foreach (var trend in trends)
        {
            data.DailyTrends.Add(new TrendData
            {
                Date = trend.date,
                Conversations = trend.conversations,
                Resolved = trend.resolved,
                HandedOver = trend.handed_over
            });
        }

        return data;
    }

    /// <summary>
    /// PDF 生成
    /// </summary>
    private async Task<byte[]> GeneratePdfAsync(AIReportData data)
    {
        // QuestPDF を使用して PDF 生成（簡易版）
        // 実際の実装では QuestPDF ライブラリが必要
        // ここではサンプル実装

        var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: 'Noto Sans JP', sans-serif; }}
        h1 {{ color: #007bff; }}
        table {{ border-collapse: collapse; width: 100%; margin: 20px 0; }}
        th, td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
        th {{ background-color: #007bff; color: white; }}
        .summary {{ display: flex; gap: 20px; margin: 20px 0; }}
        .summary-card {{ border: 1px solid #ddd; padding: 15px; border-radius: 5px; flex: 1; }}
        .summary-card h3 {{ margin: 0 0 10px 0; color: #666; }}
        .summary-card .value {{ font-size: 24px; font-weight: bold; color: #007bff; }}
    </style>
</head>
<body>
    <h1>AI 窓口分析レポート</h1>
    <p>レポート種別：{data.ReportType}</p>
    <p>対象期間：{data.ReportDate:yyyy-MM-dd}</p>
    
    <h2>サマリー</h2>
    <div class='summary'>
        <div class='summary-card'>
            <h3>総対話数</h3>
            <div class='value'>{data.Summary.TotalConversations}</div>
        </div>
        <div class='summary-card'>
            <h3>AI 解決数</h3>
            <div class='value'>{data.Summary.AResolved}</div>
        </div>
        <div class='summary-card'>
            <h3>転接数</h3>
            <div class='value'>{data.Summary.HandedOver}</div>
        </div>
        <div class='summary-card'>
            <h3>回答率</h3>
            <div class='value'>{data.Summary.AResponseRate:F1}%</div>
        </div>
    </div>
    
    <h2>インテント別内訳</h2>
    <table>
        <tr><th>インテント</th><th>件数</th><th>割合</th></tr>
        {string.Join("", data.IntentStats.Select(s => $"<tr><td>{s.Intent}</td><td>{s.Count}</td><td>{s.Percentage:F1}%</td></tr>"))}
    </table>
    
    <h2>チャネル別利用状況</h2>
    <table>
        <tr><th>チャネル</th><th>件数</th><th>割合</th></tr>
        {string.Join("", data.ChannelStats.Select(s => $"<tr><td>{s.Channel}</td><td>{s.Count}</td><td>{s.Percentage:F1}%</td></tr>"))}
    </table>
    
    <h2>感情分析</h2>
    <table>
        <tr><th>感情</th><th>件数</th><th>割合</th></tr>
        {string.Join("", data.SentimentStats.Select(s => $"<tr><td>{s.Label}</td><td>{s.Count}</td><td>{s.Percentage:F1}%</td></tr>"))}
    </table>
    
    <h2>エスカレーション理由</h2>
    <table>
        <tr><th>理由</th><th>件数</th><th>割合</th></tr>
        {string.Join("", data.HandoverStats.Select(s => $"<tr><td>{s.Reason}</td><td>{s.Count}</td><td>{s.Percentage:F1}%</td></tr>"))}
    </table>
</body>
</html>";

        // 実際の実装では HTML を PDF に変換
        // ここでは簡易的に HTML を返す
        return System.Text.Encoding.UTF8.GetBytes(html);
    }
}
