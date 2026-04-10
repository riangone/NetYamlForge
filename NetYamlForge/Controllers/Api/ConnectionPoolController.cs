using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.Connection;

namespace NetYamlForge.Controllers.Api;

/// <summary>
/// 连接池监控 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ConnectionPoolController : ControllerBase
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<ConnectionPoolController> _logger;

    public ConnectionPoolController(
        IConnectionManager connectionManager,
        ILogger<ConnectionPoolController> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有项目的连接池统计信息
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetAllPoolStats()
    {
        var stats = _connectionManager.GetAllPoolStats();
        return Ok(new
        {
            timestamp = DateTime.UtcNow,
            pools = stats.Select(kv => new
            {
                projectName = kv.Key,
                stats = kv.Value
            })
        });
    }

    /// <summary>
    /// 获取指定项目的连接池统计信息
    /// </summary>
    [HttpGet("stats/{projectName}")]
    public IActionResult GetPoolStats(string projectName)
    {
        var stats = _connectionManager.GetPoolStats(projectName);
        return Ok(new
        {
            projectName,
            timestamp = DateTime.UtcNow,
            stats
        });
    }

    /// <summary>
    /// 重置指定项目的连接池（关闭所有连接）
    /// </summary>
    [HttpPost("reset/{projectName}")]
    public IActionResult ResetPool(string projectName)
    {
        _logger.LogWarning("Resetting connection pool for project {ProjectName}", projectName);
        _connectionManager.ResetPool(projectName);
        return Ok(new { message = $"Connection pool for project '{projectName}' has been reset" });
    }

    /// <summary>
    /// Phase 2: 重置所有连接池
    /// </summary>
    [HttpPost("reset-all")]
    public IActionResult ResetAllPools()
    {
        _logger.LogWarning("Resetting all connection pools");
        _connectionManager.ResetAllPools();
        return Ok(new { message = "All connection pools have been reset" });
    }
}
