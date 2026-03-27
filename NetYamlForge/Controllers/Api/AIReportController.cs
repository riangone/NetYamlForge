using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers.Api;

/// <summary>
/// AI 分析レポート API
/// </summary>
[ApiController]
[Route("api/ai/reports")]
[Produces("application/json")]
public class AIReportController : ControllerBase
{
    private readonly IAIReportPdfService _reportService;
    private readonly ILogger<AIReportController> _logger;

    public AIReportController(
        IAIReportPdfService reportService,
        ILogger<AIReportController> logger)
    {
        _reportService = reportService;
        _logger = logger;
    }

    /// <summary>
    /// 日次レポート PDF を生成
    /// </summary>
    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyReport(
        [FromQuery] DateTime? date = null)
    {
        try
        {
            var reportDate = date ?? DateTime.Today;
            var pdfBytes = await _reportService.GenerateDailyReportAsync(reportDate);

            return File(pdfBytes, "application/pdf", $"ai-daily-report-{reportDate:yyyyMMdd}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "日次レポート生成に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 週次レポート PDF を生成
    /// </summary>
    [HttpGet("weekly")]
    public async Task<IActionResult> GetWeeklyReport(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] int? weeks = null)
    {
        try
        {
            var start = startDate ?? DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            var end = start.AddDays(7).AddSeconds(-1);
            
            if (weeks.HasValue)
            {
                start = start.AddDays(-(weeks.Value - 1) * 7);
            }

            var pdfBytes = await _reportService.GenerateWeeklyReportAsync(start, end);

            return File(pdfBytes, "application/pdf", $"ai-weekly-report-{start:yyyyMMdd}-{end:yyyyMMdd}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "週次レポート生成に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 月次レポート PDF を生成
    /// </summary>
    [HttpGet("monthly")]
    public async Task<IActionResult> GetMonthlyReport(
        [FromQuery] int? year = null,
        [FromQuery] int? month = null)
    {
        try
        {
            var now = DateTime.Now;
            var reportYear = year ?? now.Year;
            var reportMonth = month ?? now.Month;

            var pdfBytes = await _reportService.GenerateMonthlyReportAsync(reportYear, reportMonth);

            return File(pdfBytes, "application/pdf", $"ai-monthly-report-{reportYear}{reportMonth:00}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "月次レポート生成に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// レポートプレビュー（JSON）
    /// </summary>
    [HttpGet("preview")]
    public async Task<ActionResult<ReportPreviewDto>> GetReportPreview(
        [FromQuery] string type = "daily",
        [FromQuery] DateTime? date = null)
    {
        try
        {
            // TODO: 実際のレポートデータを取得
            var preview = new ReportPreviewDto
            {
                ReportType = type,
                Date = date ?? DateTime.Today,
                TotalConversations = 150,
                AResolved = 118,
                HandedOver = 32,
                ResponseRate = 78.7,
                AvgSatisfaction = 0.42
            };

            return Ok(preview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "レポートプレビュー取得に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }
}

public class ReportPreviewDto
{
    public string ReportType { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int TotalConversations { get; set; }
    public int AResolved { get; set; }
    public int HandedOver { get; set; }
    public double ResponseRate { get; set; }
    public double AvgSatisfaction { get; set; }
}
