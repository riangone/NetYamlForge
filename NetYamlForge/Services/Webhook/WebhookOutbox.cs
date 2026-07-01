using System;

namespace NetYamlForge.Services.Webhook;

public class WebhookOutbox
{
    public string Id { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string EventName { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public string TargetUrl { get; set; } = default!;
    public string? Secret { get; set; }
    public int State { get; set; } // 0: Pending, 1: Success, 2: Failed
    public int Attempts { get; set; }
    public string NextAttemptAt { get; set; } = default!;
    public string CreatedAt { get; set; } = default!;
    public string? LastError { get; set; }
}
