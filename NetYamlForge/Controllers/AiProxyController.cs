// AI サービスプロキシコントローラー
// NetYamlForge から NetYamlForge.AI.Web へ HTTP 経由で転送する。
// NetYamlForge.AI への直接プロジェクト参照は不要（完全疎結合）。

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

/// <summary>
/// AI サービスへのリバースプロキシ
/// UI は NetYamlForge 側に置き、API 呼び出しはすべてここ経由で AI.Web に転送する。
/// </summary>
[Route("{project}/ai")]
[Route("ai")]
public class AiProxyController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiProxyController> _logger;

    private string AiServiceBaseUrl =>
        _configuration["AI:ServiceBaseUrl"] ?? "http://localhost:5200";

    public AiProxyController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AiProxyController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    // ─────────────────────────────────────────────
    // UI エントリーポイント（Razor ビュー）
    // ─────────────────────────────────────────────

    /// <summary>
    /// AI チャットウィジェット（部分ビュー）
    /// HTMX で他のページに埋め込む場合は ?partial=true を付ける。
    /// </summary>
    [HttpGet("chat")]
    public IActionResult Chat([FromRoute] string? project, [FromQuery] bool partial = false)
    {
        ViewBag.Project = project;
        ViewBag.AiServiceUrl = AiServiceBaseUrl;
        return partial ? PartialView("_AiChat") : View();
    }

    // ─────────────────────────────────────────────
    // API プロキシ（/ai/proxy/* → AI.Web の実 API へ転送）
    // ─────────────────────────────────────────────

    /// <summary>
    /// AI サービスの任意 API を転送する汎用プロキシ。
    /// 例: POST /auto-dealer-demo/ai/proxy/aiwindow/conversations
    ///   → POST http://localhost:5200/auto-dealer-demo/api/aiwindow/conversations
    /// </summary>
    [HttpPost("proxy/{**path}")]
    [HttpGet("proxy/{**path}")]
    [HttpPut("proxy/{**path}")]
    [HttpDelete("proxy/{**path}")]
    public async Task<IActionResult> Proxy(
        [FromRoute] string? project,
        [FromRoute] string path,
        CancellationToken cancellationToken)
    {
        var targetPath = string.IsNullOrEmpty(project)
            ? $"api/{path}"
            : $"{project}/api/{path}";

        var targetUrl = $"{AiServiceBaseUrl}/{targetPath}";

        try
        {
            var client = _httpClientFactory.CreateClient("AiService");

            using var upstreamRequest = new HttpRequestMessage(
                new HttpMethod(Request.Method),
                targetUrl);

            // リクエストボディを転送
            if (Request.ContentLength > 0 || Request.Headers.ContainsKey("Transfer-Encoding"))
            {
                upstreamRequest.Content = new StreamContent(Request.Body);
                if (Request.ContentType is not null)
                    upstreamRequest.Content.Headers.ContentType =
                        MediaTypeHeaderValue.Parse(Request.ContentType);
            }

            // 認証ヘッダーを転送（オプション）
            if (Request.Headers.TryGetValue("Authorization", out var auth))
                upstreamRequest.Headers.TryAddWithoutValidation("Authorization", (string?)auth);

            var upstream = await client.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            // リダイレクト（ログインページ等）を透過せず JSON エラーに変換
            if (upstream.StatusCode == System.Net.HttpStatusCode.Found ||
                upstream.StatusCode == System.Net.HttpStatusCode.MovedPermanently ||
                upstream.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                upstream.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var location = upstream.Headers.Location?.ToString();
                _logger.LogWarning("AI サービスが認証リダイレクト: {Status} → {Location}", upstream.StatusCode, location);
                return StatusCode(503, new
                {
                    error = "AI サービスへの認証が必要です",
                    detail = $"HTTP {(int)upstream.StatusCode}",
                    hint = "AI サービス (http://localhost:5005) に直接ログインしてください"
                });
            }

            Response.StatusCode = (int)upstream.StatusCode;

            var contentType = upstream.Content.Headers.ContentType?.MediaType;
            // HTML が返ってきた場合（エラーページ等）は JSON エラーに変換
            if (contentType != null && contentType.Contains("text/html"))
            {
                var html = await upstream.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("AI サービスが HTML を返却（エラーページの可能性）: {Url}", targetUrl);
                return StatusCode(upstream.IsSuccessStatusCode ? 200 : 502, new
                {
                    error = "AI サービスが予期しない応答を返しました",
                    detail = html.Length > 200 ? html[..200] + "..." : html
                });
            }

            if (contentType != null) Response.ContentType = contentType;
            await upstream.Content.CopyToAsync(Response.Body, cancellationToken);
            return new EmptyResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "AI サービスへの接続失敗: {Url}", targetUrl);
            return StatusCode(503, new { error = "AI サービスが利用できません", detail = ex.Message });
        }
    }

    /// <summary>
    /// AI サービスのヘルスチェック状態を返す
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("AiService");
            var response = await client.GetAsync($"{AiServiceBaseUrl}/health", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Content(body, "application/json");
        }
        catch
        {
            return StatusCode(503, new { status = "unavailable" });
        }
    }
}
