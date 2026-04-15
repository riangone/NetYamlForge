using System.Text.Json;
using NetYamlForge.AI.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// DaemonMessageProtocol 单元测试
/// </summary>
public class DaemonMessageProtocolTests
{
    private readonly Mock<ILogger> _loggerMock = new();

    [Fact]
    public void ForProvider_WithQwenCode_ReturnsQwenProtocol()
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");

        // Act
        var json = protocol.FormatRequest("Hello World", "session-123");
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.Equal("message", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("Hello World", doc.RootElement.GetProperty("content").GetString());
        Assert.Equal("session-123", doc.RootElement.GetProperty("session_id").GetString());
    }

    [Fact]
    public void ForProvider_WithClaude_ReturnsClaudeProtocol()
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("claude");

        // Act
        var json = protocol.FormatRequest("Hello World", "session-456");
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.Equal("user", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("Hello World", doc.RootElement.GetProperty("message").GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("result", true)]
    [InlineData("error", true)]
    [InlineData("assistant", false)]
    [InlineData("system", false)]
    [InlineData("progress", false)]
    public void IsResponseComplete_WithResultType_ReturnsTrue(string msgType, bool expected)
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");
        var json = $"{{\"type\":\"{msgType}\",\"result\":\"done\"}}";
        using var doc = JsonDocument.Parse(json);

        // Act
        var isComplete = protocol.IsResponseComplete(msgType, doc.RootElement);

        // Assert
        Assert.Equal(expected, isComplete);
    }

    [Theory]
    [InlineData("assistant", true)]
    [InlineData("system", true)]
    [InlineData("progress", true)]
    [InlineData("result", false)]
    public void IsPartialResponse_WithAssistantType_ReturnsTrue(string msgType, bool expected)
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");

        // Act
        var isPartial = protocol.IsPartialResponse(msgType);

        // Assert
        Assert.Equal(expected, isPartial);
    }

    [Fact]
    public void ExtractResult_WithResultField_ReturnsText()
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");
        var json = "{\"type\":\"result\",\"result\":\"Final answer\"}";
        using var doc = JsonDocument.Parse(json);

        // Act
        var result = protocol.ExtractResult(doc.RootElement);

        // Assert
        Assert.Equal("Final answer", result);
    }

    [Fact]
    public void ExtractResult_WithContentArray_ExtractsTextParts()
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");
        var json = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Part 1\"},{\"type\":\"text\",\"text\":\"Part 2\"}]}}";
        using var doc = JsonDocument.Parse(json);

        // Act
        var result = protocol.ExtractResult(doc.RootElement);

        // Assert
        Assert.Equal("Part 1\nPart 2", result);
    }

    [Fact]
    public void ParseProgressMessage_WithResultMessage_ReturnsCompletedUpdate()
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");
        var json = "{\"type\":\"result\",\"result\":\"Done\",\"session_id\":\"abc-123\"}";

        // Act
        var update = protocol.ParseProgressMessage(json);

        // Assert
        Assert.NotNull(update);
        Assert.Equal("Done", update.Message);
        Assert.Equal(100, update.Progress);
        Assert.Equal(NetYamlForge.AI.Models.TaskStatus.Completed, update.Status);
        Assert.Equal("abc-123", update.SessionId);
    }

    [Fact]
    public void ParseProgressMessage_WithAssistantMessage_ReturnsRunningUpdate()
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");
        var json = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Processing...\"}]}}";

        // Act
        var update = protocol.ParseProgressMessage(json);

        // Assert
        Assert.NotNull(update);
        Assert.Equal("Processing...", update.Message);
        Assert.Equal(NetYamlForge.AI.Models.TaskStatus.Running, update.Status);
    }

    [Fact]
    public void ParseProgressMessage_WithErrorMessage_ReturnsFailedUpdate()
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");
        var json = "{\"type\":\"error\",\"error\":\"Something went wrong\"}";

        // Act
        var update = protocol.ParseProgressMessage(json);

        // Assert
        Assert.NotNull(update);
        Assert.Equal("Something went wrong", update.Message);
        Assert.Equal(NetYamlForge.AI.Models.TaskStatus.Failed, update.Status);
    }

    [Fact]
    public void FormatRequest_WithAllowedTools_IncludesToolsInJson()
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");
        var tools = new List<string> { "Read", "Write", "Bash" };

        // Act
        var json = protocol.FormatRequest("test", null, null, tools);
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.True(doc.RootElement.TryGetProperty("allowed_tools", out var toolsEl));
        Assert.Equal(3, toolsEl.GetArrayLength());
    }

    [Fact]
    public void FormatRequest_WithSystemPromptOverride_IncludesSystemPromptInJson()
    {
        // Arrange
        var protocol = DaemonMessageProtocol.ForProvider("qwen");

        // Act
        var json = protocol.FormatRequest("test", null, "You are a helper");
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.Equal("You are a helper", doc.RootElement.GetProperty("system_prompt").GetString());
    }
}
