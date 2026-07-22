#pragma warning disable DCS001

using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;

namespace NetYamlForge.Services.WebPush;

/// <summary>
/// PushOutbox をポーリングし、対象ユーザーの全購読先へ Web Push を送信するバックグラウンドサービス。
/// WebhookOutboxPoller と同じ Outbox + 指数バックオフ再試行方式を採用しています。
/// 410 Gone / 404 NotFound を返した購読は失効しているためその場で削除します。
/// </summary>
public class PushOutboxPoller : BasePollingBackgroundService
{
    private readonly WebPushClient _webPushClient = new();

    public PushOutboxPoller(IServiceProvider serviceProvider, ILogger<PushOutboxPoller> logger)
        : base(serviceProvider, logger, TimeSpan.FromSeconds(5))
    {
    }

    protected override async Task PollAsync(IServiceProvider serviceProvider, CancellationToken stoppingToken)
    {
        var db = serviceProvider.GetRequiredService<IDbConnection>();
        var subscriptionStore = serviceProvider.GetRequiredService<IPushSubscriptionStore>();
        var vapidKeyProvider = serviceProvider.GetRequiredService<IVapidKeyProvider>();
        var webPushOptions = serviceProvider.GetRequiredService<IOptions<WebPushOptions>>().Value;

        await db.ExecuteAsync(@"
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

        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var selectSql = @"
            SELECT * FROM ""PushOutbox""
            WHERE ""State"" = 0 OR (""State"" = 2 AND ""Attempts"" < 5 AND ""NextAttemptAt"" <= @Now)";

        var pendingItems = (await db.QueryAsync<PushOutbox>(selectSql, new { Now = nowStr })).ToList();
        if (!pendingItems.Any()) return;

        Logger.LogInformation("Found {Count} pending push notifications to process.", pendingItems.Count);

        var vapid = vapidKeyProvider.GetKeys();

        foreach (var item in pendingItems)
        {
            if (stoppingToken.IsCancellationRequested) break;

            var subscriptions = await subscriptionStore.GetByUserAsync(item.TenantId, item.UserId);
            if (subscriptions.Count == 0)
            {
                // 送信先の購読が一件も無い場合は成功扱いで完了させる（再試行しても無意味）。
                await MarkSuccessAsync(db, item.Id);
                continue;
            }

            var payload = JsonSerializer.Serialize(new { title = item.Title, body = item.Body, url = item.Url });
            var vapidDetails = new VapidDetails(webPushOptions.Subject, vapid.PublicKey, vapid.PrivateKey);

            var anyFailure = false;
            string? lastError = null;

            foreach (var sub in subscriptions)
            {
                try
                {
                    var pushSubscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                    await _webPushClient.SendNotificationAsync(pushSubscription, payload, vapidDetails, stoppingToken);
                }
                catch (WebPushException wex)
                {
                    if (wex.StatusCode == HttpStatusCode.Gone || wex.StatusCode == HttpStatusCode.NotFound)
                    {
                        Logger.LogInformation("Push subscription {Id} is no longer valid ({Status}); removing.", sub.Id, wex.StatusCode);
                        await subscriptionStore.RemoveByIdAsync(sub.Id);
                        continue;
                    }

                    anyFailure = true;
                    lastError = wex.Message;
                    Logger.LogWarning(wex, "Failed to send push notification {Id} to subscription {SubId}", item.Id, sub.Id);
                }
                catch (Exception ex)
                {
                    anyFailure = true;
                    lastError = ex.Message;
                    Logger.LogWarning(ex, "Failed to send push notification {Id} to subscription {SubId}", item.Id, sub.Id);
                }
            }

            if (!anyFailure)
            {
                await MarkSuccessAsync(db, item.Id);
                Logger.LogInformation("Push notification {Id} delivered successfully.", item.Id);
            }
            else
            {
                await MarkFailureAsync(db, item, lastError);
            }
        }
    }

    private static async Task MarkSuccessAsync(IDbConnection db, string id)
    {
        await db.ExecuteAsync(
            @"UPDATE ""PushOutbox"" SET ""State"" = 1, ""Attempts"" = ""Attempts"" + 1, ""LastError"" = NULL WHERE ""Id"" = @Id",
            new { Id = id });
    }

    private async Task MarkFailureAsync(IDbConnection db, PushOutbox item, string? lastError)
    {
        var attempts = item.Attempts + 1;
        var nextAttemptInSeconds = 5 * (int)Math.Pow(2, attempts); // 10s, 20s, 40s, 80s, 160s...
        var nextAttemptStr = DateTime.UtcNow.AddSeconds(nextAttemptInSeconds).ToString("yyyy-MM-dd HH:mm:ss");
        var newState = attempts >= 5 ? 3 : 2; // 3 = dead letter

        await db.ExecuteAsync(
            @"UPDATE ""PushOutbox""
              SET ""State"" = @State, ""Attempts"" = @Attempts, ""NextAttemptAt"" = @NextAttemptAt, ""LastError"" = @LastError
              WHERE ""Id"" = @Id",
            new { State = newState, Attempts = attempts, NextAttemptAt = nextAttemptStr, LastError = lastError, Id = item.Id });

        Logger.LogWarning("Push notification {Id} failed (Attempt {Attempts}). Next retry: {NextAttemptAt}", item.Id, attempts, nextAttemptStr);
    }
}
