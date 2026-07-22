using System;

namespace NetYamlForge.Services.WebPush;

/// <summary>
/// ブラウザの PushManager.subscribe() が返す購読情報をDBに永続化するためのレコード。
/// </summary>
public class PushSubscriptionRecord
{
    public string Id { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public string Endpoint { get; set; } = default!;
    public string P256dh { get; set; } = default!;
    public string Auth { get; set; } = default!;
    public string? UserAgent { get; set; }
    public string CreatedAt { get; set; } = default!;
}

/// <summary>
/// プッシュ送信のリトライ管理用アウトボックス（WebhookOutbox と同じ設計）。
/// </summary>
public class PushOutbox
{
    public string Id { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string? Url { get; set; }
    public int State { get; set; } // 0: Pending, 1: Success, 2: Retry待ち, 3: DeadLetter
    public int Attempts { get; set; }
    public string NextAttemptAt { get; set; } = default!;
    public string CreatedAt { get; set; } = default!;
    public string? LastError { get; set; }
}

/// <summary>appsettings.json の "WebPush" セクションにバインドされる設定。</summary>
public class WebPushOptions
{
    /// <summary>VAPID Subject。"mailto:you@example.com" 形式を推奨。</summary>
    public string Subject { get; set; } = "mailto:admin@netyamlforge.local";
}

/// <summary>起動時に自動生成・永続化される VAPID 鍵ペア。</summary>
public class VapidKeyPair
{
    public string PublicKey { get; set; } = default!;
    public string PrivateKey { get; set; } = default!;
}
