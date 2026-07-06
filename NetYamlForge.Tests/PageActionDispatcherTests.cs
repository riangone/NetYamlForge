using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.Page;
using Xunit;

namespace NetYamlForge.Tests;

public class PageActionDispatcherTests
{
    private class DummyHandler : IPageActionHandler
    {
        public string ActionName { get; init; }
        public string? Project { get; init; }
        public string ResultValue { get; init; }

        public Task<IActionResult> HandleAsync(PageActionContext ctx)
        {
            return Task.FromResult<IActionResult>(new OkObjectResult(ResultValue));
        }
    }

    [Fact]
    public async Task DispatchAsync_ShouldPrioritizeProjectHandlerOverGlobal()
    {
        // Arrange
        var globalHandler = new DummyHandler { ActionName = "test-action", Project = null, ResultValue = "global" };
        var projectHandler = new DummyHandler { ActionName = "test-action", Project = "my-project", ResultValue = "project" };

        var dispatcher = new PageActionDispatcher(new[] { globalHandler });
        dispatcher.Register("my-project", projectHandler);

        var httpContext = new DefaultHttpContext();
        var ctx = new PageActionContext("my-project", "test-page", new Dictionary<string, string?>(), null!, new ClaimsPrincipal(), httpContext);

        // Act
        var result = await dispatcher.DispatchAsync("my-project", "test-page", "test-action", ctx);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("project", okResult.Value);
    }

    [Fact]
    public async Task DispatchAsync_ShouldFallbackToGlobalHandler()
    {
        // Arrange
        var globalHandler = new DummyHandler { ActionName = "test-action", Project = null, ResultValue = "global" };
        var dispatcher = new PageActionDispatcher(new[] { globalHandler });

        var httpContext = new DefaultHttpContext();
        var ctx = new PageActionContext("another-project", "test-page", new Dictionary<string, string?>(), null!, new ClaimsPrincipal(), httpContext);

        // Act
        var result = await dispatcher.DispatchAsync("another-project", "test-page", "test-action", ctx);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("global", okResult.Value);
    }

    [Fact]
    public async Task DispatchAsync_ShouldReturnNull_WhenNoHandlerFound()
    {
        // Arrange
        var dispatcher = new PageActionDispatcher(Array.Empty<IPageActionHandler>());

        var httpContext = new DefaultHttpContext();
        var ctx = new PageActionContext("my-project", "test-page", new Dictionary<string, string?>(), null!, new ClaimsPrincipal(), httpContext);

        // Act
        var result = await dispatcher.DispatchAsync("my-project", "test-page", "unknown-action", ctx);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Clear_ShouldRemoveProjectHandlers()
    {
        // Arrange
        var projectHandler = new DummyHandler { ActionName = "test-action", Project = "my-project", ResultValue = "project" };
        var dispatcher = new PageActionDispatcher(Array.Empty<IPageActionHandler>());
        dispatcher.Register("my-project", projectHandler);

        var httpContext = new DefaultHttpContext();
        var ctx = new PageActionContext("my-project", "test-page", new Dictionary<string, string?>(), null!, new ClaimsPrincipal(), httpContext);

        // Act
        dispatcher.Clear("my-project");
        var result = await dispatcher.DispatchAsync("my-project", "test-page", "test-action", ctx);

        // Assert
        Assert.Null(result);
    }
}
