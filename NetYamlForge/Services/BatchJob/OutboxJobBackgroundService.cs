using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models.Email;
using System.Text.Json;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 持久化アウトボックスジョブキューを監視して、バックグラウンドで処理する HostedService です。
/// </summary>
public class OutboxJobBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxJobBackgroundService> _logger;

    public OutboxJobBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<OutboxJobBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxJobBackgroundService is starting...");

        // 起動時にクラッシュや再起動で中立状態（Running）のままになったジョブを復元する
        try
        {
            using var startupScope = _serviceProvider.CreateScope();
            var outboxJobService = startupScope.ServiceProvider.GetRequiredService<IOutboxJobService>();
            await outboxJobService.RecoverInterruptedJobsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover interrupted jobs during startup.");
        }

        // ジョブの定期監視ループ
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var outboxJobService = scope.ServiceProvider.GetRequiredService<IOutboxJobService>();

                // 一度に 5 件までのジョブを処理
                var pendingJobs = await outboxJobService.GetPendingJobsAsync(limit: 5);

                foreach (var job in pendingJobs)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    // 楽観的ロック（ステータスを Running に変更できた場合のみ実行）
                    var locked = await outboxJobService.LockJobAsync(job.Id);
                    if (locked)
                    {
                        // 非同期実行
                        _ = ExecuteJobAsync(job, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during outbox job polling cycle.");
            }

            // 1秒待機
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }

        _logger.LogInformation("OutboxJobBackgroundService is stopping.");
    }

    private async Task ExecuteJobAsync(OutboxJob job, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var outboxJobService = scope.ServiceProvider.GetRequiredService<IOutboxJobService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OutboxJobBackgroundService>>();

        BatchJobDefinition? batchJobDefinition = null;

        try
        {
            _logger.LogInformation("Processing job {JobId} of type {JobType}...", job.Id, job.JobType);

            if (job.JobType == "batch_job")
            {
                batchJobDefinition = JsonSerializer.Deserialize<BatchJobDefinition>(job.Payload);
                if (batchJobDefinition == null)
                    throw new InvalidOperationException("Failed to deserialize BatchJobDefinition payload");

                var batchJobExecutor = scope.ServiceProvider.GetRequiredService<IRealBatchJobExecutor>();
                var result = await batchJobExecutor.ExecuteRealAsync(batchJobDefinition, job.ProjectName, stoppingToken);

                if (!result.Success)
                {
                    throw new Exception(result.ErrorMessage ?? "Batch job execution failed without a specific error message.");
                }

                // 履歴保存
                var jobHistoryStore = scope.ServiceProvider.GetService<IBatchJobHistoryStore>();
                if (jobHistoryStore != null)
                {
                    await jobHistoryStore.SaveHistoryAsync(new BatchJobHistory
                    {
                        JobId = batchJobDefinition.Id,
                        ExecutedAt = DateTime.UtcNow,
                        Result = result
                    });
                }
            }
            else if (job.JobType == "ai_folder_processor_task")
            {
                var payload = JsonSerializer.Deserialize<AiFolderProcessorTaskPayload>(job.Payload);
                if (payload == null)
                    throw new InvalidOperationException("Failed to deserialize AiFolderProcessorTaskPayload");

                var aiFolderProcessor = scope.ServiceProvider.GetRequiredService<AiFolderProcessorExecutor>();
                await aiFolderProcessor.ProcessSingleTaskAsync(payload.ProjectName, payload.ConnectionString, payload.TaskId, payload.RelativeFilePath);
            }
            else
            {
                throw new NotSupportedException($"Job type '{job.JobType}' is not supported.");
            }

            // 成功マーク
            await outboxJobService.CompleteJobAsync(job.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing job {JobId} of type {JobType}", job.Id, job.JobType);

            var attempts = job.Attempts + 1; // ロック時にDBでカウントアップされた後の現在の実行回数

            if (attempts < job.MaxAttempts)
            {
                // 指数バックオフ計算：5 * 2^(attempts - 1) 秒
                var backoffSeconds = 5 * Math.Pow(2, attempts - 1);
                if (backoffSeconds > 300) backoffSeconds = 300; // 最大 5 分

                var nextRun = DateTime.UtcNow.AddSeconds(backoffSeconds);
                await outboxJobService.FailJobAsync(job.Id, ex.Message, nextRun);
            }
            else
            {
                // 完全に失敗
                await outboxJobService.FailJobAsync(job.Id, ex.Message, null);

                // バッチジョブの場合は失敗メール通知を実行
                if (job.JobType == "batch_job" && batchJobDefinition != null && batchJobDefinition.OnFailure?.Notify != null && batchJobDefinition.OnFailure.Notify.Any())
                {
                    await SendFailureNotificationAsync(scope, batchJobDefinition, job.ProjectName, ex);
                }
            }
        }
    }

    private async Task SendFailureNotificationAsync(
        IServiceScope scope, 
        BatchJobDefinition job, 
        string? projectName, 
        Exception ex)
    {
        try
        {
            var emailFactory = scope.ServiceProvider.GetService<NetYamlForge.Services.Email.IEmailServiceFactory>();
            var emailService = emailFactory?.GetForProject(projectName);
            if (emailService == null)
            {
                _logger.LogWarning("Email service factory is not available. Skipping error email notification.");
                return;
            }

            var notifyEmails = job.OnFailure!.Notify!;
            var jobName = job.DisplayName ?? job.Id;

            foreach (var email in notifyEmails)
            {
                try
                {
                    _logger.LogInformation("Sending failure notification email to: {Email} for job: {JobName}", email, jobName);
                    await emailService.SendEmailAsync(new EmailMessage
                    {
                        To = email,
                        Subject = $"【警告】バッチジョブ実行失敗: {jobName}",
                        Body = $"プロジェクト '{projectName}' 内のバッチジョブ '{jobName}' (ID: {job.Id}) が最大試行回数に達し失敗しました。\n\n" +
                               $"時間: {DateTime.Now}\n" +
                               $"エラー詳細: {ex.Message}\n\n" +
                               $"ログをご確認ください。",
                        IsHtml = false,
                        Date = DateTimeOffset.UtcNow
                    });
                }
                catch (Exception mailEx)
                {
                    _logger.LogWarning(mailEx, "Failed to send email to {Email}", email);
                }
            }
        }
        catch (Exception notifyEx)
        {
            _logger.LogError(notifyEx, "Error sending failure notification.");
        }
    }
}
