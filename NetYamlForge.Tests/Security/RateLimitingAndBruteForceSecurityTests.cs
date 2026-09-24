using System;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NetYamlForge.Controllers;
using NetYamlForge.Models.Auth;
using NetYamlForge.Models.Config;
using NetYamlForge.Services;
using NetYamlForge.Services.Api;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Tenant;
using Xunit;

namespace NetYamlForge.Tests.Security;

[Trait("Category", "Security")]
public class RateLimitingAndBruteForceSecurityTests
{
    private static IOptions<RateLimitingOptions> CreateOptions(
        int threshold = 3,
        int lockoutSeconds = 60,
        int authPermitLimit = 2)
    {
        return Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions
        {
            Enabled = true,
            Auth = new RateLimitRule
            {
                Enabled = true,
                PermitLimit = authPermitLimit,
                WindowSeconds = 10,
                QueueLimit = 0,
                LimitBy = "IP"
            },
            Api = new RateLimitRule
            {
                Enabled = true,
                PermitLimit = 100,
                WindowSeconds = 60,
                QueueLimit = 0,
                LimitBy = "IP"
            },
            BruteForce = new BruteForceProtection
            {
                Enabled = true,
                FailedAttemptThreshold = threshold,
                LockoutSeconds = lockoutSeconds
            }
        });
    }

    [Fact]
    public void FailedLoginTracker_RecordsFailuresAndLocksOut_AfterThresholdReached()
    {
        var options = CreateOptions(threshold: 3, lockoutSeconds: 100);
        var tracker = new FailedLoginTracker(options, NullLogger<FailedLoginTracker>.Instance);
        const string key = "192.168.1.100";

        Assert.False(tracker.IsLockedOut(key));

        tracker.RecordFailedAttempt(key);
        Assert.False(tracker.IsLockedOut(key));

        tracker.RecordFailedAttempt(key);
        Assert.False(tracker.IsLockedOut(key));

        tracker.RecordFailedAttempt(key);
        // Reached threshold of 3
        Assert.True(tracker.IsLockedOut(key));

        // Reset clears lockout
        tracker.Reset(key);
        Assert.False(tracker.IsLockedOut(key));
    }

    [Fact]
    public async Task GlobalRateLimitingMiddleware_BlocksWhenClientIpIsLockedOut()
    {
        var options = CreateOptions(threshold: 2, lockoutSeconds: 60);
        var tracker = new FailedLoginTracker(options, NullLogger<FailedLoginTracker>.Instance);
        const string clientIp = "10.0.0.50";

        // Pre-lockout the IP in tracker
        tracker.RecordFailedAttempt(clientIp);
        tracker.RecordFailedAttempt(clientIp);
        Assert.True(tracker.IsLockedOut(clientIp));

        bool nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new GlobalRateLimitingMiddleware(
            next,
            NullLogger<GlobalRateLimitingMiddleware>.Instance,
            options,
            tracker);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(clientIp);
        httpContext.Request.Path = "/Account/Login";
        httpContext.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(httpContext);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("60", httpContext.Response.Headers["Retry-After"]);
    }

    [Fact]
    public async Task GlobalRateLimitingMiddleware_BlocksWhenRateLimitExceeded()
    {
        var options = CreateOptions(threshold: 10, lockoutSeconds: 60, authPermitLimit: 2);
        var tracker = new FailedLoginTracker(options, NullLogger<FailedLoginTracker>.Instance);
        const string clientIp = "10.0.0.99";

        int callCount = 0;
        RequestDelegate next = (ctx) =>
        {
            callCount++;
            return Task.CompletedTask;
        };

        var middleware = new GlobalRateLimitingMiddleware(
            next,
            NullLogger<GlobalRateLimitingMiddleware>.Instance,
            options,
            tracker);

        // First 2 requests should succeed
        for (int i = 0; i < 2; i++)
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(clientIp);
            ctx.Request.Path = "/Account/Login";
            ctx.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(ctx);
            Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        }

        Assert.Equal(2, callCount);

        // 3rd request exceeds limit
        var rejectedCtx = new DefaultHttpContext();
        rejectedCtx.Connection.RemoteIpAddress = IPAddress.Parse(clientIp);
        rejectedCtx.Request.Path = "/Account/Login";
        rejectedCtx.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(rejectedCtx);

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejectedCtx.Response.StatusCode);
        Assert.Equal(2, callCount); // next was NOT called
    }

    [Fact]
    public async Task AccountController_Login_TriggersLockoutAfterRepeatedFailures()
    {
        var options = CreateOptions(threshold: 2, lockoutSeconds: 60);
        var tracker = new FailedLoginTracker(options, NullLogger<FailedLoginTracker>.Instance);

        var usersMock = new Mock<IUserAuthService>();
        var loggerMock = new Mock<ILogger<AccountController>>();
        var auditMock = new Mock<IAuditLogService>();
        var projectScope = new ProjectScope();
        var tenantUsersMock = new Mock<ITenantUserService>();
        var projectManagerMock = new Mock<ProjectManager>();

        // Invalid credentials returns null
        tenantUsersMock.Setup(u => u.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((AppUser?)null);

        var controller = new AccountController(
            usersMock.Object,
            loggerMock.Object,
            auditMock.Object,
            projectScope,
            tenantUsersMock.Object,
            projectManagerMock.Object,
            tracker);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var model = new LoginViewModel { UserName = "admin", Password = "wrong-password" };

        // Attempt 1 -> Fails
        await controller.Login(model);
        Assert.False(tracker.IsLockedOut("10.0.0.1"));

        // Clear ModelState before attempt 2 to simulate new HTTP request
        controller.ModelState.Clear();

        // Attempt 2 -> Fails, hits threshold 2
        await controller.Login(model);
        Assert.True(tracker.IsLockedOut("10.0.0.1"));

        // Attempt 3 -> Locked out before credential validation
        controller.ModelState.Clear();
        var result = await controller.Login(model) as ViewResult;
        Assert.NotNull(result);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
        var error = controller.ModelState[string.Empty]!.Errors[0].ErrorMessage;
        Assert.Contains("锁定", error);
    }

    [Fact]
    public async Task TenantAccountController_Login_TriggersLockoutAfterRepeatedFailures()
    {
        var options = CreateOptions(threshold: 2, lockoutSeconds: 60);
        var tracker = new FailedLoginTracker(options, NullLogger<FailedLoginTracker>.Instance);

        var tenantUsersMock = new Mock<ITenantUserService>();
        var loggerMock = new Mock<ILogger<TenantAccountController>>();

        tenantUsersMock.Setup(u => u.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((AppUser?)null);

        var controller = new TenantAccountController(
            tenantUsersMock.Object,
            loggerMock.Object,
            tracker);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var model = new LoginViewModel { UserName = "tenant-user", Password = "wrong-password" };

        // Attempt 1 -> Fails
        await controller.Login(model);
        Assert.False(tracker.IsLockedOut("10.0.0.2"));

        // Clear ModelState before attempt 2
        controller.ModelState.Clear();

        // Attempt 2 -> Fails, hits threshold 2
        await controller.Login(model);
        Assert.True(tracker.IsLockedOut("10.0.0.2"));

        // Attempt 3 -> Locked out before credential validation
        controller.ModelState.Clear();
        var result = await controller.Login(model) as ViewResult;
        Assert.NotNull(result);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
        var error = controller.ModelState[string.Empty]!.Errors[0].ErrorMessage;
        Assert.Contains("锁定", error);
    }
}
