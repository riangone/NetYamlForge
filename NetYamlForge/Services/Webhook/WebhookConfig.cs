using System.Collections.Generic;

namespace NetYamlForge.Services.Webhook;

public class WebhookSubscriptionList
{
    public List<WebhookSubscription> Webhooks { get; set; } = new();
}

public class WebhookSubscription
{
    public string Name { get; set; } = default!;
    public bool Enabled { get; set; } = true;
    public List<string> Events { get; set; } = new();
    public string TargetUrl { get; set; } = default!;
    public string? Secret { get; set; }
    public int MaxRetryAttempts { get; set; } = 5;
    public int InitialDelaySeconds { get; set; } = 5;
}
