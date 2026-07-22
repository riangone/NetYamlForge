#pragma warning disable DCS001

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;

namespace NetYamlForge.Services.WebPush;

public class PushNotificationService : IPushNotificationService
{
    private readonly IDbConnection _db;

    public PushNotificationService(IDbConnection db)
    {
        _db = db;
    }

    public async Task EnqueueAsync(string tenantId, string userId, string title, string body, string? url = null)
    {
        await EnsurePushOutboxTableExistsAsync();

        var sql = @"
            INSERT INTO ""PushOutbox"" (""Id"", ""TenantId"", ""UserId"", ""Title"", ""Body"", ""Url"", ""State"", ""Attempts"", ""NextAttemptAt"", ""CreatedAt"")
            VALUES (@Id, @TenantId, @UserId, @Title, @Body, @Url, @State, @Attempts, @NextAttemptAt, @CreatedAt)";

        await _db.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            UserId = userId,
            Title = title,
            Body = body,
            Url = url,
            State = 0,
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    private async Task EnsurePushOutboxTableExistsAsync()
    {
        await _db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ""PushOutbox"" (
                ""Id"" TEXT PRIMARY KEY,
                ""TenantId"" TEXT NOT NULL,
                ""UserId"" TEXT NOT NULL,
                ""Title"" TEXT NOT NULL,
                ""Body"" TEXT NOT NULL,
                ""Url"" TEXT,
                ""State"" INTEGER NOT NULL,
                ""Attempts"" INTEGER NOT NULL,
                ""NextAttemptAt"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""LastError"" TEXT
            );");
    }
}
