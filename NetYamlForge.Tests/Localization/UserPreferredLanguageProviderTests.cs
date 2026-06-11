using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetYamlForge.Localization;
using Xunit;

namespace NetYamlForge.Tests.Localization;

/// <summary>
/// fe1c1d8 の回帰テスト：言語切替の優先順位 cookie > claim > Accept-Language を固定化します。
/// cookie を claim より優先することで、ログインユーザーが言語を切り替えた際に即時反映されます。
/// </summary>
public class UserPreferredLanguageProviderTests
{
    private static readonly string CookieName = CookieRequestCultureProvider.DefaultCookieName;

    private static DefaultHttpContext CreateHttpContext(
        string? cookieCulture = null,
        string? claimCulture = null,
        string? acceptLanguage = null,
        bool authenticated = true)
    {
        var httpContext = new DefaultHttpContext();

        if (cookieCulture != null)
        {
            var cookieValue = CookieRequestCultureProvider.MakeCookieValue(
                new RequestCulture(cookieCulture));
            httpContext.Request.Headers["Cookie"] = $"{CookieName}={Uri.EscapeDataString(cookieValue)}";
        }

        if (claimCulture != null)
        {
            var identity = authenticated
                ? new ClaimsIdentity(new[] { new Claim("lang", claimCulture) }, "TestAuth")
                : new ClaimsIdentity(new[] { new Claim("lang", claimCulture) });
            httpContext.User = new ClaimsPrincipal(identity);
        }

        if (acceptLanguage != null)
        {
            httpContext.Request.Headers["Accept-Language"] = acceptLanguage;
        }

        return httpContext;
    }

    // ─── Provider 单元级：cookie > claim ───

    [Fact]
    public async Task DetermineProviderCultureResult_ShouldPreferCookie_WhenBothCookieAndClaimPresent()
    {
        var provider = new UserPreferredLanguageProvider();
        var httpContext = CreateHttpContext(cookieCulture: "ja-JP", claimCulture: "ko-KR");

        var result = await provider.DetermineProviderCultureResult(httpContext);

        Assert.NotNull(result);
        Assert.Equal("ja-JP", result.Cultures.First().Value);
    }

    [Fact]
    public async Task DetermineProviderCultureResult_ShouldReturnCookieCulture_WhenOnlyCookiePresent()
    {
        var provider = new UserPreferredLanguageProvider();
        var httpContext = CreateHttpContext(cookieCulture: "zh-CN");

        var result = await provider.DetermineProviderCultureResult(httpContext);

        Assert.NotNull(result);
        Assert.Equal("zh-CN", result.Cultures.First().Value);
    }

    [Fact]
    public async Task DetermineProviderCultureResult_ShouldFallBackToClaim_WhenCookieValueIsMalformed()
    {
        var provider = new UserPreferredLanguageProvider();
        var httpContext = CreateHttpContext(claimCulture: "ko-KR");
        httpContext.Request.Headers["Cookie"] = $"{CookieName}=not-a-valid-culture-cookie";

        var result = await provider.DetermineProviderCultureResult(httpContext);

        Assert.NotNull(result);
        Assert.Equal("ko-KR", result.Cultures.First().Value);
    }

    [Fact]
    public async Task DetermineProviderCultureResult_ShouldReturnClaimCulture_WhenNoCookieAndAuthenticated()
    {
        var provider = new UserPreferredLanguageProvider();
        var httpContext = CreateHttpContext(claimCulture: "ja-JP");

        var result = await provider.DetermineProviderCultureResult(httpContext);

        Assert.NotNull(result);
        Assert.Equal("ja-JP", result.Cultures.First().Value);
    }

    [Fact]
    public async Task DetermineProviderCultureResult_ShouldIgnoreClaim_WhenIdentityIsNotAuthenticated()
    {
        var provider = new UserPreferredLanguageProvider();
        var httpContext = CreateHttpContext(claimCulture: "ja-JP", authenticated: false);

        var result = await provider.DetermineProviderCultureResult(httpContext);

        Assert.Null(result);
    }

    [Fact]
    public async Task DetermineProviderCultureResult_ShouldReturnNull_WhenNoCookieAndNoClaim()
    {
        // null を返すことで RequestLocalization は次の Provider（Accept-Language）へ委譲する
        var provider = new UserPreferredLanguageProvider();
        var httpContext = CreateHttpContext(acceptLanguage: "ko-KR");

        var result = await provider.DetermineProviderCultureResult(httpContext);

        Assert.Null(result);
    }

    // ─── Middleware 链级：与 Program.cs 同等的 Provider 链上验证 cookie > claim > Accept-Language ───

    private static RequestLocalizationOptions CreateOptionsLikeProgram()
    {
        var supportedCultures = new[] { "en-US", "zh-CN", "ja-JP", "ko-KR" }
            .Select(x => new CultureInfo(x))
            .ToList();

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture("en-US"),
            SupportedCultures = supportedCultures,
            SupportedUICultures = supportedCultures
        };
        // Program.cs と同じ位置（QueryString, Cookie の後・AcceptLanguage の前）に挿入
        options.RequestCultureProviders.Insert(2, new UserPreferredLanguageProvider());
        return options;
    }

    private static async Task<RequestCulture> ResolveCultureThroughMiddlewareAsync(HttpContext httpContext)
    {
        RequestCulture? resolved = null;
        var middleware = new RequestLocalizationMiddleware(
            ctx =>
            {
                resolved = ctx.Features.Get<IRequestCultureFeature>()?.RequestCulture;
                return Task.CompletedTask;
            },
            Options.Create(CreateOptionsLikeProgram()),
            NullLoggerFactory.Instance);

        await middleware.Invoke(httpContext);

        Assert.NotNull(resolved);
        return resolved;
    }

    [Fact]
    public async Task Pipeline_CookieShouldWin_OverClaimAndAcceptLanguage()
    {
        var httpContext = CreateHttpContext(
            cookieCulture: "zh-CN", claimCulture: "ja-JP", acceptLanguage: "ko-KR");

        var culture = await ResolveCultureThroughMiddlewareAsync(httpContext);

        Assert.Equal("zh-CN", culture.UICulture.Name);
    }

    [Fact]
    public async Task Pipeline_ClaimShouldWin_OverAcceptLanguage_WhenNoCookie()
    {
        var httpContext = CreateHttpContext(claimCulture: "ja-JP", acceptLanguage: "ko-KR");

        var culture = await ResolveCultureThroughMiddlewareAsync(httpContext);

        Assert.Equal("ja-JP", culture.UICulture.Name);
    }

    [Fact]
    public async Task Pipeline_AcceptLanguageShouldApply_WhenNoCookieAndNoClaim()
    {
        var httpContext = CreateHttpContext(acceptLanguage: "ko-KR");

        var culture = await ResolveCultureThroughMiddlewareAsync(httpContext);

        Assert.Equal("ko-KR", culture.UICulture.Name);
    }

    [Fact]
    public async Task Pipeline_ShouldFallBackToDefaultCulture_WhenNothingIsPresent()
    {
        var httpContext = CreateHttpContext();

        var culture = await ResolveCultureThroughMiddlewareAsync(httpContext);

        Assert.Equal("en-US", culture.UICulture.Name);
    }
}
