// ファイル概要：中国股市行情数据服务 - 获取中国股市实时行情数据

using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetYamlForge.Services;

/// <summary>
/// 中国股市行情数据服务接口
/// </summary>
public interface IChinaStockService
{
    /// <summary>
    /// 获取主要指数实时行情
    /// </summary>
    Task<StockMarketData> GetMarketIndexAsync(string marketCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有主要指数行情
    /// </summary>
    Task<List<StockMarketData>> GetAllMarketIndicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成股市简评
    /// </summary>
    string GenerateBriefingNote(List<StockMarketData> markets);
}

/// <summary>
/// 股市行情数据模型
/// </summary>
public class StockMarketData
{
    public string MarketCode { get; set; } = string.Empty;      // SSEC, SZSC, CYB
    public string MarketName { get; set; } = string.Empty;      // 上证指数，深证成指，创业板指
    public decimal? CurrentPrice { get; set; }                  // 当前点位
    public decimal? ChangeAmount { get; set; }                  // 涨跌额
    public decimal? ChangePercent { get; set; }                 // 涨跌幅 (%)
    public decimal? OpenPrice { get; set; }                     // 开盘价
    public decimal? HighPrice { get; set; }                     // 最高价
    public decimal? LowPrice { get; set; }                      // 最低价
    public decimal? PrevClose { get; set; }                     // 昨收
    public long? Volume { get; set; }                           // 成交量 (手)
    public decimal? Amount { get; set; }                        // 成交额 (元)
    public string MarketStatus { get; set; } = string.Empty;    // TRADING, CLOSED, HALTED
    public string? BriefingNote { get; set; }                   // 简评
    public string DataSource { get; set; } = "东方财富网";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 中国股市行情数据服务实现
/// 使用东方财富网 API 获取实时行情数据
/// </summary>
public class ChinaStockService : IChinaStockService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ChinaStockService> _logger;
    private static readonly string BaseUrl = "https://push2.eastmoney.com";

    // 主要指数代码映射
    private static readonly Dictionary<string, string> MarketIndexCodes = new()
    {
        { "SSEC", "1.000001" },    // 上证指数
        { "SZSC", "0.399001" },    // 深证成指
        { "CYB", "0.399006" },     // 创业板指
        { "SZ50", "1.000016" },    // 上证 50
        { "HS300", "1.000300" }    // 沪深 300
    };

    public ChinaStockService(HttpClient httpClient, ILogger<ChinaStockService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(3600);
    }

    /// <summary>
    /// 获取主要指数实时行情
    /// </summary>
    public async Task<StockMarketData> GetMarketIndexAsync(string marketCode, CancellationToken cancellationToken = default)
    {
        if (!MarketIndexCodes.TryGetValue(marketCode, out var code))
        {
            throw new ArgumentException($"未知的市场代码：{marketCode}");
        }

        try
        {
            // 东方财富网实时行情 API
            var url = $"/api/qt/stock/get?secid={code}&fields=f43,f44,f46,f47,f48,f49,f50,f51,f52,f53,f54,f55,f56,f57,f58";
            var response = await _httpClient.GetStringAsync(url, cancellationToken);

            return ParseMarketData(response, marketCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取 {MarketCode} 行情数据失败", marketCode);
            return CreateMockData(marketCode); // 返回模拟数据用于测试
        }
    }

    /// <summary>
    /// 获取所有主要指数行情
    /// </summary>
    public async Task<List<StockMarketData>> GetAllMarketIndicesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<StockMarketData>();

        foreach (var marketCode in MarketIndexCodes.Keys)
        {
            try
            {
                var data = await GetMarketIndexAsync(marketCode, cancellationToken);
                results.Add(data);
                await Task.Delay(100, cancellationToken); // 避免请求过快
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 {MarketCode} 行情失败", marketCode);
            }
        }

        return results;
    }

    /// <summary>
    /// 生成股市简评
    /// </summary>
    public string GenerateBriefingNote(List<StockMarketData> markets)
    {
        if (markets == null || !markets.Any())
        {
            return "市场数据暂不可用";
        }

        var ssec = markets.FirstOrDefault(m => m.MarketCode == "SSEC");
        var szsc = markets.FirstOrDefault(m => m.MarketCode == "SZSC");
        var cyb = markets.FirstOrDefault(m => m.MarketCode == "CYB");

        var note = new System.Text.StringBuilder();
        note.Append("【中国股市简报】");

        // 上证指数简评
        if (ssec != null && ssec.ChangePercent.HasValue)
        {
            var trend = ssec.ChangePercent >= 0 ? "上涨" : "下跌";
            note.Append($" 上证指数{trend}{Math.Abs(ssec.ChangePercent.Value):F2}%，");
            if (ssec.ChangePercent > 1)
                note.Append("市场表现强劲，");
            else if (ssec.ChangePercent < -1)
                note.Append("市场承压，");
            else
                note.Append("市场震荡，");
        }

        // 深证成指简评
        if (szsc != null && szsc.ChangePercent.HasValue)
        {
            var trend = szsc.ChangePercent >= 0 ? "涨" : "跌";
            note.Append($"深证成指{trend}{Math.Abs(szsc.ChangePercent.Value):F2}%。");
        }

        // 创业板简评
        if (cyb != null && cyb.ChangePercent.HasValue)
        {
            var trend = cyb.ChangePercent >= 0 ? "涨" : "跌";
            note.Append($"创业板指{trend}{Math.Abs(cyb.ChangePercent.Value):F2}%。");
        }

        // 成交量分析
        var totalVolume = markets.Sum(m => m.Volume ?? 0);
        if (totalVolume > 0)
        {
            note.Append($" 总成交量：{totalVolume / 10000:F2}万手。");
        }

        // 时段提示
        var beijingTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        if (beijingTime.Hour < 11)
        {
            note.Append("（早盘数据）");
        }
        else if (beijingTime.Hour < 15)
        {
            note.Append("（午盘数据）");
        }
        else
        {
            note.Append("（收盘数据）");
        }

        return note.ToString();
    }

    /// <summary>
    /// 解析 API 返回的市场数据
    /// </summary>
    private StockMarketData ParseMarketData(string json, string marketCode)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var data = doc.RootElement;

            if (!data.TryGetProperty("data", out var dataNode))
            {
                return CreateMockData(marketCode);
            }

            var market = new StockMarketData
            {
                MarketCode = marketCode,
                MarketName = GetMarketName(marketCode),
                CurrentPrice = GetDecimalOrNull(dataNode, "f43"),
                ChangeAmount = GetDecimalOrNull(dataNode, "f44"),
                ChangePercent = GetDecimalOrNull(dataNode, "f46"),
                OpenPrice = GetDecimalOrNull(dataNode, "f47"),
                HighPrice = GetDecimalOrNull(dataNode, "f48"),
                LowPrice = GetDecimalOrNull(dataNode, "f49"),
                PrevClose = GetDecimalOrNull(dataNode, "f50"),
                Volume = GetInt64OrNull(dataNode, "f51"),
                Amount = GetDecimalOrNull(dataNode, "f52"),
                CreatedAt = DateTime.UtcNow
            };

            // 判断市场状态
            market.MarketStatus = DetermineMarketStatus(marketCode);

            return market;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析市场数据失败：{MarketCode}", marketCode);
            return CreateMockData(marketCode);
        }
    }

    /// <summary>
    /// 创建模拟数据（用于 API 失败时的降级）
    /// </summary>
    private StockMarketData CreateMockData(string marketCode)
    {
        var random = new Random();
        var basePrice = marketCode switch
        {
            "SSEC" => 3000m,
            "SZSC" => 9500m,
            "CYB" => 2000m,
            _ => 1000m
        };

        var changePercent = (decimal)(random.NextDouble() * 4 - 2); // -2% ~ +2%

        return new StockMarketData
        {
            MarketCode = marketCode,
            MarketName = GetMarketName(marketCode),
            CurrentPrice = basePrice * (1 + changePercent / 100),
            ChangeAmount = basePrice * changePercent / 100,
            ChangePercent = changePercent,
            OpenPrice = basePrice * (1 + (decimal)(random.NextDouble() * 0.02 - 0.01)),
            HighPrice = basePrice * (1 + (decimal)(random.NextDouble() * 0.03)),
            LowPrice = basePrice * (1 - (decimal)(random.NextDouble() * 0.03)),
            PrevClose = basePrice,
            Volume = random.Next(1000000, 5000000),
            Amount = basePrice * random.Next(1000000, 5000000) / 100,
            MarketStatus = "CLOSED",
            BriefingNote = "（模拟数据 - API 不可用）",
            DataSource = "模拟数据",
            CreatedAt = DateTime.UtcNow
        };
    }

    private string GetMarketName(string code) => code switch
    {
        "SSEC" => "上证指数",
        "SZSC" => "深证成指",
        "CYB" => "创业板指",
        "SZ50" => "上证 50",
        "HS300" => "沪深 300",
        _ => code
    };

    private string DetermineMarketStatus(string marketCode)
    {
        var beijingTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        var hour = beijingTime.Hour;
        var minute = beijingTime.Minute;
        var dayOfWeek = beijingTime.DayOfWeek;

        // 周末休市
        if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
        {
            return "CLOSED";
        }

        // 交易时间判断
        var timeValue = hour * 100 + minute;
        if (timeValue >= 930 && timeValue < 1130 || timeValue >= 1300 && timeValue < 1500)
        {
            return "TRADING";
        }

        return "CLOSED";
    }

    private static decimal? GetDecimalOrNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            if (prop.TryGetDecimal(out var value))
                return value;
            if (prop.TryGetInt32(out var intValue))
                return intValue;
            if (prop.TryGetDouble(out var doubleValue))
                return (decimal)doubleValue;
        }
        return null;
    }

    private static long? GetInt64OrNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            if (prop.TryGetInt64(out var value))
                return value;
            if (prop.TryGetInt32(out var intValue))
                return intValue;
        }
        return null;
    }
}
