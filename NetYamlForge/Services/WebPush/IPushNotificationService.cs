using System.Threading.Tasks;

namespace NetYamlForge.Services.WebPush;

/// <summary>
/// アプリ内の任意の場所（Hook / BatchJob / Controller）から
/// ブラウザへのプッシュ通知をキューイングするためのエントリポイント。
/// 実送信は PushOutboxPoller が非同期に行います（Webhook と同じ Outbox パターン）。
/// </summary>
public interface IPushNotificationService
{
    /// <summary>指定ユーザーの全購読先（複数デバイス/ブラウザ）宛にプッシュ通知をキューイングします。</summary>
    Task EnqueueAsync(string tenantId, string userId, string title, string body, string? url = null);
}
