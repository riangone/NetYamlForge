using NetYamlForge.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

public class RequestTraceMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_UsesIncomingTraceHeader_WhenProvided()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestTraceMiddleware.TraceHeaderName] = "trace-from-client";
        var middleware = new RequestTraceMiddleware(_ => Task.CompletedTask, NullLogger<RequestTraceMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("trace-from-client", context.TraceIdentifier);
        Assert.Equal("trace-from-client", context.Response.Headers[RequestTraceMiddleware.TraceHeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_UsesFallbackTraceId_WhenHeaderMissing()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "server-trace-id";
        var middleware = new RequestTraceMiddleware(_ => Task.CompletedTask, NullLogger<RequestTraceMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("server-trace-id", context.TraceIdentifier);
        Assert.Equal("server-trace-id", context.Response.Headers[RequestTraceMiddleware.TraceHeaderName].ToString());
    }

    [Fact]
    public void ResolveTraceId_IgnoresWhitespaceHeader()
    {
        // Arrange / Act
        var resolved = RequestTraceMiddleware.ResolveTraceId("   ", "fallback-trace-id");

        // Assert
        Assert.Equal("fallback-trace-id", resolved);
    }
}
