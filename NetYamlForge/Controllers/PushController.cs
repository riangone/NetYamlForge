// ファイル概要: WebPush 購読の登録/解除および VAPID 公開鍵配布用エンドポイントを提供します。
// フロントエンドは GET /Push/VapidPublicKey で鍵を取得し、
// PushManager.subscribe() の結果を POST /Push/Subscribe に送って永続化します。

using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.Tenant;
using NetYamlForge.Services.WebPush;

namespace NetYamlForge.Controllers;

[Authorize]
[Route("Push/{action}")]
[Route("{project}/Push/{action}")]
public class PushController : Controller
{
    private readonly IVapidKeyProvider _vapidKeyProvider;
    private readonly IPushSubscriptionStore _subscriptionStore;
    private readonly TenantContext _tenantContext;

    public PushController(IVapidKeyProvider vapidKeyProvider, IPushSubscriptionStore subscriptionStore, TenantContext tenantContext)
    {
        _vapidKeyProvider = vapidKeyProvider;
        _subscriptionStore = subscriptionStore;
        _tenantContext = tenantContext;
    }

    /// <summary>フロントエンドが PushManager.subscribe() に渡す VAPID 公開鍵（Base64URL）を返します。</summary>
    [HttpGet]
    public IActionResult VapidPublicKey()
    {
        return Ok(new { publicKey = _vapidKeyProvider.GetKeys().PublicKey });
    }

    public class SubscribeRequest
    {
        public string Endpoint { get; set; } = default!;
        public SubscribeKeys Keys { get; set; } = default!;

        public class SubscribeKeys
        {
            public string P256dh { get; set; } = default!;
            public string Auth { get; set; } = default!;
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Endpoint) || request.Keys == null
            || string.IsNullOrWhiteSpace(request.Keys.P256dh) || string.IsNullOrWhiteSpace(request.Keys.Auth))
        {
            return BadRequest(new { error = "endpoint / keys.p256dh / keys.auth is required" });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var tenantId = _tenantContext.TenantId ?? "default";
        var userAgent = Request.Headers.UserAgent.ToString();

        var record = await _subscriptionStore.SubscribeAsync(tenantId, userId, request.Endpoint, request.Keys.P256dh, request.Keys.Auth, userAgent);
        return Ok(new { id = record.Id });
    }

    public class UnsubscribeRequest
    {
        public string Endpoint { get; set; } = default!;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return BadRequest(new { error = "endpoint is required" });
        }

        var tenantId = _tenantContext.TenantId ?? "default";
        await _subscriptionStore.UnsubscribeAsync(tenantId, request.Endpoint);
        return Ok();
    }
}
