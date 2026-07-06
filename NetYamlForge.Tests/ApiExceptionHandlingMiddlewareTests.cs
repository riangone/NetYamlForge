using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class ApiExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ApiRoute_CatchesUnauthorizedAccessException_Returns401()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/test";
        
        var middleware = new ApiExceptionHandlingMiddleware(
            _ => throw new UnauthorizedAccessException("Unauthorized test"),
            NullLogger<ApiExceptionHandlingMiddleware>.Instance);

        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("unauthorized", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Unauthorized test", root.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokeAsync_ApiRoute_CatchesGenericException_Returns500()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/test";
        
        var middleware = new ApiExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Generic error"),
            NullLogger<ApiExceptionHandlingMiddleware>.Instance);

        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(400, context.Response.StatusCode); // InvalidOperationException yields 400
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_operation", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Generic error", root.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokeAsync_ApiRoute_QuotaExceededException_Returns400WithQuotaCode()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/test";
        
        var middleware = new ApiExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Tenant quota exceeded: max limits reached"),
            NullLogger<ApiExceptionHandlingMiddleware>.Instance);

        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("quota_exceeded", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_NonApiRoute_PropagatesException()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/home/index";
        
        var middleware = new ApiExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("View error"),
            NullLogger<ApiExceptionHandlingMiddleware>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }
}
