using System.Collections.Generic;
using System.Threading.Tasks;

namespace NetYamlForge.Services.WebPush;

public interface IPushSubscriptionStore
{
    /// <summary>テーブルが存在しなければ作成します（Webhook系サービスと同じ自己マイグレーション方式）。</summary>
    Task EnsureSchemaAsync();

    /// <summary>購読情報を登録します（同一 Endpoint が既にあれば上書き = upsert）。</summary>
    Task<PushSubscriptionRecord> SubscribeAsync(string tenantId, string userId, string endpoint, string p256dh, string auth, string? userAgent);

    /// <summary>Endpoint 単位で購読を解除します。</summary>
    Task UnsubscribeAsync(string tenantId, string endpoint);

    /// <summary>410 Gone 等でブラウザ側が破棄した購読を削除します。</summary>
    Task RemoveByIdAsync(string id);

    /// <summary>指定テナント・ユーザーの全購読先を取得します。</summary>
    Task<IReadOnlyList<PushSubscriptionRecord>> GetByUserAsync(string tenantId, string userId);
}
