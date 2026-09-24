using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NetYamlForge.Services;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.Connection;
using NetYamlForge.Services.Database;
using Xunit;

namespace NetYamlForge.Tests.Services.BatchJob;

public class BatchJobDistributedLeaseTests : IDisposable
{
    private readonly string _dbDir;
    private readonly string _dbPath;
    private readonly IConfiguration _config;
    private readonly OutboxJobService _outboxJobService;

    public BatchJobDistributedLeaseTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), "nyf-batch-lease-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _dbPath = Path.Combine(_dbDir, "system.db");

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "SystemDbPath", _dbPath }
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // 初始化基本表结构 (DCS003 抑制理由：测试初始化)
#pragma warning disable DCS003
        using (var conn = new SqliteConnection("Data Source=" + _dbPath))
#pragma warning restore DCS003
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
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
                    project_name TEXT,
                    idempotency_key TEXT,
                    locked_by TEXT,
                    lock_expires_at TEXT
                );";
            cmd.ExecuteNonQuery();
        }

        _outboxJobService = new OutboxJobService(_config, NullLogger<OutboxJobService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_dbDir))
            {
                Directory.Delete(_dbDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task EnqueueWithIdempotencyAsync_ShouldPreventDuplicateEnqueuing()
    {
        var idempotencyKey = "batch:daily-sync:202609241800";

        // 第一次入队应当成功
        var firstResult = await _outboxJobService.EnqueueWithIdempotencyAsync(
            "batch_job",
            "{\"id\":\"daily-sync\"}",
            "test-project",
            idempotencyKey
        );
        Assert.True(firstResult);

        // 第二次使用相同幂等键入队应当被跳过
        var secondResult = await _outboxJobService.EnqueueWithIdempotencyAsync(
            "batch_job",
            "{\"id\":\"daily-sync\"}",
            "test-project",
            idempotencyKey
        );
        Assert.False(secondResult);

        // 验证队列中仅存在 1 条记录
        var pending = await _outboxJobService.GetPendingJobsAsync(10);
        Assert.Single(pending);
        Assert.Equal(idempotencyKey, pending[0].IdempotencyKey);
    }

    [Fact]
    public async Task LockJobAsync_WithWorkerLease_ShouldRecordWorkerAndExpiry()
    {
        await _outboxJobService.EnqueueAsync("batch_job", "{}", "proj");
        var pending = await _outboxJobService.GetPendingJobsAsync(1);
        Assert.Single(pending);

        var jobId = pending[0].Id;
        var locked = await _outboxJobService.LockJobAsync(jobId, "worker_node_a", TimeSpan.FromMinutes(10));
        Assert.True(locked);

        // 另一个 Worker 尝试并发锁定未过期的 Job 应当失败
        var lockedByAnother = await _outboxJobService.LockJobAsync(jobId, "worker_node_b", TimeSpan.FromMinutes(10));
        Assert.False(lockedByAnother);
    }

    [Fact]
    public async Task RecoverStaleRunningJobsAsync_ShouldResetExpiredWorkerLocks()
    {
        await _outboxJobService.EnqueueAsync("batch_job", "{}", "proj");
        var pending = await _outboxJobService.GetPendingJobsAsync(1);
        var jobId = pending[0].Id;

        // 锁定但设置较短的过期（这里模拟过期）
        await _outboxJobService.LockJobAsync(jobId, "dead_worker", TimeSpan.FromMilliseconds(1));
        await Task.Delay(50); // 等待租约过期

        // 执行回收
        var recovered = await _outboxJobService.RecoverStaleRunningJobsAsync(TimeSpan.FromSeconds(0));
        Assert.True(recovered >= 1);

        // 检查任务是否重新变成 Pending
        var availableAgain = await _outboxJobService.GetPendingJobsAsync(1);
        Assert.Single(availableAgain);
        Assert.Equal(jobId, availableAgain[0].Id);
        Assert.Equal("Pending", availableAgain[0].Status);
    }

    [Fact]
    public async Task DbConnectionFactory_ThinFacade_ShouldDelegateToConnectionManager()
    {
        var projectManagerMock = new Mock<ProjectManager>();
        var projectInfo = new ProjectInfo
        {
            Name = "demo-facade",
            DatabaseType = "sqlite",
            ConnectionString = "Data Source=:memory:"
        };
        ProjectInfo? outProject = projectInfo;
        projectManagerMock.Setup(pm => pm.TryGet("demo-facade", out outProject)).Returns(true);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var loggerFactoryMock = new Mock<Microsoft.Extensions.Logging.ILoggerFactory>();
        loggerFactoryMock.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<Microsoft.Extensions.Logging.ILogger>().Object);
        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        var options = Options.Create(new ConnectionPoolOptions
        {
            MaxPoolSize = 48,
            IdleTimeoutMs = 90000,
            MaxLifetimeMs = 180000
        });

        var connectionManager = new ConnectionManager(
            projectManagerMock.Object,
            NullLogger<ConnectionManager>.Instance,
            scopeFactoryMock.Object,
            loggerFactoryMock.Object,
            httpContextAccessorMock.Object,
            options.Value
        );

        // 测试面向数据库层的薄门面
        var factory = new NetYamlForge.Services.Database.DbConnectionFactory(connectionManager, projectManagerMock.Object);

        using var connection = await factory.CreateConnectionAsync("demo-facade");
        Assert.NotNull(connection);
        Assert.Equal(ConnectionState.Open, connection.State);

        var stats = connectionManager.GetPoolStats("demo-facade");
        Assert.Equal(1, stats.TotalCreated);
        Assert.Equal(1, stats.CurrentActiveConnections);

        connectionManager.ReleaseConnection("demo-facade", connection);
        Assert.Equal(0, stats.CurrentActiveConnections);
    }
}
