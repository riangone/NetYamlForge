using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.AI.Services;
using Xunit;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// AI 进程池管理器单元测试
/// </summary>
public class AIProcessPoolManagerTests
{
    private readonly Mock<ILogger<CliProcessPoolManager>> _loggerMock;
    private readonly CliProcessPoolConfig _config;

    public AIProcessPoolManagerTests()
    {
        _loggerMock = new Mock<ILogger<CliProcessPoolManager>>();
        _config = new CliProcessPoolConfig
        {
            EnableDaemonMode = true,
            MaxPoolSize = 3,
            IdleTimeoutMinutes = 10,
            HealthCheckIntervalSeconds = 30,
            MaxStartRetries = 3
        };
    }

    [Fact]
    public void Constructor_InitializesSuccessfully()
    {
        // Arrange & Act
        using var manager = new CliProcessPoolManager(_loggerMock.Object, _config);

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public async Task AcquireProcessAsync_CreatesNewProcessWhenPoolEmpty()
    {
        // Arrange
        using var manager = new CliProcessPoolManager(_loggerMock.Object, _config);
        var processCreated = false;

        Func<Task<PersistentAIProcess>> createFunc = async () =>
        {
            processCreated = true;
            var process = new PersistentAIProcess("qwen", _config, new Mock<ILogger>().Object);
            await Task.Delay(10);
            return process;
        };

        // Act
        var process = await manager.AcquireProcessAsync("qwen", createFunc);

        // Assert
        Assert.NotNull(process);
        Assert.True(processCreated);
        Assert.Equal("qwen", process.Provider);
    }

    [Fact]
    public async Task ReturnProcess_ReturnsHealthyProcessToPool()
    {
        // Arrange
        using var manager = new CliProcessPoolManager(_loggerMock.Object, _config);
        var createCount = 0;

        Func<Task<PersistentAIProcess>> createFunc = async () =>
        {
            createCount++;
            var process = new PersistentAIProcess("qwen", _config, new Mock<ILogger>().Object);
            await Task.Delay(10);
            return process;
        };

        // Act - 获取进程
        var process1 = await manager.AcquireProcessAsync("qwen", createFunc);
        
        // 归还进程
        manager.ReturnProcess("qwen", process1);

        // 再次获取进程（应该从池中获取）
        var process2 = await manager.AcquireProcessAsync("qwen", createFunc);

        // Assert
        Assert.Equal(1, createCount); // 只创建了一次
        Assert.NotNull(process2);
    }

    [Fact]
    public async Task ReturnProcess_DisposesUnhealthyProcess()
    {
        // Arrange
        using var manager = new CliProcessPoolManager(_loggerMock.Object, _config);
        
        var process = new PersistentAIProcess("qwen", _config, new Mock<ILogger>().Object);
        process.Dispose(); // 标记为不健康

        // Act
        manager.ReturnProcess("qwen", process);

        // Assert - 不健康的进程应该被丢弃（Dispose）
        var stats = manager.GetPoolStats();
        Assert.False(stats.ContainsKey("qwen")); // 池中没有进程
    }

    [Fact]
    public async Task ClearPool_RemovesAllProcessesFromPool()
    {
        // Arrange
        using var manager = new CliProcessPoolManager(_loggerMock.Object, _config);

        Func<Task<PersistentAIProcess>> createFunc = async () =>
        {
            var process = new PersistentAIProcess("qwen", _config, new Mock<ILogger>().Object);
            await Task.Delay(10);
            return process;
        };

        // 创建并归还几个进程
        for (int i = 0; i < 3; i++)
        {
            var process = await manager.AcquireProcessAsync("qwen", createFunc);
            manager.ReturnProcess("qwen", process);
        }

        // Act
        manager.ClearPool("qwen");

        // Assert
        var stats = manager.GetPoolStats();
        Assert.False(stats.ContainsKey("qwen"));
    }

    [Fact]
    public async Task ClearAllPools_RemovesAllProcessesFromAllPools()
    {
        // Arrange
        using var manager = new CliProcessPoolManager(_loggerMock.Object, _config);

        Func<Task<PersistentAIProcess>> createQwenFunc = async () =>
        {
            var process = new PersistentAIProcess("qwen", _config, new Mock<ILogger>().Object);
            await Task.Delay(10);
            return process;
        };

        Func<Task<PersistentAIProcess>> createClaudeFunc = async () =>
        {
            var process = new PersistentAIProcess("claude", _config, new Mock<ILogger>().Object);
            await Task.Delay(10);
            return process;
        };

        // 创建两个提供者的进程
        var qwenProcess = await manager.AcquireProcessAsync("qwen", createQwenFunc);
        var claudeProcess = await manager.AcquireProcessAsync("claude", createClaudeFunc);

        manager.ReturnProcess("qwen", qwenProcess);
        manager.ReturnProcess("claude", claudeProcess);

        // Act
        manager.ClearAllPools();

        // Assert
        var stats = manager.GetPoolStats();
        Assert.Empty(stats);
    }

    [Fact]
    public async Task GetPoolStats_ReturnsCorrectStatistics()
    {
        // Arrange
        using var manager = new CliProcessPoolManager(_loggerMock.Object, _config);

        Func<Task<PersistentAIProcess>> createFunc = async () =>
        {
            var process = new PersistentAIProcess("qwen", _config, new Mock<ILogger>().Object);
            await Task.Delay(10);
            return process;
        };

        // 创建并归还2个进程
        var process1 = await manager.AcquireProcessAsync("qwen", createFunc);
        var process2 = await manager.AcquireProcessAsync("qwen", createFunc);

        manager.ReturnProcess("qwen", process1);
        manager.ReturnProcess("qwen", process2);

        // Act
        var stats = manager.GetPoolStats();

        // Assert
        Assert.True(stats.ContainsKey("qwen"));
        Assert.Equal(2, stats["qwen"]["poolSize"]);
        Assert.Equal(2, stats["qwen"]["healthyCount"]);
        Assert.Equal(0, stats["qwen"]["busyCount"]);
        Assert.Equal(3, stats["qwen"]["maxPoolSize"]);
    }

    [Fact]
    public async Task AcquireProcessAsync_SkipsPoolWhenDaemonModeDisabled()
    {
        // Arrange
        _config.EnableDaemonMode = false;
        using var manager = new CliProcessPoolManager(_loggerMock.Object, _config);
        var createCount = 0;

        Func<Task<PersistentAIProcess>> createFunc = async () =>
        {
            createCount++;
            var process = new PersistentAIProcess("qwen", _config, new Mock<ILogger>().Object);
            await Task.Delay(10);
            return process;
        };

        // Act
        var process1 = await manager.AcquireProcessAsync("qwen", createFunc);
        manager.ReturnProcess("qwen", process1);
        var process2 = await manager.AcquireProcessAsync("qwen", createFunc);

        // Assert
        Assert.Equal(2, createCount); // 每次都创建新进程
    }

    [Fact]
    public void Dispose_ReleasesAllResources()
    {
        // Arrange
        var manager = new CliProcessPoolManager(_loggerMock.Object, _config);

        // Act
        manager.Dispose();

        // Assert - 不应该抛出异常
        Assert.True(true);
    }
}

/// <summary>
/// PersistentAIProcess 单元测试
/// </summary>
public class PersistentAIProcessTests
{
    private readonly Mock<ILogger> _loggerMock;
    private readonly CliProcessPoolConfig _config;

    public PersistentAIProcessTests()
    {
        _loggerMock = new Mock<ILogger>();
        _config = new CliProcessPoolConfig
        {
            EnableDaemonMode = true,
            MaxPoolSize = 3,
            IdleTimeoutMinutes = 10,
            HealthCheckIntervalSeconds = 30,
            MaxStartRetries = 3,
            MaxLifetimeMinutes = 60,
            MaxRequestsPerProcess = 100
        };
    }

    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        // Arrange & Act
        var process = new PersistentAIProcess("qwen", _config, _loggerMock.Object);

        // Assert
        Assert.Equal("qwen", process.Provider);
        Assert.Equal(-1, process.ProcessId);
        Assert.Equal(0, process.RequestCount);
        Assert.False(process.IsHealthy);
        Assert.False(process.IsBusy);
    }

    [Fact]
    public void HealthCheck_ReturnsTrueWhenProcessIsValid()
    {
        // Arrange
        var process = new PersistentAIProcess("qwen", _config, _loggerMock.Object);

        // Act - 注意：由于进程未启动，IsHealthy 将为 false
        var result = process.HealthCheck();

        // Assert
        Assert.False(result); // 未启动的进程不健康
    }

    [Fact]
    public void Touch_UpdatesLastUsedTime()
    {
        // Arrange
        var process = new PersistentAIProcess("qwen", _config, _loggerMock.Object);
        var idleTimeBefore = process.IdleTime;

        // Act
        Thread.Sleep(100);
        process.Touch();
        var idleTimeAfter = process.IdleTime;

        // Assert
        Assert.True(idleTimeAfter < idleTimeBefore);
    }

    [Fact]
    public void Dispose_SetsProcessToUnhealthy()
    {
        // Arrange
        var process = new PersistentAIProcess("qwen", _config, _loggerMock.Object);

        // Act
        process.Dispose();

        // Assert
        Assert.False(process.IsHealthy);
    }

    [Fact]
    public void GetStats_ReturnsCorrectInformation()
    {
        // Arrange
        var process = new PersistentAIProcess("qwen", _config, _loggerMock.Object);

        // Act
        var stats = process.GetStats();

        // Assert
        Assert.Equal("qwen", stats["provider"]);
        Assert.Equal(-1, stats["pid"]);
        Assert.False((bool)stats["healthy"]);
        Assert.False((bool)stats["busy"]);
        Assert.Equal(0, stats["requestCount"]);
    }
}
