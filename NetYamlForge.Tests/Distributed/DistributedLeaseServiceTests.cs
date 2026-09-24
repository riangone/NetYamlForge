using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NetYamlForge.Services.Distributed;
using Xunit;

namespace NetYamlForge.Tests.Distributed;

public class DistributedLeaseServiceTests : IDisposable
{
    private readonly string _dbDir;
    private readonly string _dbPath;
    private readonly IConfiguration _config;
    private readonly DistributedLeaseService _leaseService;

    public DistributedLeaseServiceTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), "nyf-lease-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _dbPath = Path.Combine(_dbDir, "system.db");

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "SystemDbPath", _dbPath }
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _leaseService = new DistributedLeaseService(_config, NullLogger<DistributedLeaseService>.Instance);
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
    public async Task TryAcquireLeaseAsync_FirstHolder_ShouldSucceed()
    {
        var acquired = await _leaseService.TryAcquireLeaseAsync("test_lease_1", "node_1", TimeSpan.FromMinutes(1));
        Assert.True(acquired);

        var info = await _leaseService.GetLeaseAsync("test_lease_1");
        Assert.NotNull(info);
        Assert.Equal("node_1", info.HolderId);
        Assert.False(info.IsExpired);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_SecondHolder_ShouldFailWhenNotExpired()
    {
        var acquired1 = await _leaseService.TryAcquireLeaseAsync("test_lease_2", "node_1", TimeSpan.FromMinutes(5));
        Assert.True(acquired1);

        var acquired2 = await _leaseService.TryAcquireLeaseAsync("test_lease_2", "node_2", TimeSpan.FromMinutes(5));
        Assert.False(acquired2);

        var info = await _leaseService.GetLeaseAsync("test_lease_2");
        Assert.NotNull(info);
        Assert.Equal("node_1", info.HolderId);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_SameHolder_ShouldRenewAndSucceed()
    {
        var acquired1 = await _leaseService.TryAcquireLeaseAsync("test_lease_3", "node_1", TimeSpan.FromMinutes(1));
        Assert.True(acquired1);

        var acquired2 = await _leaseService.TryAcquireLeaseAsync("test_lease_3", "node_1", TimeSpan.FromMinutes(2));
        Assert.True(acquired2);
    }

    [Fact]
    public async Task RenewLeaseAsync_ValidHolder_ShouldSucceed()
    {
        await _leaseService.TryAcquireLeaseAsync("test_lease_4", "node_1", TimeSpan.FromMinutes(1));
        var renewed = await _leaseService.RenewLeaseAsync("test_lease_4", "node_1", TimeSpan.FromMinutes(3));
        Assert.True(renewed);

        var renewedWrongNode = await _leaseService.RenewLeaseAsync("test_lease_4", "node_2", TimeSpan.FromMinutes(3));
        Assert.False(renewedWrongNode);
    }

    [Fact]
    public async Task ReleaseLeaseAsync_ShouldAllowAnotherHolderToAcquireImmediately()
    {
        await _leaseService.TryAcquireLeaseAsync("test_lease_5", "node_1", TimeSpan.FromMinutes(5));
        await _leaseService.ReleaseLeaseAsync("test_lease_5", "node_1");

        var acquiredByNode2 = await _leaseService.TryAcquireLeaseAsync("test_lease_5", "node_2", TimeSpan.FromMinutes(5));
        Assert.True(acquiredByNode2);

        var info = await _leaseService.GetLeaseAsync("test_lease_5");
        Assert.NotNull(info);
        Assert.Equal("node_2", info.HolderId);
    }
}
