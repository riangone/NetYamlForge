// 自然语言查询 API 控制器
// 将用户的自然语言查询转换为结构化查询并返回结果
// ルート：/{project}/api/ai-query

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services;
using Microsoft.AspNetCore.Routing;

namespace NetYamlForge.AI.Controllers.Api;

[Authorize]
[ApiController]
[Route("{project}/api/ai-query")]
public class AIQueryController : ControllerBase
{
    private readonly QueryParserService _parser;
    private readonly QueryExecutionService _executor;
    private readonly QueryResultFormatter _formatter;
    private readonly ILogger<AIQueryController> _logger;

    public AIQueryController(
        QueryParserService parser,
        QueryExecutionService executor,
        QueryResultFormatter formatter,
        ILogger<AIQueryController> logger)
    {
        _parser = parser;
        _executor = executor;
        _formatter = formatter;
        _logger = logger;
    }

    private string GetProjectName()
    {
        return HttpContext.GetRouteValue("project")?.ToString() ?? "default";
    }

    /// <summary>
    /// POST: /{project}/api/ai-query/query
    /// 执行自然语言查询
    /// </summary>
    [HttpPost("query")]
    public async Task<IActionResult> Query(
        [FromBody] NaturalLanguageQueryRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "查询内容不能为空" });
        }

        var project = HttpContext.GetRouteValue("project")?.ToString() 
            ?? request.Project 
            ?? throw new InvalidOperationException("未指定项目");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 1. AI 解析自然语言
            _logger.LogInformation("开始解析查询：{Query}", request.Query);
            var parsedQuery = await _parser.ParseAsync(request.Query, project, request.Entity);

            // 2. 执行查询
            _logger.LogInformation("执行查询：实体={Entity}", parsedQuery.Entity);
            var (data, total) = await _executor.ExecuteAsync(parsedQuery, project, ct);

            // 3. 格式化结果
            var markdown = _formatter.FormatAsMarkdown(data, parsedQuery, total);

            stopwatch.Stop();

            var response = new NaturalLanguageQueryResponse
            {
                Data = data,
                Markdown = markdown,
                ParsedQuery = parsedQuery,
                Total = total,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };

            _logger.LogInformation("查询完成：耗时={Ms}ms, 结果={Count}条", 
                stopwatch.ElapsedMilliseconds, data.Count);

            return Ok(response);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "查询执行失败：{Query}", request.Query);

            return StatusCode(500, new
            {
                error = "查询执行失败",
                message = ex.Message,
                executionTimeMs = stopwatch.ElapsedMilliseconds
            });
        }
    }

    /// <summary>
    /// GET: /{project}/api/ai-query/suggest-entities
    /// 获取建议的实体列表（用于 AI 查询）
    /// </summary>
    [HttpGet("suggest-entities")]
    public IActionResult GetSuggestEntities()
    {
        // TODO: 实现实体建议逻辑
        return Ok(new
        {
            entities = new List<object>()
        });
    }

    /// <summary>
    /// POST: /{project}/api/ai-query/validate
    /// 验证查询（不执行，仅解析）
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> Validate(
        [FromBody] NaturalLanguageQueryRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "查询内容不能为空" });
        }

        var project = HttpContext.GetRouteValue("project")?.ToString() 
            ?? request.Project;

        try
        {
            var parsedQuery = await _parser.ParseAsync(request.Query, project, request.Entity);

            return Ok(new
            {
                valid = true,
                parsedQuery = parsedQuery
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                valid = false,
                error = ex.Message
            });
        }
    }
}
