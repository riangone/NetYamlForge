using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetYamlForge.Controllers;
using NetYamlForge.Localization;
using NetYamlForge.Services.Tenant;
using Xunit;

namespace NetYamlForge.Tests.Controllers;

public class LocalizationControllerTests
{
    [Fact]
    public async Task SetLanguage_ShouldSetCookieAndRedirectToLocalUrl()
    {
        // Arrange
        var mockService = new Mock<ITenantUserService>();
        var httpContext = new DefaultHttpContext();
        
        var controller = new LocalizationController(mockService.Object, NullLogger<LocalizationController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        // Act
        var result = await controller.SetLanguage("zh-CN", "/test-url");

        // Assert
        Assert.NotNull(result);
        var redirectResult = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/test-url", redirectResult.Url);

        // Check if cookie is appended
        var hasCookie = httpContext.Response.Headers.TryGetValue("Set-Cookie", out var cookieValues);
        Assert.True(hasCookie);
        Assert.Contains(".AspNetCore.Culture", cookieValues.ToString());
    }

    [Fact]
    public async Task SetLanguage_ShouldPersistLanguage_WhenUserIsAuthenticated()
    {
        // Arrange
        var mockService = new Mock<ITenantUserService>();
        mockService.Setup(x => x.UpdatePreferredLanguageAsync(123, "zh-CN"))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var httpContext = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "123")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        var controller = new LocalizationController(mockService.Object, NullLogger<LocalizationController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        // Act
        var result = await controller.SetLanguage("zh-CN", "/test-url");

        // Assert
        mockService.Verify(x => x.UpdatePreferredLanguageAsync(123, "zh-CN"), Times.Once);
    }

    [Fact]
    public async Task SetLanguage_ShouldFallbackToReferer_WhenReturnUrlIsNull()
    {
        // Arrange
        var mockService = new Mock<ITenantUserService>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("localhost", 5001);
        httpContext.Request.Headers["Referer"] = "http://localhost:5001/previous-page";

        var controller = new LocalizationController(mockService.Object, NullLogger<LocalizationController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        // Act
        var result = await controller.SetLanguage("zh-CN", null);

        // Assert
        var redirectResult = Assert.IsType<RedirectResult>(result);
        Assert.Equal("http://localhost:5001/previous-page", redirectResult.Url);
    }

    [Fact]
    public async Task SetLanguage_ShouldFallbackToDefaultAction_WhenUrlIsExternal()
    {
        // Arrange
        var mockService = new Mock<ITenantUserService>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("localhost", 5001);

        var controller = new LocalizationController(mockService.Object, NullLogger<LocalizationController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        // Act
        var result = await controller.SetLanguage("zh-CN", "http://malicious-site.com/steal");

        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirectResult.ActionName);
        Assert.Equal("DynamicEntity", redirectResult.ControllerName);
    }

    [Fact]
    public async Task Provider_ShouldReturnCultureFromClaim_WhenUserIsAuthenticated()
    {
        // Arrange
        var provider = new UserPreferredLanguageProvider();
        var httpContext = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("lang", "ko-KR")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        // Act
        var result = await provider.DetermineProviderCultureResult(httpContext);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ko-KR", result.Cultures.First().Value);
    }

    [Fact]
    public async Task Provider_ShouldReturnNull_WhenUserIsNotAuthenticated()
    {
        // Arrange
        var provider = new UserPreferredLanguageProvider();
        var httpContext = new DefaultHttpContext();

        // Act
        var result = await provider.DetermineProviderCultureResult(httpContext);

        // Assert
        Assert.Null(result);
    }
}
