using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Services;
using NetYamlForge.Services.Connection;
using Xunit;

namespace NetYamlForge.Tests.Services.Connection;

public class ConnectionPoolTests
{
    private readonly Mock<ILogger<ConnectionPool>> _loggerMock;
    private readonly ConnectionPoolOptions _testOptions;

    public ConnectionPoolTests()
    {
        _loggerMock = new Mock<ILogger<ConnectionPool>>();
        _testOptions = new ConnectionPoolOptions
        {
            MaxPoolSize = 5,
            IdleTimeoutMs = 1000, // 1 秒用于测试
            MaxLifetimeMs = 5000, // 5 秒用于测试
            Enabled = true
        };
    }

    [Fact]
    public async Task AcquireAsync_ShouldCreateNewConnection_WhenPoolIsEmpty()
    {
        // Arrange
        var createdCount = 0;
        IDbConnection Factory()
        {
            createdCount++;
            return new SqliteConnection("Data Source=:memory:");
        }

        var pool = new ConnectionPool("test-project", Factory, _testOptions, _loggerMock.Object);

        // Act
        var connection1 = await pool.AcquireAsync();
        var connection2 = await pool.AcquireAsync();

        // Assert
        Assert.NotNull(connection1);
        Assert.NotNull(connection2);
        Assert.NotSame(connection1, connection2);
        Assert.Equal(2, createdCount);

        pool.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ShouldReuseConnection_WhenReleasedToPool()
    {
        // Arrange
        var createdCount = 0;
        IDbConnection Factory()
        {
            createdCount++;
            return new SqliteConnection("Data Source=:memory:");
        }

        var pool = new ConnectionPool("test-project", Factory, _testOptions, _loggerMock.Object);

        // Act - 获取连接并释放
        var connection1 = await pool.AcquireAsync();
        pool.Release(connection1);

        // 再次获取应该复用
        var connection2 = await pool.AcquireAsync();

        // Assert
        Assert.Same(connection1, connection2);
        Assert.Equal(1, createdCount);
        Assert.Equal(1, pool.Stats.TotalReused);

        pool.Dispose();
    }

    [Fact]
    public async Task Release_ShouldNotExceedMaxPoolSize()
    {
        // Arrange
        var createdConnections = new List<IDbConnection>();
        IDbConnection Factory()
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            createdConnections.Add(conn);
            return conn;
        }

        var pool = new ConnectionPool("test-project", Factory, _testOptions, _loggerMock.Object);

        // Act - 创建超过 MaxPoolSize 的连接
        var connections = new List<IDbConnection>();
        for (int i = 0; i < 10; i++)
        {
            connections.Add(await pool.AcquireAsync());
        }

        // 释放所有连接
        foreach (var conn in connections)
        {
            pool.Release(conn);
        }

        // Assert - 池中最多保留 MaxPoolSize 个连接
        Assert.Equal(5, _testOptions.MaxPoolSize);
        Assert.Equal(5, pool.Stats.CurrentPooledConnections);

        pool.Dispose();
    }

    [Fact]
    public async Task Stats_ShouldTrackCreatedAndReusedConnections()
    {
        // Arrange
        IDbConnection Factory() => new SqliteConnection("Data Source=:memory:");

        var pool = new ConnectionPool("test-project", Factory, _testOptions, _loggerMock.Object);

        // Act - 创建和复用连接
        var conn1 = await pool.AcquireAsync();
        pool.Release(conn1);

        var conn2 = await pool.AcquireAsync();
        pool.Release(conn2);

        var conn3 = await pool.AcquireAsync();
        pool.Release(conn3);

        // Assert
        Assert.Equal(1, pool.Stats.TotalCreated);
        Assert.Equal(2, pool.Stats.TotalReused);
        Assert.Equal(0, pool.Stats.TotalDisposed);

        pool.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ShouldThrow_WhenDisposed()
    {
        // Arrange
        IDbConnection Factory() => new SqliteConnection("Data Source=:memory:");
        var pool = new ConnectionPool("test-project", Factory, _testOptions, _loggerMock.Object);
        pool.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.AcquireAsync());
    }

    [Fact]
    public void Release_ShouldHandleNullConnection()
    {
        // Arrange
        IDbConnection Factory() => new SqliteConnection("Data Source=:memory:");
        var pool = new ConnectionPool("test-project", Factory, _testOptions, _loggerMock.Object);

        // Act - 不应抛出异常
        pool.Release(null!);

        // Cleanup
        pool.Dispose();
    }
}

public class ConnectionManagerTests
{
    private readonly Mock<ProjectManager> _projectManagerMock;
    private readonly Mock<ILogger<ConnectionManager>> _loggerMock;
    private readonly ConnectionPoolOptions _testOptions;

    public ConnectionManagerTests()
    {
        _projectManagerMock = new Mock<ProjectManager>();
        _loggerMock = new Mock<ILogger<ConnectionManager>>();
        _testOptions = new ConnectionPoolOptions
        {
            MaxPoolSize = 5,
            IdleTimeoutMs = 1000,
            MaxLifetimeMs = 5000,
            Enabled = true
        };
    }

    [Fact]
    public async Task GetConnectionAsync_ShouldThrow_WhenProjectNotFound()
    {
        // Arrange
        _projectManagerMock.Setup(pm => pm.TryGet(It.IsAny<string>(), out It.Ref<ProjectInfo>.IsAny))
            .Returns(false);

        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        
        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            serviceProviderMock.Object,
            loggerFactoryMock.Object,
            _testOptions);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.GetConnectionAsync("non-existent-project"));
    }

    [Fact]
    public async Task GetConnectionAsync_ShouldReturnOpenConnection()
    {
        // Arrange
        var projectInfo = new ProjectInfo
        {
            Name = "test-project",
            DatabaseType = "sqlite",
            ConnectionString = "Data Source=:memory:"
        };

        ProjectInfo? outProject = projectInfo;
        _projectManagerMock.Setup(pm => pm.TryGet("test-project", out outProject))
            .Returns(true);

        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        
        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            serviceProviderMock.Object,
            loggerFactoryMock.Object,
            _testOptions);

        // Act
        var connection = await manager.GetConnectionAsync("test-project");

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(ConnectionState.Open, connection.State);

        manager.Dispose();
    }

    [Fact]
    public void GetAllPoolStats_ShouldReturnStatsForAllProjects()
    {
        // Arrange
        var project1 = new ProjectInfo
        {
            Name = "project1",
            DatabaseType = "sqlite",
            ConnectionString = "Data Source=:memory:"
        };
        var project2 = new ProjectInfo
        {
            Name = "project2",
            DatabaseType = "sqlite",
            ConnectionString = "Data Source=:memory:"
        };

        ProjectInfo? outProject1 = project1;
        ProjectInfo? outProject2 = project2;

        _projectManagerMock.Setup(pm => pm.TryGet("project1", out outProject1)).Returns(true);
        _projectManagerMock.Setup(pm => pm.TryGet("project2", out outProject2)).Returns(true);

        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        
        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            serviceProviderMock.Object,
            loggerFactoryMock.Object,
            _testOptions);

        // Act - 先创建一些连接
        var conn1 = manager.GetConnectionAsync("project1").GetAwaiter().GetResult();
        var conn2 = manager.GetConnectionAsync("project2").GetAwaiter().GetResult();

        var stats = manager.GetAllPoolStats();

        // Assert
        Assert.Equal(2, stats.Count);
        Assert.True(stats.ContainsKey("project1"));
        Assert.True(stats.ContainsKey("project2"));

        manager.Dispose();
    }

    [Fact]
    public void ResetPool_ShouldRemoveAndDisposePool()
    {
        // Arrange
        var projectInfo = new ProjectInfo
        {
            Name = "test-project",
            DatabaseType = "sqlite",
            ConnectionString = "Data Source=:memory:"
        };

        ProjectInfo? outProject = projectInfo;
        _projectManagerMock.Setup(pm => pm.TryGet("test-project", out outProject))
            .Returns(true);

        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            serviceProviderMock.Object,
            loggerFactoryMock.Object,
            _testOptions);

        // Act - 创建连接池
        var conn = manager.GetConnectionAsync("test-project").GetAwaiter().GetResult();
        manager.ReleaseConnection("test-project", conn);

        // 验证池存在
        Assert.True(manager.GetAllPoolStats().ContainsKey("test-project"));

        // 重置池
        manager.ResetPool("test-project");

        // Assert - 池应被移除
        Assert.False(manager.GetAllPoolStats().ContainsKey("test-project"));
        
        manager.Dispose();
    }

    [Fact]
    public void ResetAllPools_ShouldClearAllPools()
    {
        // Arrange
        var project1 = new ProjectInfo
        {
            Name = "project1",
            DatabaseType = "sqlite",
            ConnectionString = "Data Source=:memory:"
        };
        var project2 = new ProjectInfo
        {
            Name = "project2",
            DatabaseType = "sqlite",
            ConnectionString = "Data Source=:memory:"
        };

        ProjectInfo? outProject1 = project1;
        ProjectInfo? outProject2 = project2;

        _projectManagerMock.Setup(pm => pm.TryGet("project1", out outProject1)).Returns(true);
        _projectManagerMock.Setup(pm => pm.TryGet("project2", out outProject2)).Returns(true);

        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            serviceProviderMock.Object,
            loggerFactoryMock.Object,
            _testOptions);

        // Act - 创建一些连接池
        var conn1 = manager.GetConnectionAsync("project1").GetAwaiter().GetResult();
        var conn2 = manager.GetConnectionAsync("project2").GetAwaiter().GetResult();
        manager.ReleaseConnection("project1", conn1);
        manager.ReleaseConnection("project2", conn2);

        // 验证池存在
        Assert.Equal(2, manager.GetAllPoolStats().Count);

        // 重置所有池
        manager.ResetAllPools();

        // Assert - 所有池应被清除
        Assert.Empty(manager.GetAllPoolStats());

        manager.Dispose();
    }
}
