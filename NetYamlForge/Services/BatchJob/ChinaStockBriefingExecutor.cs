// ファイル概要：中国股市简报定时任务执行器 - 获取股市数据并保存到数据库

using System.Data;
using System.Text;
using Dapper;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 中国股市简报任务执行器
/// </summary>
public class ChinaStockBriefingExecutor : IBatchStepHandler
{
    public string StepType => "china_stock_briefing";
    private readonly IChinaStockService _stockService;
    private readonly ILogger<ChinaStockBriefingExecutor> _logger;

    public ChinaStockBriefingExecutor(
        IChinaStockService stockService,
        ILogger<ChinaStockBriefingExecutor> logger)
    {
        _stockService = stockService;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var r = await ExecuteAsync(job, db, tx, ct);
        result.Success = r.Success;
        result.RowsAffected = r.RowsAffected;
        result.ErrorMessage = r.ErrorMessage;
        result.ErrorDetail = r.ErrorDetail;
    }

    /// <summary>
    /// 执行中国股市数据获取和保存
    /// </summary>
    public async Task<BatchJobResult> ExecuteAsync(
        BatchJobDefinition job,
        IDbConnection db,
        IDbTransaction? tx,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchJobResult
        {
            JobId = job.Id,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("中国股市データ取得開始：{JobId}", job.Id);

            // 1. 获取所有主要指数行情
            var markets = await _stockService.GetAllMarketIndicesAsync(cancellationToken);

            if (markets == null || !markets.Any())
            {
                throw new Exception("股市数据获取失败：返回数据为空");
            }

            // 2. 生成简评
            var briefingNote = _stockService.GenerateBriefingNote(markets);
            _logger.LogInformation("生成简评：{Note}", briefingNote);

            // 3. 保存到数据库
            var rowsAffected = 0;
            foreach (var market in markets)
            {
                market.BriefingNote = briefingNote;
                rowsAffected += await SaveMarketDataAsync(db, tx, market, cancellationToken);
            }

            // 4. 导出 CSV（如果配置了 outputFile）
            if (!string.IsNullOrEmpty(job.Settings.OutputFile))
            {
                await ExportToCsvAsync(markets, job.Settings.OutputFile, job.Settings);
            }

            result.Success = true;
            result.RowsAffected = rowsAffected;
            result.EndedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "中国股市データ取得完了：{JobId}, Rows: {Rows}, Duration: {Duration}ms",
                job.Id, rowsAffected, result.DurationMs);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "中国股市データ取得エラー：{JobId}", job.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndedAt = DateTime.UtcNow;
            return result;
        }
    }

    /// <summary>
    /// 保存单个市场数据到数据库
    /// </summary>
    private async Task<int> SaveMarketDataAsync(
        IDbConnection db,
        IDbTransaction? tx,
        StockMarketData market,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO stock_market_data (
                market_code, market_name, current_price, change_amount, change_percent,
                open_price, high_price, low_price, prev_close, volume, amount,
                market_status, briefing_note, data_source, created_at, updated_at
            ) VALUES (
                @MarketCode, @MarketName, @CurrentPrice, @ChangeAmount, @ChangePercent,
                @OpenPrice, @HighPrice, @LowPrice, @PrevClose, @Volume, @Amount,
                @MarketStatus, @BriefingNote, @DataSource, @CreatedAt, @UpdatedAt
            )";

        return await db.ExecuteAsync(sql, new
        {
            market.MarketCode,
            market.MarketName,
            CurrentPrice = market.CurrentPrice.HasValue ? (object)market.CurrentPrice.Value : DBNull.Value,
            ChangeAmount = market.ChangeAmount.HasValue ? (object)market.ChangeAmount.Value : DBNull.Value,
            ChangePercent = market.ChangePercent.HasValue ? (object)market.ChangePercent.Value : DBNull.Value,
            OpenPrice = market.OpenPrice.HasValue ? (object)market.OpenPrice.Value : DBNull.Value,
            HighPrice = market.HighPrice.HasValue ? (object)market.HighPrice.Value : DBNull.Value,
            LowPrice = market.LowPrice.HasValue ? (object)market.LowPrice.Value : DBNull.Value,
            PrevClose = market.PrevClose.HasValue ? (object)market.PrevClose.Value : DBNull.Value,
            Volume = market.Volume.HasValue ? (object)market.Volume.Value : DBNull.Value,
            Amount = market.Amount.HasValue ? (object)market.Amount.Value : DBNull.Value,
            MarketStatus = market.MarketStatus,
            BriefingNote = (object?)market.BriefingNote ?? DBNull.Value,
            DataSource = market.DataSource,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, transaction: tx);
    }

    /// <summary>
    /// 导出股市数据到 CSV 文件
    /// </summary>
    private async Task ExportToCsvAsync(
        List<StockMarketData> markets,
        string outputFile,
        JobSettings settings)
    {
        var sb = new StringBuilder();

        // 表头
        if (settings.IncludeHeader)
        {
            sb.AppendLine($"市场代码，市场名称，当前点位，涨跌额，涨跌幅 (%)");
        }

        // 数据行
        foreach (var market in markets)
        {
            sb.AppendLine($"{market.MarketCode},{market.MarketName}," +
                         $"{market.CurrentPrice?.ToString("F2") ?? ""}," +
                         $"{market.ChangeAmount?.ToString("F2") ?? ""}," +
                         $"{market.ChangePercent?.ToString("F2") ?? ""}");
        }

        // 确保输出目录存在
        var directory = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 替换日期占位符
        var finalPath = outputFile.Replace("{date:yyyyMMdd_HHmm}", DateTime.Now.ToString("yyyyMMdd_HHmm"));

        await File.WriteAllTextAsync(finalPath, sb.ToString(), Encoding.UTF8);
    }
}
