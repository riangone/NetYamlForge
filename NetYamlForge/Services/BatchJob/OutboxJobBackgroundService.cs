using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models.Email;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 持久化アウトボックスジョブキューを監視して、バックグラウンドで処理する HostedService です。
/// </summary>
public class OutboxJobBackgroundService : BasePollingBackgroundService
{
    public OutboxJobBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<OutboxJobBackgroundService> logger)
        : base(serviceProvider, logger, TimeSpan.FromSeconds(1))
    {
    }

    protected override async Task OnStartupAsync(CancellationToken stoppingToken)
    {
        // 启动时恢复中断的作业
        using var startupScope = ServiceProvider.CreateScope();
        var outboxJobService = startupScope.ServiceProvider.GetRequiredService<IOutboxJobService>();
        await outboxJobService.RecoverInterruptedJobsAsync();
    }

    protected override async Task PollAsync(IServiceProvider serviceProvider, CancellationToken stoppingToken)
    {
        var outboxJobService = serviceProvider.GetRequiredService<IOutboxJobService>();

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

    private async Task ExecuteJobAsync(OutboxJob job, CancellationToken stoppingToken)
    {
        using var scope = ServiceProvider.CreateScope();
        var outboxJobService = scope.ServiceProvider.GetRequiredService<IOutboxJobService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OutboxJobBackgroundService>>();

        BatchJobDefinition? batchJobDefinition = null;

        try
        {
            Logger.LogInformation("Processing job {JobId} of type {JobType}...", job.Id, job.JobType);

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

            var attempts = job.Attempts + 1; // ロック時にDBでカウントアップされた後の現在の执行回数

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
                Logger.LogWarning("Email service factory is not available. Skipping error email notification.");
                return;
            }

            var notifyEmails = job.OnFailure!.Notify!;
            var jobName = job.DisplayName ?? job.Id;

            foreach (var email in notifyEmails)
            {
                try
                {
                    Logger.LogInformation("Sending failure notification email to: {Email} for job: {JobName}", email, jobName);
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
                    Logger.LogWarning(mailEx, "Failed to send email to {Email}", email);
                }
            }
        }
        catch (Exception notifyEx)
        {
            Logger.LogError(notifyEx, "Error sending failure notification.");
        }
    }
}
