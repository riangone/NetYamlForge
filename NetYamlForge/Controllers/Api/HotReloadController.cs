// ファイル概要：YAML ホットリロード状態を管理する API コントローラー
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetYamlForge.Services.HotReload;

namespace NetYamlForge.Controllers.Api;

[ApiController]
[Route("api/hotreload")]
[Authorize(Policy = "AdminOnly")]
public class HotReloadController : ControllerBase
{
    private readonly ProjectYamlCacheManager _cacheManager;
    private readonly IOptions<HotReloadOptions> _options;
    private readonly ILogger<HotReloadController> _logger;

    public HotReloadController(
        ProjectYamlCacheManager cacheManager,
        IOptions<HotReloadOptions> options,
        ILogger<HotReloadController> logger)
    {
        _cacheManager = cacheManager;
        _options = options;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var status = new
        {
            Enabled = _options.Value.Enabled,
            OnlyInDevelopment = _options.Value.OnlyInDevelopment,
            IsDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development",
            Caches = _cacheManager.GetStatusSnapshot(),
            Timestamp = DateTime.UtcNow
        };
        return Ok(status);
    }

    [HttpPost("reload/{projectName}")]
    public async Task<IActionResult> ReloadProject(string projectName)
    {
        try
        {
            await _cacheManager.ReloadProjectAsync(projectName);
            return Ok(new { Success = true, Message = $"プロジェクト '{projectName}' のキャッシュをリロードしました" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    [HttpPost("clear-all")]
    public IActionResult ClearAll()
    {
        _cacheManager.ClearAll();
        return Ok(new { Success = true, Message = "全キャッシュをクリアしました" });
    }
}
