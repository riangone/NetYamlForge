using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.Connection;
using Xunit;

namespace NetYamlForge.Tests.Services.BatchJob;

public class OutboxJobQueueTests : IDisposable
{
    private readonly string _dbDir;
    private readonly string _dbPath;
    private readonly IConfiguration _config;
    private readonly ServiceProvider _serviceProvider;

    public OutboxJobQueueTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), "nyf-outbox-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _dbPath = Path.Combine(_dbDir, "system.db");

        // 構成設定
        var inMemorySettings = new Dictionary<string, string?> {
            {"SystemDbPath", _dbPath}
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // サービスコンテナの構築
        var services = new ServiceCollection();
        services.AddSingleton(_config);
        services.AddLogging(builder => builder.AddConsole());

        // サービス登録
        services.AddScoped<IOutboxJobService, OutboxJobService>();
        
        // モック依存登録
        var mockRealBatchExecutor = new Mock<IRealBatchJobExecutor>();
        var ended = DateTime.UtcNow;
        var started = ended.AddMilliseconds(-100);
        mockRealBatchExecutor.Setup(x => x.ExecuteRealAsync(It.IsAny<BatchJobDefinition>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchJobResult { Success = true, StartedAt = started, EndedAt = ended });
        services.AddSingleton(mockRealBatchExecutor.Object);

        services.AddScoped<IBatchJobExecutor, BatchJobExecutor>();

        _serviceProvider = services.BuildServiceProvider();

        // スキーマの初期化
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        // テスト用の SQLite 接続 (DCS003 抑制理由：テスト用)
#pragma warning disable DCS003
        using var conn = new SqliteConnection("Data Source=" + _dbPath);
#pragma warning restore DCS003
        conn.Open();
        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS nyf_jobs (
                id TEXT PRIMARY KEY,
                job_type TEXT NOT NULL,
                payload TEXT NOT NULL,
                status TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                max_attempts INTEGER NOT NULL DEFAULT 3,
                scheduled_at TEXT NOT NULL,
                started_at TEXT,
                completed_at TEXT,
                error_message TEXT,
                project_name TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_nyf_jobs_status_scheduled_at ON nyf_jobs(status, scheduled_at);
        ");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dbDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task EnqueueAndGetPendingJobs_ShouldWorkCorrectly()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOutboxJobService>();

        // Enqueue
        await service.EnqueueAsync("batch_job", "{}", "test_project", maxAttempts: 3);

        // Get Pending
        var pending = await service.GetPendingJobsAsync();
        Assert.Single(pending);
        Assert.Equal("batch_job", pending[0].JobType);
        Assert.Equal("Pending", pending[0].Status);
        Assert.Equal("test_project", pending[0].ProjectName);
    }

    [Fact]
    public async Task LockJob_ShouldBeAtomicAndReturnTrueOnce()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOutboxJobService>();

        await service.EnqueueAsync("batch_job", "{}", "test_project", maxAttempts: 3);
        var pending = await service.GetPendingJobsAsync();
        var jobId = pending[0].Id;

        // 並行ロックのシミュレーション
        var lockResults = await Task.WhenAll(
            service.LockJobAsync(jobId),
            service.LockJobAsync(jobId),
            service.LockJobAsync(jobId)
        );

        // 1回だけロック成功するはず
        Assert.Single(lockResults, x => x);
    }

    [Fact]
    public async Task CompleteJob_ShouldMarkStatusAsCompleted()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOutboxJobService>();

        await service.EnqueueAsync("batch_job", "{}", "test_project");
        var pending = await service.GetPendingJobsAsync();
        var jobId = pending[0].Id;

        await service.LockJobAsync(jobId);
        await service.CompleteJobAsync(jobId);

        // DB 状態の検証 (DCS003 抑制理由：テスト用)
#pragma warning disable DCS003
        using var conn = new SqliteConnection("Data Source=" + _dbPath);
#pragma warning restore DCS003
        conn.Open();
        var status = await conn.ExecuteScalarAsync<string>("SELECT status FROM nyf_jobs WHERE id = @Id", new { Id = jobId });
        Assert.Equal("Completed", status);
    }

    [Fact]
    public async Task FailJob_UnderMaxAttempts_ShouldRescheduleToPending()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOutboxJobService>();

        await service.EnqueueAsync("batch_job", "{}", "test_project", maxAttempts: 3);
        var pending = await service.GetPendingJobsAsync();
        var jobId = pending[0].Id;

        await service.LockJobAsync(jobId);
        
        // 失敗とリトライのシミュレーション（Lock時に attempts は 1 増えている）
        var nextRun = DateTime.UtcNow.AddMinutes(5);
        await service.FailJobAsync(jobId, "Sample error", nextRun);

        // 状態が Pending に戻ったか検証 (DCS003 抑制理由：テスト用)
#pragma warning disable DCS003
        using var conn = new SqliteConnection("Data Source=" + _dbPath);
#pragma warning restore DCS003
        conn.Open();
        var job = await conn.QuerySingleAsync<OutboxJob>("SELECT status as Status, scheduled_at as ScheduledAt, error_message as ErrorMessage FROM nyf_jobs WHERE id = @Id", new { Id = jobId });
        Assert.Equal("Pending", job.Status);
        Assert.Equal("Sample error", job.ErrorMessage);
    }

    [Fact]
    public async Task RecoverInterruptedJobs_ShouldResetRunningToPending()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOutboxJobService>();

        await service.EnqueueAsync("batch_job", "{}", "test_project", maxAttempts: 3);
        var pending = await service.GetPendingJobsAsync();
        var jobId = pending[0].Id;

        // ジョブをロック（Running にする）
        await service.LockJobAsync(jobId);

        // プロセスの再起動・リカバリをシミュレーション
        await service.RecoverInterruptedJobsAsync();

        // 状態が Pending に戻ったか検証 (DCS003 抑制理由：テスト用)
#pragma warning disable DCS003
        using var conn = new SqliteConnection("Data Source=" + _dbPath);
#pragma warning restore DCS003
        conn.Open();
        var status = await conn.ExecuteScalarAsync<string>("SELECT status FROM nyf_jobs WHERE id = @Id", new { Id = jobId });
        Assert.Equal("Pending", status);
    }
}
