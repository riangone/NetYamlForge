#pragma warning disable DCS001

using System;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Dapper;

namespace NetYamlForge.Services.Webhook;

public class WebhookOutboxPoller : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookOutboxPoller> _logger;
    private readonly HttpClient _httpClient;

    public WebhookOutboxPoller(IServiceProvider serviceProvider, ILogger<WebhookOutboxPoller> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebhookOutboxPoller Background Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingWebhooksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pending webhooks in background poller.");
            }

            await Task.Delay(5000, stoppingToken); // Poll every 5 seconds
        }

        _logger.LogInformation("WebhookOutboxPoller Background Service is stopping.");
    }

    private async Task ProcessPendingWebhooksAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

        // Ensure table exists
        await db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ""WebhookOutbox"" (
                ""Id"" TEXT PRIMARY KEY,
                ""TenantId"" TEXT NOT NULL,
                ""EventName"" TEXT NOT NULL,
                ""Payload"" TEXT NOT NULL,
                ""TargetUrl"" TEXT NOT NULL,
                ""Secret"" TEXT,
                ""State"" INTEGER NOT NULL,
                ""Attempts"" INTEGER NOT NULL,
                ""NextAttemptAt"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""LastError"" TEXT
            );");

        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var selectSql = @"
            SELECT * FROM ""WebhookOutbox""
            WHERE ""State"" = 0 OR (""State"" = 2 AND ""Attempts"" < 5 AND ""NextAttemptAt"" <= @Now)";

        var pendingItems = (await db.QueryAsync<WebhookOutbox>(selectSql, new { Now = nowStr })).ToList();

        if (!pendingItems.Any()) return;

        _logger.LogInformation("Found {Count} pending webhooks to process.", pendingItems.Count);

        foreach (var item in pendingItems)
        {
            if (stoppingToken.IsCancellationRequested) break;

            bool success = false;
            string? lastError = null;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, item.TargetUrl);
                request.Content = new StringContent(item.Payload, Encoding.UTF8, "application/json");

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                request.Headers.Add("X-NetYamlForge-Timestamp", timestamp);

                if (!string.IsNullOrEmpty(item.Secret))
                {
                    var signature = ComputeSignature(item.Payload, item.Secret, timestamp);
                    request.Headers.Add("X-NetYamlForge-Signature", $"t={timestamp},v1={signature}");
                }

                var response = await _httpClient.SendAsync(request, stoppingToken);
                if (response.IsSuccessStatusCode)
                {
                    success = true;
                }
                else
                {
                    lastError = $"HTTP {(int)response.StatusCode} - {response.ReasonPhrase}";
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                _logger.LogWarning(ex, "Failed to send webhook {Id} to {Url}", item.Id, item.TargetUrl);
            }

            if (success)
            {
                var updateSql = @"
                    UPDATE ""WebhookOutbox""
                    SET ""State"" = 1, ""Attempts"" = ""Attempts"" + 1, ""LastError"" = NULL
                    WHERE ""Id"" = @Id";
                await db.ExecuteAsync(updateSql, new { Id = item.Id });
                _logger.LogInformation("Webhook {Id} sent successfully.", item.Id);
            }
            else
            {
                var attempts = item.Attempts + 1;
                var nextAttemptInSeconds = 5 * (int)Math.Pow(2, attempts); // 10s, 20s, 40s, 80s, 160s...
                var nextAttemptStr = DateTime.UtcNow.AddSeconds(nextAttemptInSeconds).ToString("yyyy-MM-dd HH:mm:ss");
                var newState = attempts >= 5 ? 3 : 2; // 3 means permanent failure (dead letter)

                var updateSql = @"
                    UPDATE ""WebhookOutbox""
                    SET ""State"" = @State, ""Attempts"" = @Attempts, ""NextAttemptAt"" = @NextAttemptAt, ""LastError"" = @LastError
                    WHERE ""Id"" = @Id";
                
                await db.ExecuteAsync(updateSql, new
                {
                    State = newState,
                    Attempts = attempts,
                    NextAttemptAt = nextAttemptStr,
                    LastError = lastError,
                    Id = item.Id
                });
                _logger.LogWarning("Webhook {Id} failed (Attempt {Attempts}). Next retry: {NextAttemptAt}", item.Id, attempts, nextAttemptStr);
            }
        }
    }

    private string ComputeSignature(string payload, string secret, string timestamp)
    {
        var valueToSign = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(valueToSign));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
