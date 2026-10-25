namespace NetYamlForge.Models.Config;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// クライアント実 IP を解決するために信頼するヘッダー名。
    /// 空の場合はヘッダーを信頼せず、常に RemoteIpAddress を使用します。既定は X-Forwarded-For。
    /// </summary>
    public string ClientIpHeader { get; set; } = "X-Forwarded-For";

    /// <summary>
    /// ClientIpHeader を受け入れるリバースプロキシの CIDR 網の一覧（例: "10.0.0.0/8"）。
    /// 空の場合はヘッダーを無条件に信頼します。
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>
    /// RateLimiter レジストリのアイドル掃引周期（秒）。既定 600。
    /// </summary>
    public int CacheSweepSeconds { get; set; } = 600;

    /// <summary>
    /// 単一の限流キーが Mark されてから削除されるまでの最大存続時間（秒）。既定 3600。
    /// </summary>
    public int LimiterIdleTtlSeconds { get; set; } = 3600;

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

    /// <summary>
    /// 超過時にキューへ入れて待たせる数。DoS 対策として 0（即時 429 拒否）を推奨。
    /// </summary>
    public int QueueLimit { get; set; } = 0;

    /// <summary>
    /// 分区キーを決める要素のカンマ区切りリスト。要素は "IP", "User" のいずれか。
    /// 例: "IP" = IP のみ、"IP,User" = IP と User の複合分区。空文字は "IP" 扱い。
    /// </summary>
    public string LimitBy { get; set; } = "IP";
}

public class BruteForceProtection
{
    public bool Enabled { get; set; } = true;
    public int FailedAttemptThreshold { get; set; } = 5;
    public int LockoutSeconds { get; set; } = 300;

    /// <summary>
    /// ログインフォームからユーザー名を読む際に使うフォームキー名。既定 "UserName"。
    /// </summary>
    public string UsernameFormKey { get; set; } = "UserName";
}
