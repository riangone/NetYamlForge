#pragma warning disable DCS001

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace NetYamlForge.Services.WebPush;

/// <summary>
/// PushSubscription の永続化。WebhookOutbox と同じ「CREATE TABLE IF NOT EXISTS」方式の
/// 自己マイグレーションを行い、Dapper + IDbConnection でDB種別非依存に実装します。
/// </summary>
public class PushSubscriptionStore : IPushSubscriptionStore
{
    private readonly IDbConnection _db;

    public PushSubscriptionStore(IDbConnection db)
    {
        _db = db;
    }

    public async Task EnsureSchemaAsync()
    {
        await _db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ""PushSubscription"" (
                ""Id"" TEXT PRIMARY KEY,
                ""TenantId"" TEXT NOT NULL,
                ""UserId"" TEXT NOT NULL,
                ""Endpoint"" TEXT NOT NULL,
                ""P256dh"" TEXT NOT NULL,
                ""Auth"" TEXT NOT NULL,
                ""UserAgent"" TEXT,
                ""CreatedAt"" TEXT NOT NULL
            );");
    }

    public async Task<PushSubscriptionRecord> SubscribeAsync(string tenantId, string userId, string endpoint, string p256dh, string auth, string? userAgent)
    {
        await EnsureSchemaAsync();

        var existing = await _db.QueryFirstOrDefaultAsync<PushSubscriptionRecord>(
            @"SELECT * FROM ""PushSubscription"" WHERE ""TenantId"" = @TenantId AND ""Endpoint"" = @Endpoint",
            new { TenantId = tenantId, Endpoint = endpoint });

        if (existing != null)
        {
            await _db.ExecuteAsync(
                @"UPDATE ""PushSubscription""
                  SET ""UserId"" = @UserId, ""P256dh"" = @P256dh, ""Auth"" = @Auth, ""UserAgent"" = @UserAgent
                  WHERE ""Id"" = @Id",
                new { existing.Id, UserId = userId, P256dh = p256dh, Auth = auth, UserAgent = userAgent });

            existing.UserId = userId;
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.UserAgent = userAgent;
            return existing;
        }

        var record = new PushSubscriptionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            UserId = userId,
            Endpoint = endpoint,
            P256dh = p256dh,
            Auth = auth,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        };

        await _db.ExecuteAsync(
            @"INSERT INTO ""PushSubscription"" (""Id"", ""TenantId"", ""UserId"", ""Endpoint"", ""P256dh"", ""Auth"", ""UserAgent"", ""CreatedAt"")
              VALUES (@Id, @TenantId, @UserId, @Endpoint, @P256dh, @Auth, @UserAgent, @CreatedAt)",
            record);

        return record;
    }

    public async Task UnsubscribeAsync(string tenantId, string endpoint)
    {
        await EnsureSchemaAsync();
        await _db.ExecuteAsync(
            @"DELETE FROM ""PushSubscription"" WHERE ""TenantId"" = @TenantId AND ""Endpoint"" = @Endpoint",
            new { TenantId = tenantId, Endpoint = endpoint });
    }

    public async Task RemoveByIdAsync(string id)
    {
        await EnsureSchemaAsync();
        await _db.ExecuteAsync(@"DELETE FROM ""PushSubscription"" WHERE ""Id"" = @Id", new { Id = id });
    }

    public async Task<IReadOnlyList<PushSubscriptionRecord>> GetByUserAsync(string tenantId, string userId)
    {
        await EnsureSchemaAsync();
        var rows = await _db.QueryAsync<PushSubscriptionRecord>(
            @"SELECT * FROM ""PushSubscription"" WHERE ""TenantId"" = @TenantId AND ""UserId"" = @UserId",
            new { TenantId = tenantId, UserId = userId });
        return rows.ToList();
    }
}
