// ファイル概要：バッチジョブをバックグラウンドでスケジューリング実行する IHostedService 実装です。

using System.Collections.Concurrent;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// バッチジョブのスケジューリングサービス
/// </summary>
public interface IBatchJobScheduler
{
    /// <summary>
    /// ジョブを登録する
    /// </summary>
    void RegisterJob(BatchJobDefinition job, string projectName);

    /// <summary>
    /// ジョブの次回実行時刻を取得する
    /// </summary>
    DateTime? GetNextRunTime(string jobId);

    /// <summary>
    /// プロジェクトに登録されたスケジュール済みジョブを取得する
    /// </summary>
    IReadOnlyList<ScheduledJob> GetScheduledJobsForProject(string projectName);

    /// <summary>
    /// 指定ジョブを即時実行する（手動トリガー）
    /// </summary>
    Task TriggerJobNowAsync(string projectName, string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在実行中のジョブ ID セットを取得する
    /// </summary>
    IReadOnlySet<string> GetRunningJobIds();
}

/// <summary>
/// Cron 式パーサー
/// </summary>
public static class CronParser
{
    /// <summary>
    /// Cron 式から次の実行時刻を計算する
    /// </summary>
    public static DateTime? GetNextOccurrence(string cron, DateTime baseTime, string timezone = "UTC")
    {
        try
        {
            var parts = cron.Trim().Split(' ');
            if (parts.Length != 5)
            {
                return null;
            }

            var minute = ParseCronField(parts[0], 0, 59);
            var hour = ParseCronField(parts[1], 0, 23);
            var dayOfMonth = ParseCronField(parts[2], 1, 31);
            var month = ParseCronField(parts[3], 1, 12);
            var dayOfWeek = ParseCronField(parts[4], 0, 6);

            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var currentTime = TimeZoneInfo.ConvertTime(baseTime, tz);
            var nextTime = currentTime.AddMinutes(1).AddSeconds(-currentTime.Second);

            // 最大 1 年間検索
            for (int i = 0; i < 525600; i++) // 365 * 24 * 60
            {
                if (month.Contains(nextTime.Month) &&
                    dayOfMonth.Contains(nextTime.Day) &&
                    dayOfWeek.Contains((int)nextTime.DayOfWeek) &&
                    hour.Contains(nextTime.Hour) &&
                    minute.Contains(nextTime.Minute))
                {
                    return TimeZoneInfo.ConvertTime(nextTime, tz, TimeZoneInfo.Utc);
                }
                nextTime = nextTime.AddMinutes(1);
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static HashSet<int> ParseCronField(string field, int min, int max)
    {
        var result = new HashSet<int>();

        if (field == "*")
        {
            for (int i = min; i <= max; i++)
            {
                result.Add(i);
            }
            return result;
        }

        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                var values = part.Split('/');
                var start = values[0] == "*" ? min : int.Parse(values[0]);
                var step = int.Parse(values[1]);
                for (int i = start; i <= max; i += step)
                {
                    result.Add(i);
                }
            }
            else if (part.Contains('-'))
            {
                var values = part.Split('-');
                var start = int.Parse(values[0]);
                var end = int.Parse(values[1]);
                for (int i = start; i <= end; i++)
                {
                    result.Add(i);
                }
            }
            else
            {
                result.Add(int.Parse(part));
            }
        }

        return result;
    }
}

/// <summary>
/// バッチジョブホストサービス
/// </summary>
public class BatchJobHostedService : BackgroundService, IBatchJobScheduler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BatchJobHostedService> _logger;
    private readonly ConcurrentDictionary<string, ScheduledJob> _scheduledJobs = new();
    private readonly ConcurrentDictionary<string, byte> _runningJobs = new();

    public BatchJobHostedService(IServiceProvider serviceProvider, ILogger<BatchJobHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 初期ロード
        await LoadJobsAsync();

        // 1 分ごとにスケジュールをチェック
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRunJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ジョブスケジューリング中にエラーが発生しました");
            }

            await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
        }
    }

    private async Task LoadJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var projectManager = scope.ServiceProvider.GetRequiredService<ProjectManager>();
        var jobLoader = scope.ServiceProvider.GetRequiredService<IBatchJobLoader>();

        foreach (var project in projectManager.GetAll())
        {
            try
            {
                var jobs = await jobLoader.LoadJobsAsync(project.ProjectDir);
                foreach (var job in jobs.Values)
                {
                    if (job.Enabled && !string.IsNullOrEmpty(job.Schedule.Cron))
                    {
                        RegisterJob(job, project.Name);
                        _logger.LogInformation("ジョブをスケジュールしました：{Project}/{JobId}", 
                            project.Name, job.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "プロジェクト {Project} のジョブ読み込みに失敗しました", project.Name);
            }
        }
    }

    private async Task CheckAndRunJobsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        foreach (var kvp in _scheduledJobs)
        {
            var scheduledJob = kvp.Value;
            var nextRun = scheduledJob.NextRunTime;

            if (nextRun.HasValue && nextRun.Value <= now)
            {
                // ジョブ実行
                _ = RunJobAsync(scheduledJob, cancellationToken);

                // 次回実行時刻を計算
                if (!string.IsNullOrEmpty(scheduledJob.Job.Schedule.Cron))
                {
                    var nextOccurrence = CronParser.GetNextOccurrence(
                        scheduledJob.Job.Schedule.Cron, 
                        now, 
                        scheduledJob.Job.Schedule.Timezone);
                    
                    scheduledJob.NextRunTime = nextOccurrence;
                    _logger.LogDebug("ジョブ {JobId} の次回実行時刻を更新：{NextRun}", 
                        scheduledJob.Job.Id, nextOccurrence);
                }
            }
        }
    }

    private async Task RunJobAsync(ScheduledJob scheduledJob, CancellationToken cancellationToken)
    {
        if (!_runningJobs.TryAdd(scheduledJob.Job.Id, 0))
        {
            _logger.LogWarning("ジョブはすでに実行中です：{JobId}", scheduledJob.Job.Id);
            return;
        }

        try
        {
            await ExecuteJobCoreAsync(scheduledJob, cancellationToken);
        }
        finally
        {
            _runningJobs.TryRemove(scheduledJob.Job.Id, out _);
        }
    }

    private async Task ExecuteJobCoreAsync(ScheduledJob scheduledJob, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IBatchJobExecutor>();
        var jobHistoryStore = scope.ServiceProvider.GetService<IBatchJobHistoryStore>();

        var job = scheduledJob.Job;

        // リトライロジック
        var maxAttempts = (job.OnFailure?.RetryCount ?? 0) + 1;
        BatchJobResult? lastResult = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation("ジョブ実行開始：{JobId} (試行 {Attempt}/{Max})",
                    job.Id, attempt, maxAttempts);

                lastResult = await executor.ExecuteAsync(job, scheduledJob.ProjectName, cancellationToken);

                if (lastResult.Success)
                {
                    _logger.LogInformation("ジョブ成功：{JobId}, Duration: {Duration}ms, Rows: {Rows}",
                        job.Id, lastResult.DurationMs, lastResult.RowsAffected);
                    break;
                }

                // 失敗時
                if (attempt < maxAttempts)
                {
                    var retryInterval = TimeSpan.FromSeconds(job.OnFailure?.RetryInterval ?? 60);
                    _logger.LogWarning("ジョブ失敗、リトライ待機：{JobId}, Error: {Error}, Retry in {Seconds}s",
                        job.Id, lastResult.ErrorMessage, retryInterval.TotalSeconds);
                    await Task.Delay(retryInterval, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ジョブ実行中に例外：{JobId}", job.Id);
                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(job.OnFailure?.RetryInterval ?? 60), cancellationToken);
                }
            }
        }

        // 履歴保存
        if (jobHistoryStore != null && lastResult != null)
        {
            await jobHistoryStore.SaveHistoryAsync(new BatchJobHistory
            {
                JobId = job.Id,
                ExecutedAt = DateTime.UtcNow,
                Result = lastResult
            });
        }

        // 失敗通知
        if (lastResult != null && !lastResult.Success && job.OnFailure?.Notify != null)
        {
            // 邮件通知
            if (!string.IsNullOrEmpty(options.NotifyEmail))
            {
                try
                {
                    var subject = $"批处理作业完成: {jobName}";
                    var body = $"作业 {jobName} 已完成，状态: {result.Status}";
                    await emailService.SendEmailAsync(options.NotifyEmail, subject, body);
                    _logger.LogInformation("已发送完成通知邮件到: {Email}", options.NotifyEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "发送通知邮件失败");
                }
            }
            _logger.LogWarning("ジョブ失敗通知：{JobId}, Error: {Error}", job.Id, lastResult.ErrorMessage);
        }
    }

    /// <summary>
    /// ジョブを登録する
    /// </summary>
    public void RegisterJob(BatchJobDefinition job, string projectName)
    {
        if (string.IsNullOrEmpty(job.Schedule.Cron))
        {
            _logger.LogWarning("Cron 式が指定されていないため、ジョブ {JobId} はスキップされました", job.Id);
            return;
        }

        var nextRun = CronParser.GetNextOccurrence(
            job.Schedule.Cron, 
            DateTime.UtcNow, 
            job.Schedule.Timezone);

        var scheduledJob = new ScheduledJob
        {
            Job = job,
            ProjectName = projectName,
            NextRunTime = nextRun
        };

        _scheduledJobs[job.Id] = scheduledJob;
    }

    /// <summary>
    /// ジョブの次回実行時刻を取得する
    /// </summary>
    public DateTime? GetNextRunTime(string jobId)
    {
        return _scheduledJobs.TryGetValue(jobId, out var scheduledJob)
            ? scheduledJob.NextRunTime
            : null;
    }

    /// <summary>
    /// プロジェクトに登録されたスケジュール済みジョブを取得する
    /// </summary>
    public IReadOnlyList<ScheduledJob> GetScheduledJobsForProject(string projectName)
    {
        return _scheduledJobs.Values
            .Where(j => string.Equals(j.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 指定ジョブを即時実行する（手動トリガー）
    /// </summary>
    public Task TriggerJobNowAsync(string projectName, string jobId, CancellationToken cancellationToken = default)
    {
        if (_scheduledJobs.TryGetValue(jobId, out var scheduledJob) &&
            string.Equals(scheduledJob.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
        {
            _ = RunJobAsync(scheduledJob, cancellationToken);
            return Task.CompletedTask;
        }

        _logger.LogWarning("手動トリガー失敗：ジョブが見つかりません {Project}/{JobId}", projectName, jobId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 現在実行中のジョブ ID セットを取得する
    /// </summary>
    public IReadOnlySet<string> GetRunningJobIds() => _runningJobs.Keys.ToHashSet();
}

/// <summary>
/// スケジュールされたジョブ
/// </summary>
public class ScheduledJob
{
    /// <summary>
    /// ジョブ定義
    /// </summary>
    public BatchJobDefinition Job { get; set; } = new();

    /// <summary>
    /// プロジェクト名
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// 次回実行時刻
    /// </summary>
    public DateTime? NextRunTime { get; set; }
}

/// <summary>
/// バッチジョブ履歴ストア
/// </summary>
public interface IBatchJobHistoryStore
{
    /// <summary>
    /// 実行履歴を保存する
    /// </summary>
    Task SaveHistoryAsync(BatchJobHistory history);

    /// <summary>
    /// ジョブの履歴を取得する
    /// </summary>
    Task<List<BatchJobHistory>> GetHistoryAsync(string jobId, int limit = 50);
}

/// <summary>
/// インメモリ履歴ストア（実運用では DB 保存を実装）
/// </summary>
public class InMemoryBatchJobHistoryStore : IBatchJobHistoryStore
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<BatchJobHistory>> _history = new();
    private readonly int _maxHistoryPerJob = 100;

    public Task SaveHistoryAsync(BatchJobHistory history)
    {
        var queue = _history.GetOrAdd(history.JobId, _ => new ConcurrentQueue<BatchJobHistory>());
        queue.Enqueue(history);

        // 古い履歴を削除
        while (queue.Count > _maxHistoryPerJob)
        {
            queue.TryDequeue(out _);
        }

        return Task.CompletedTask;
    }

    public Task<List<BatchJobHistory>> GetHistoryAsync(string jobId, int limit = 50)
    {
        if (!_history.TryGetValue(jobId, out var queue))
        {
            return Task.FromResult(new List<BatchJobHistory>());
        }

        var result = queue.TakeLast(limit).Reverse().ToList();
        return Task.FromResult(result);
    }
}
