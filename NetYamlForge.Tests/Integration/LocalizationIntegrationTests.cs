// ファイル概要: LocalizationControllerと言語切り替えによるビューのローカライズを検証する統合テストです。
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Xunit;

namespace NetYamlForge.Tests.Integration;

public class LocalizationIntegrationTests : IClassFixture<NetYamlForgeWebApplicationFactory>
{
    private readonly NetYamlForgeWebApplicationFactory _factory;
    private const string IndexUrl = "/blog/DynamicEntity/Index?entity=post";

    private static readonly Regex AntiForgeryTokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    public LocalizationIntegrationTests(NetYamlForgeWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SetLanguage_ShouldSetCultureCookie_AndRenderLocalizedContent()
    {
        // 1. Arrange: HttpClientを作成（CookieContainerHandler付きでCookieを維持する）
        using var client = _factory.CreateDefaultClient(new CookieContainerHandler());

        // CSRF トークンを取得するためにインデックス画面へ GET リクエストを送信
        var getIndexResponse = await client.GetAsync(IndexUrl);
        getIndexResponse.EnsureSuccessStatusCode();
        var indexHtml = await getIndexResponse.Content.ReadAsStringAsync();

        var match = AntiForgeryTokenRegex.Match(indexHtml);
        Assert.True(match.Success, "一覧ページから antiforgery トークンを取得できませんでした。");
        var token = match.Groups[1].Value;

        // 2. Act: 言語を中文 (zh-CN) に切り替える
        var formZh = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["culture"] = "zh-CN",
            ["returnUrl"] = IndexUrl
        };

        var responseZh = await client.PostAsync(
            "/blog/Localization/SetLanguage",
            new FormUrlEncodedContent(formZh));

        // 3. Assert: 302 または 200 であることを確認し、適切にCookieと表示内容を検証する
        Assert.True(responseZh.StatusCode == HttpStatusCode.Redirect || responseZh.StatusCode == HttpStatusCode.OK);

        if (responseZh.StatusCode == HttpStatusCode.Redirect)
        {
            var hasSetCookieHeader = responseZh.Headers.TryGetValues("Set-Cookie", out var cookieValues);
            Assert.True(hasSetCookieHeader, "Set-Cookie ヘッダーがレスポンスに含まれていません。");
            var cookieStr = string.Join("; ", cookieValues ?? Array.Empty<string>());
            Assert.Contains(".AspNetCore.Culture", cookieStr);
            Assert.Contains("zh-CN", cookieStr);

            // 手動でリダイレクト先をリクエスト
            var getIndexResponseZh = await client.GetAsync(IndexUrl);
            getIndexResponseZh.EnsureSuccessStatusCode();
            var indexHtmlZhRaw = await getIndexResponseZh.Content.ReadAsStringAsync();
            var indexHtmlZh = System.Net.WebUtility.HtmlDecode(indexHtmlZhRaw);
            Assert.Contains("退出", indexHtmlZh);
            Assert.DoesNotContain("ログアウト", indexHtmlZh);
        }
        else
        {
            var indexHtmlZhRaw = await responseZh.Content.ReadAsStringAsync();
            var indexHtmlZh = System.Net.WebUtility.HtmlDecode(indexHtmlZhRaw);
            Assert.Contains("退出", indexHtmlZh);
            Assert.DoesNotContain("ログアウト", indexHtmlZh);
        }

        // 4. Act: 言語を日本語 (ja-JP) に切り替える
        var formJa = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["culture"] = "ja-JP",
            ["returnUrl"] = IndexUrl
        };

        var responseJa = await client.PostAsync(
            "/blog/Localization/SetLanguage",
            new FormUrlEncodedContent(formJa));

        Assert.True(responseJa.StatusCode == HttpStatusCode.Redirect || responseJa.StatusCode == HttpStatusCode.OK);

        if (responseJa.StatusCode == HttpStatusCode.Redirect)
        {
            var hasSetCookieHeader = responseJa.Headers.TryGetValues("Set-Cookie", out var cookieValues);
            Assert.True(hasSetCookieHeader, "Set-Cookie ヘッダーがレスポンスに含まれていません。");
            var cookieStr = string.Join("; ", cookieValues ?? Array.Empty<string>());
            Assert.Contains(".AspNetCore.Culture", cookieStr);
            Assert.Contains("ja-JP", cookieStr);

            var getIndexResponseJa = await client.GetAsync(IndexUrl);
            getIndexResponseJa.EnsureSuccessStatusCode();
            var indexHtmlJaRaw = await getIndexResponseJa.Content.ReadAsStringAsync();
            var indexHtmlJa = System.Net.WebUtility.HtmlDecode(indexHtmlJaRaw);
            Assert.Contains("ログアウト", indexHtmlJa);
            Assert.DoesNotContain("退出", indexHtmlJa);
        }
        else
        {
            var indexHtmlJaRaw = await responseJa.Content.ReadAsStringAsync();
            var indexHtmlJa = System.Net.WebUtility.HtmlDecode(indexHtmlJaRaw);
            Assert.Contains("ログアウト", indexHtmlJa);
            Assert.DoesNotContain("退出", indexHtmlJa);
        }

        // 5. Act: 言語を英語 (en-US) に切り替える
        var formEn = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["culture"] = "en-US",
            ["returnUrl"] = IndexUrl
        };

        var responseEn = await client.PostAsync(
            "/blog/Localization/SetLanguage",
            new FormUrlEncodedContent(formEn));

        Assert.True(responseEn.StatusCode == HttpStatusCode.Redirect || responseEn.StatusCode == HttpStatusCode.OK);

        if (responseEn.StatusCode == HttpStatusCode.Redirect)
        {
            var hasSetCookieHeader = responseEn.Headers.TryGetValues("Set-Cookie", out var cookieValues);
            Assert.True(hasSetCookieHeader, "Set-Cookie ヘッダーがレスポンスに含まれていません。");
            var cookieStr = string.Join("; ", cookieValues ?? Array.Empty<string>());
            Assert.Contains(".AspNetCore.Culture", cookieStr);
            Assert.Contains("en-US", cookieStr);

            var getIndexResponseEn = await client.GetAsync(IndexUrl);
            getIndexResponseEn.EnsureSuccessStatusCode();
            var indexHtmlEnRaw = await getIndexResponseEn.Content.ReadAsStringAsync();
            var indexHtmlEn = System.Net.WebUtility.HtmlDecode(indexHtmlEnRaw);
            Assert.Contains("Logout", indexHtmlEn);
            Assert.DoesNotContain("退出", indexHtmlEn);
            Assert.DoesNotContain("ログアウト", indexHtmlEn);
        }
        else
        {
            var indexHtmlEnRaw = await responseEn.Content.ReadAsStringAsync();
            var indexHtmlEn = System.Net.WebUtility.HtmlDecode(indexHtmlEnRaw);
            Assert.Contains("Logout", indexHtmlEn);
            Assert.DoesNotContain("退出", indexHtmlEn);
            Assert.DoesNotContain("ログアウト", indexHtmlEn);
        }
    }
}
