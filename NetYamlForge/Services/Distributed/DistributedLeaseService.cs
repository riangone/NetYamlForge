using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Connection;

namespace NetYamlForge.Services.Distributed;

/// <summary>
/// 基于系统库与原子 SQL 的分布式租约服务实现
/// 支持分布式 Leader 抢占、安全心跳续期与优雅释放
/// </summary>
public class DistributedLeaseService : IDistributedLeaseService
{
    private readonly string _systemDbConnectionString;
    private readonly ILogger<DistributedLeaseService> _logger;
    private bool _tableEnsured;

    public DistributedLeaseService(IConfiguration config, ILogger<DistributedLeaseService> logger)
    {
        _logger = logger;
        var dbPath = config["SystemDbPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "system.db");
        _systemDbConnectionString = dbPath.Contains(';') ? dbPath : new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
    }

    private async Task<IDbConnection> GetConnectionAsync()
    {
#pragma warning disable DCS003
        var conn = new SqliteConnection(_systemDbConnectionString);
#pragma warning restore DCS003
        await conn.OpenAsync();
        await SqliteConnectionHardening.ApplyAsync(conn);

        if (!_tableEnsured)
        {
            await SqliteWriteGate.RunAsync(conn, async () =>
            {
                await conn.ExecuteAsync(@"
                    CREATE TABLE IF NOT EXISTS nyf_leases (
                        lease_name TEXT PRIMARY KEY,
                        holder_id TEXT NOT NULL,
                        acquired_at TEXT NOT NULL,
                        expires_at TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_nyf_leases_expires_at ON nyf_leases(expires_at);
                ");
            });
            _tableEnsured = true;
        }

        return conn;
    }

    public async Task<bool> TryAcquireLeaseAsync(string leaseName, string holderId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(duration);
        var nowStr = now.ToString("yyyy-MM-dd HH:mm:ss");
        var expiresStr = expiresAt.ToString("yyyy-MM-dd HH:mm:ss");

        using var conn = await GetConnectionAsync();
        return await SqliteWriteGate.RunAsync(conn, async () =>
        {
            // 1. 尝试抢占已过期的租约或者自身已持有的租约
            var updateCount = await conn.ExecuteAsync(@"
                UPDATE nyf_leases
                SET holder_id = @HolderId,
                    acquired_at = @AcquiredAt,
                    expires_at = @ExpiresAt
                WHERE lease_name = @LeaseName
                  AND (expires_at < @Now OR holder_id = @HolderId)",
                new
                {
                    LeaseName = leaseName,
                    HolderId = holderId,
                    AcquiredAt = nowStr,
                    ExpiresAt = expiresStr,
                    Now = nowStr
                });

            if (updateCount > 0)
            {
                _logger.LogDebug("Lease {LeaseName} acquired or renewed by {HolderId} until {ExpiresAt}", leaseName, holderId, expiresStr);
                return true;
            }

            // 2. 若记录不存在，尝试插入新租约（INSERT OR IGNORE 保证并发无冲突）
            var insertCount = await conn.ExecuteAsync(@"
                INSERT OR IGNORE INTO nyf_leases (lease_name, holder_id, acquired_at, expires_at)
                VALUES (@LeaseName, @HolderId, @AcquiredAt, @ExpiresAt)",
                new
                {
                    LeaseName = leaseName,
                    HolderId = holderId,
                    AcquiredAt = nowStr,
                    ExpiresAt = expiresStr
                });

            if (insertCount > 0)
            {
                _logger.LogDebug("Lease {LeaseName} initially created and acquired by {HolderId} until {ExpiresAt}", leaseName, holderId, expiresStr);
                return true;
            }

            return false;
        });
    }

    public async Task<bool> RenewLeaseAsync(string leaseName, string holderId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(duration);
        var nowStr = now.ToString("yyyy-MM-dd HH:mm:ss");
        var expiresStr = expiresAt.ToString("yyyy-MM-dd HH:mm:ss");

        using var conn = await GetConnectionAsync();
        return await SqliteWriteGate.RunAsync(conn, async () =>
        {
            var count = await conn.ExecuteAsync(@"
                UPDATE nyf_leases
                SET expires_at = @ExpiresAt
                WHERE lease_name = @LeaseName
                  AND holder_id = @HolderId
                  AND expires_at >= @Now",
                new
                {
                    LeaseName = leaseName,
                    HolderId = holderId,
                    ExpiresAt = expiresStr,
                    Now = nowStr
                });

            return count > 0;
        });
    }

    public async Task ReleaseLeaseAsync(string leaseName, string holderId, CancellationToken cancellationToken = default)
    {
        using var conn = await GetConnectionAsync();
        await SqliteWriteGate.RunAsync(conn, async () =>
        {
            await conn.ExecuteAsync(@"
                DELETE FROM nyf_leases
                WHERE lease_name = @LeaseName AND holder_id = @HolderId",
                new { LeaseName = leaseName, HolderId = holderId });
        });

        _logger.LogDebug("Lease {LeaseName} released by {HolderId}", leaseName, holderId);
    }

    public async Task<DistributedLeaseInfo?> GetLeaseAsync(string leaseName, CancellationToken cancellationToken = default)
    {
        using var conn = await GetConnectionAsync();
        var entity = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT lease_name as LeaseName, holder_id as HolderId, acquired_at as AcquiredAt, expires_at as ExpiresAt
            FROM nyf_leases
            WHERE lease_name = @LeaseName",
            new { LeaseName = leaseName });

        if (entity == null) return null;

        return new DistributedLeaseInfo
        {
            LeaseName = (string)entity.LeaseName,
            HolderId = (string)entity.HolderId,
            AcquiredAt = DateTime.Parse((string)entity.AcquiredAt),
            ExpiresAt = DateTime.Parse((string)entity.ExpiresAt)
        };
    }
}
