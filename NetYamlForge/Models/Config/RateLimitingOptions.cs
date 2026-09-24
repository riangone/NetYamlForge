namespace NetYamlForge.Models.Config;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;
    public RateLimitRule Api { get; set; } = new();
    public RateLimitRule Auth { get; set; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 60,
        QueueLimit = 0
    };
    public BruteForceProtection BruteForce { get; set; } = new();
}

public class RateLimitRule
{
    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 10;
    public string LimitBy { get; set; } = "IP";
}

public class BruteForceProtection
{
    public bool Enabled { get; set; } = true;
    public int FailedAttemptThreshold { get; set; } = 5;
    public int LockoutSeconds { get; set; } = 300;
}
