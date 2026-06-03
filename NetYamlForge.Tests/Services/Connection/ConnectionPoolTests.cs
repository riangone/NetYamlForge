using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Services;
using NetYamlForge.Services.Connection;
using Xunit;

namespace NetYamlForge.Tests.Services.Connection;


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

        var scopeFactory = new Mock<IServiceScopeFactory>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        
        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            scopeFactory.Object,
            loggerFactory.Object,
            httpContextAccessor.Object,
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

        var scopeFactory = new Mock<IServiceScopeFactory>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        
        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            scopeFactory.Object,
            loggerFactory.Object,
            httpContextAccessor.Object,
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

        var scopeFactory = new Mock<IServiceScopeFactory>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        
        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            scopeFactory.Object,
            loggerFactory.Object,
            httpContextAccessor.Object,
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

        var scopeFactory = new Mock<IServiceScopeFactory>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        
        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            scopeFactory.Object,
            loggerFactory.Object,
            httpContextAccessor.Object,
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

        var scopeFactory = new Mock<IServiceScopeFactory>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(lf => lf.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        
        var manager = new ConnectionManager(
            _projectManagerMock.Object,
            _loggerMock.Object,
            scopeFactory.Object,
            loggerFactory.Object,
            httpContextAccessor.Object,
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
