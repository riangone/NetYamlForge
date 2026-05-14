// ファイル概要: バッチジョブ管理画面のコントローラー。
// ジョブ一覧、実行履歴表示、手動トリガーを提供します。

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Controllers;

// ─── ViewModels ──────────────────────────────────────────────────────────────

public class BatchJobListItemViewModel
{
    public string JobId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? CronExpression { get; set; }
    public string Timezone { get; set; } = "UTC";
    public bool Enabled { get; set; }
    public DateTime? NextRunTime { get; set; }
    public BatchJobHistory? LastExecution { get; set; }
    public bool IsRunning { get; set; }
    public string? Description { get; set; }
}

public class BatchJobIndexViewModel
{
    public string ProjectName { get; set; } = string.Empty;
    public List<BatchJobListItemViewModel> Jobs { get; set; } = new();
}

public class BatchJobHistoryViewModel
{
    public string JobId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public List<BatchJobHistory> History { get; set; } = new();
}

// ─── Controller ──────────────────────────────────────────────────────────────

[Authorize]
[Route("{project}/BatchJob/{action=Index}")]
public class BatchJobController : BaseProjectController
{
    private readonly IBatchJobScheduler _scheduler;
    private readonly IBatchJobLoader _loader;
    private readonly IBatchJobHistoryStore _historyStore;
    private readonly ProjectManager _projectManager;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<BatchJobController> _logger;

    public BatchJobController(
        IBatchJobScheduler scheduler,
        IBatchJobLoader loader,
        IBatchJobHistoryStore historyStore,
        ProjectManager projectManager,
        ProjectScope projectScope,
        ILogger<BatchJobController> logger)
    {
        _scheduler = scheduler;
        _loader = loader;
        _historyStore = historyStore;
        _projectManager = projectManager;
        _projectScope = projectScope;
        _logger = logger;
    }

    // ─── Index ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        if (!_projectScope.IsSet)
            return NotFound();

        var project = _projectScope.Current;
        var projectName = project.Name;

        // YAML からジョブ定義を読み込む（無効なジョブも含む）
        var allJobs = await _loader.LoadJobsAsync(project.ProjectDir);

        // スケジューラ登録済みジョブ（有効なもの）
        var scheduledJobs = _scheduler.GetScheduledJobsForProject(projectName)
            .ToDictionary(j => j.Job.Id, j => j);
        var runningIds = _scheduler.GetRunningJobIds();

        var items = new List<BatchJobListItemViewModel>();
        foreach (var kvp in allJobs.OrderBy(k => k.Key))
        {
            var job = kvp.Value;
            scheduledJobs.TryGetValue(job.Id, out var scheduled);

            var lastHistory = (await _historyStore.GetHistoryAsync(job.Id, 1)).FirstOrDefault();

            items.Add(new BatchJobListItemViewModel
            {
                JobId = job.Id,
                DisplayName = string.IsNullOrEmpty(job.DisplayName) ? job.Id : job.DisplayName,
                Type = job.Type,
                CronExpression = job.Schedule.Cron,
                Timezone = job.Schedule.Timezone,
                Enabled = job.Enabled,
                NextRunTime = scheduled?.NextRunTime,
                LastExecution = lastHistory,
                IsRunning = runningIds.Contains(job.Id),
                Description = job.Description
            });
        }

        var vm = new BatchJobIndexViewModel
        {
            ProjectName = projectName,
            Jobs = items
        };

        ViewData["Title"] = "Batch Jobs";
        return View(vm);
    }

    // ─── History ─────────────────────────────────────────────────────────────

    public async Task<IActionResult> History(string jobId)
    {
        if (!_projectScope.IsSet)
            return NotFound();

        var project = _projectScope.Current;
        var allJobs = await _loader.LoadJobsAsync(project.ProjectDir);

        if (!allJobs.TryGetValue(jobId, out var job))
            return NotFound();

        var history = await _historyStore.GetHistoryAsync(jobId, 100);

        var vm = new BatchJobHistoryViewModel
        {
            JobId = jobId,
            DisplayName = string.IsNullOrEmpty(job.DisplayName) ? jobId : job.DisplayName,
            ProjectName = project.Name,
            History = history
        };

        ViewData["Title"] = $"{vm.DisplayName} - 実行履歴";
        return View(vm);
    }

    // ─── TriggerJob (POST, HTMX) ─────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> TriggerJob(string jobId)
    {
        if (!_projectScope.IsSet)
            return NotFound();

        var projectName = _projectScope.Current.Name;

        _logger.LogInformation("手動トリガー：{Project}/{JobId} by {User}",
            projectName, jobId, User.Identity?.Name);

        await _scheduler.TriggerJobNowAsync(projectName, jobId);

        // HTMX リクエストの場合は _JobRow 部分ビューを返す
        if (IsHtmxRequest())
        {
            var project = _projectScope.Current;
            var allJobs = await _loader.LoadJobsAsync(project.ProjectDir);
            allJobs.TryGetValue(jobId, out var job);

            var lastHistory = (await _historyStore.GetHistoryAsync(jobId, 1)).FirstOrDefault();
            var scheduledJobs = _scheduler.GetScheduledJobsForProject(projectName)
                .ToDictionary(j => j.Job.Id, j => j);
            scheduledJobs.TryGetValue(jobId, out var scheduled);
            var runningIds = _scheduler.GetRunningJobIds();

            var item = new BatchJobListItemViewModel
            {
                JobId = jobId,
                DisplayName = job != null && !string.IsNullOrEmpty(job.DisplayName) ? job.DisplayName : jobId,
                Type = job?.Type ?? "",
                CronExpression = job?.Schedule.Cron,
                Timezone = job?.Schedule.Timezone ?? "UTC",
                Enabled = job?.Enabled ?? false,
                NextRunTime = scheduled?.NextRunTime,
                LastExecution = lastHistory,
                IsRunning = runningIds.Contains(jobId),
                Description = job?.Description
            };

            ViewData["ProjectName"] = projectName;
            return PartialView("_JobRow", item);
        }

        return RedirectToAction("Index", new { project = projectName });
    }

    // ─── JobStatus (GET, HTMX polling) ───────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> JobStatus(string jobId)
    {
        if (!_projectScope.IsSet)
            return NotFound();

        var project = _projectScope.Current;
        var projectName = project.Name;
        var allJobs = await _loader.LoadJobsAsync(project.ProjectDir);
        allJobs.TryGetValue(jobId, out var job);

        var lastHistory = (await _historyStore.GetHistoryAsync(jobId, 1)).FirstOrDefault();
        var scheduledJobs = _scheduler.GetScheduledJobsForProject(projectName)
            .ToDictionary(j => j.Job.Id, j => j);
        scheduledJobs.TryGetValue(jobId, out var scheduled);
        var runningIds = _scheduler.GetRunningJobIds();

        var item = new BatchJobListItemViewModel
        {
            JobId = jobId,
            DisplayName = job != null && !string.IsNullOrEmpty(job.DisplayName) ? job.DisplayName : jobId,
            Type = job?.Type ?? "",
            CronExpression = job?.Schedule.Cron,
            Timezone = job?.Schedule.Timezone ?? "UTC",
            Enabled = job?.Enabled ?? false,
            NextRunTime = scheduled?.NextRunTime,
            LastExecution = lastHistory,
            IsRunning = runningIds.Contains(jobId),
            Description = job?.Description
        };

        ViewData["ProjectName"] = projectName;
        return PartialView("_JobRow", item);
    }
}
