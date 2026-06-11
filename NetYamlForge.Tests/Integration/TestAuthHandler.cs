// ファイル概要: 統合テスト用の認証ハンドラーです。
// すべてのリクエストを Admin ロール付きのテストユーザーとして認証します。
// NameIdentifier を非数値にすることで ProjectScopeMiddleware のテナント
// アクセスチェック（数値ユーザー ID 前提）を安全にスキップします。

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Tests.Integration;

/// <summary>
/// テスト専用の認証スキーム。常に Admin ロールのテストユーザーとして認証します。
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";
    public const string UserName = "e2e-admin";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, UserName),
            // 非数値の NameIdentifier → ProjectScopeMiddleware の
            // テナントアクセス検証（int パース前提）をスキップさせる
            new Claim(ClaimTypes.NameIdentifier, UserName),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
