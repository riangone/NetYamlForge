using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Connection;

namespace NetYamlForge.Services.BatchJob;

public interface IOutboxJobService
{
    Task EnqueueAsync(string jobType, string payload, string? projectName, int maxAttempts = 3, DateTime? scheduledAt = null);
    Task<List<OutboxJob>> GetPendingJobsAsync(int limit = 10);
    Task<bool> LockJobAsync(string jobId);
    Task CompleteJobAsync(string jobId);
    Task FailJobAsync(string jobId, string errorMessage, DateTime? nextRunTime);
    Task RecoverInterruptedJobsAsync();
}

public class OutboxJobService : IOutboxJobService
{
    private readonly string _systemDbConnectionString;
    private readonly ILogger<OutboxJobService> _logger;

    public OutboxJobService(IConfiguration config, ILogger<OutboxJobService> logger)
    {
        _logger = logger;
        var dbPath = config["SystemDbPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "system.db");
        _systemDbConnectionString = dbPath.Contains(';') ? dbPath : new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
    }

    private async Task<IDbConnection> GetConnectionAsync()
    {
        // DCS003 抑制理由: system.db はグローバル認証 DB であり ProjectScope に依存しないため直接接続する
#pragma warning disable DCS003
        var conn = new SqliteConnection(_systemDbConnectionString);
#pragma warning restore DCS003
        await conn.OpenAsync();
        await SqliteConnectionHardening.ApplyAsync(conn);
        return conn;
    }

    public async Task EnqueueAsync(string jobType, string payload, string? projectName, int maxAttempts = 3, DateTime? scheduledAt = null)
    {
        var jobId = Guid.NewGuid().ToString("D");
        var schedTime = scheduledAt ?? DateTime.UtcNow;

        using var conn = await GetConnectionAsync();
        await SqliteWriteGate.RunAsync(conn, async () =>
        {
            await conn.ExecuteAsync(@"
                INSERT INTO nyf_jobs (id, job_type, payload, status, attempts, max_attempts, scheduled_at, project_name)
                VALUES (@Id, @JobType, @Payload, 'Pending', 0, @MaxAttempts, @ScheduledAt, @ProjectName)",
                new
                {
                    Id = jobId,
                    JobType = jobType,
                    Payload = payload,
                    MaxAttempts = maxAttempts,
                    ScheduledAt = schedTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    ProjectName = projectName
                });
        });
        
        _logger.LogInformation("Job {JobId} of type {JobType} enqueued. Scheduled at: {ScheduledAt}", jobId, jobType, schedTime);
    }

    public async Task<List<OutboxJob>> GetPendingJobsAsync(int limit = 10)
    {
        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        using var conn = await GetConnectionAsync();
        var jobs = await conn.QueryAsync<OutboxJob>(@"
            SELECT id as Id, job_type as JobType, payload as Payload, status as Status, 
                   attempts as Attempts, max_attempts as MaxAttempts, scheduled_at as ScheduledAt,
                   started_at as StartedAt, completed_at as CompletedAt, error_message as ErrorMessage,
                   project_name as ProjectName
            FROM nyf_jobs
            WHERE status = 'Pending' AND scheduled_at <= @Now
            ORDER BY scheduled_at ASC
            LIMIT @Limit",
            new { Now = nowStr, Limit = limit });
        return jobs.ToList();
    }

    public async Task<bool> LockJobAsync(string jobId)
    {
        using var conn = await GetConnectionAsync();
        return await SqliteWriteGate.RunAsync(conn, async () =>
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var affected = await conn.ExecuteAsync(@"
                UPDATE nyf_jobs
                SET status = 'Running',
                    started_at = @Now,
                    attempts = attempts + 1
                WHERE id = @Id AND status = 'Pending'",
                new { Id = jobId, Now = nowStr });
            return affected > 0;
        });
    }

    public async Task CompleteJobAsync(string jobId)
    {
        using var conn = await GetConnectionAsync();
        await SqliteWriteGate.RunAsync(conn, async () =>
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await conn.ExecuteAsync(@"
                UPDATE nyf_jobs
                SET status = 'Completed',
                    completed_at = @Now
                WHERE id = @Id",
                new { Id = jobId, Now = nowStr });
        });
        _logger.LogInformation("Job {JobId} completed successfully.", jobId);
    }

    public async Task FailJobAsync(string jobId, string errorMessage, DateTime? nextRunTime)
    {
        using var conn = await GetConnectionAsync();
        await SqliteWriteGate.RunAsync(conn, async () =>
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            if (nextRunTime.HasValue)
            {
                // まだリトライ可能な場合：ステータスを Pending に戻し、次回実行時刻を更新する
                await conn.ExecuteAsync(@"
                    UPDATE nyf_jobs
                    SET status = 'Pending',
                        scheduled_at = @NextRunTime,
                        error_message = @Error
                    WHERE id = @Id",
                    new
                    {
                        Id = jobId,
                        NextRunTime = nextRunTime.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                        Error = errorMessage
                    });
                _logger.LogWarning("Job {JobId} failed. Rescheduled at: {NextRunTime}. Error: {Error}", jobId, nextRunTime, errorMessage);
            }
            else
            {
                // 最大リトライ回数に達した場合：ステータスを Failed に更新する
                await conn.ExecuteAsync(@"
                    UPDATE nyf_jobs
                    SET status = 'Failed',
                        completed_at = @Now,
                        error_message = @Error
                    WHERE id = @Id",
                    new
                    {
                        Id = jobId,
                        Now = nowStr,
                        Error = errorMessage
                    });
                _logger.LogError("Job {JobId} failed permanently. Error: {Error}", jobId, errorMessage);
            }
        });
    }

    public async Task RecoverInterruptedJobsAsync()
    {
        using var conn = await GetConnectionAsync();
        await SqliteWriteGate.RunAsync(conn, async () =>
        {
            // 起動時に Running 状態のジョブを Pending にリセットし、再実行可能にする
            var affected = await conn.ExecuteAsync(@"
                UPDATE nyf_jobs
                SET status = 'Pending',
                    error_message = 'Interrupted by process restart'
                WHERE status = 'Running'");
            if (affected > 0)
            {
                _logger.LogInformation("Recovered {Count} interrupted running job(s) to Pending status.", affected);
            }
        });
    }
}
