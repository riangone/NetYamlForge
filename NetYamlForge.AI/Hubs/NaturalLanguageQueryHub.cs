using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services;

namespace NetYamlForge.AI.Hubs;

/// <summary>
/// AI 自然语言查询 SignalR Hub
/// 支持实时对话式查询
/// </summary>
public class NaturalLanguageQueryHub : Hub
{
    private readonly QueryParserService _parser;
    private readonly QueryExecutionService _executor;
    private readonly QueryResultFormatter _formatter;
    private readonly ILogger<NaturalLanguageQueryHub> _logger;

    public NaturalLanguageQueryHub(
        QueryParserService parser,
        QueryExecutionService executor,
        QueryResultFormatter formatter,
        ILogger<NaturalLanguageQueryHub> logger)
    {
        _parser = parser;
        _executor = executor;
        _formatter = formatter;
        _logger = logger;
    }

    /// <summary>
    /// 连接建立
    /// </summary>
    public async Task Connect(string? project = null)
    {
        _logger.LogInformation("NLQuery 客户端连接：{ConnectionId}, Project: {Project}", 
            Context.ConnectionId, project);

        await Clients.Caller.SendAsync("connected", new
        {
            connectionId = Context.ConnectionId,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 发送自然语言查询
    /// </summary>
    public async Task SendQuery(string query, string? project = null, string? entity = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            await Clients.Caller.SendAsync("error", new { message = "查询内容不能为空" });
            return;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 1. 发送"思考中"状态
            await Clients.Caller.SendAsync("thinking_start", new
            {
                query = query,
                timestamp = DateTime.UtcNow
            });

            // 2. AI 解析自然语言
            var parsedQuery = await _parser.ParseAsync(query, project, entity);

            // 发送解析结果
            await Clients.Caller.SendAsync("parsing_complete", new
            {
                parsedQuery = parsedQuery,
                timestamp = DateTime.UtcNow
            });

            // 3. 执行查询
            var (data, total) = await _executor.ExecuteAsync(parsedQuery, project ?? "");

            stopwatch.Stop();

            // 4. 格式化结果
            var markdown = _formatter.FormatAsMarkdown(data, parsedQuery, total);

            // 5. 发送最终结果
            await Clients.Caller.SendAsync("query_complete", new
            {
                data = data,
                markdown = markdown,
                parsedQuery = parsedQuery,
                total = total,
                executionTimeMs = stopwatch.ElapsedMilliseconds,
                timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("NLQuery 完成：耗时={Ms}ms, 结果={Count}条", 
                stopwatch.ElapsedMilliseconds, data.Count);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "NLQuery 执行失败：{Query}", query);

            await Clients.Caller.SendAsync("error", new
            {
                message = ex.Message,
                query = query,
                executionTimeMs = stopwatch.ElapsedMilliseconds
            });
        }
        finally
        {
            await Clients.Caller.SendAsync("thinking_stop");
        }
    }

    /// <summary>
    /// 取消查询
    /// </summary>
    public async Task CancelQuery()
    {
        _logger.LogInformation("NLQuery 取消：{ConnectionId}", Context.ConnectionId);
        // TODO: 实现取消逻辑
        await Clients.Caller.SendAsync("query_cancelled");
    }

    /// <summary>
    /// 获取历史查询
    /// </summary>
    public async Task GetHistory(int count = 10)
    {
        // TODO: 实现历史查询记录
        await Clients.Caller.SendAsync("history", new
        {
            queries = new List<object>()
        });
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("NLQuery 客户端断开：{ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
