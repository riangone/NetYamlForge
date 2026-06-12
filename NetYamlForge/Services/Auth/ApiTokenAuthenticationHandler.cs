using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.Auth;

public class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IUserAuthService _userAuthService;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IUserAuthService userAuthService)
        : base(options, logger, encoder)
    {
        _userAuthService = userAuthService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;

        // 1. From Authorization Header (Bearer token)
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader.Substring("Bearer ".Length).Trim();
        }

        // 2. From Custom Headers
        if (string.IsNullOrEmpty(token))
        {
            token = Request.Headers["X-Api-Token"].FirstOrDefault();
        }
        if (string.IsNullOrEmpty(token))
        {
            token = Request.Headers["X-Api-Key"].FirstOrDefault();
        }

        // 3. From Query Parameters
        if (string.IsNullOrEmpty(token))
        {
            token = Request.Query["api_token"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var user = await _userAuthService.GetByApiTokenAsync(token);
        if (user == null)
        {
            return AuthenticateResult.Fail("Invalid API Token");
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim("PreferredLanguage", user.PreferredLanguage)
        };

        if (user.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var roles = await _userAuthService.GetUserRolesAsync(user.UserName);
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
