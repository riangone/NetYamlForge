using NetYamlForge.Services.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Xunit;

namespace NetYamlForge.Tests.Services.AI;

public class ChatHistoryServiceTests : IDisposable
{
    private readonly ChatHistoryService _service;
    private readonly string _testDbPath;
    private readonly string _testUserId = "test-user-001";

    public ChatHistoryServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_chathistory_{Guid.NewGuid():N}.db");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatHistory:DbPath"] = _testDbPath
            })!
            .Build();

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var logger = new LoggerFactory().CreateLogger<ChatHistoryService>();
        _service = new ChatHistoryService(config, envMock.Object, logger);
    }

    [Fact]
    public async Task SaveMessageAsync_SavesMessage_WithChatContext()
    {
        // Act
        await _service.SaveMessageAsync(_testUserId, "Hello", "user", provider: "qwen", chatContext: "framework", projectName: null);
        await _service.SaveMessageAsync(_testUserId, "Hi there!", "assistant", provider: "qwen", chatContext: "framework", projectName: null);

        // Assert
        var history = await _service.GetHistoryAsync(_testUserId, projectName: null, limit: 10, chatContext: "framework");
        var messages = history.ToList();

        Assert.Equal(2, messages.Count);
        Assert.Equal("Hello", messages[0].Content);
        Assert.Equal("user", messages[0].Type);
        Assert.Equal("Hi there!", messages[1].Content);
        Assert.Equal("assistant", messages[1].Type);
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByChatContext()
    {
        // Arrange
        await _service.SaveMessageAsync(_testUserId, "Framework message", "user", chatContext: "framework", projectName: null);
        await _service.SaveMessageAsync(_testUserId, "Dealer customer message", "user", chatContext: "dealer-customer", projectName: null);
        await _service.SaveMessageAsync(_testUserId, "Dealer staff message", "user", chatContext: "dealer-staff", projectName: null);

        // Act & Assert
        var frameworkHistory = await _service.GetHistoryAsync(_testUserId, projectName: null, limit: 10, chatContext: "framework");
        Assert.Single(frameworkHistory);
        Assert.Contains("Framework message", frameworkHistory.First().Content);

        var dealerCustomerHistory = await _service.GetHistoryAsync(_testUserId, projectName: null, limit: 10, chatContext: "dealer-customer");
        Assert.Single(dealerCustomerHistory);
        Assert.Contains("Dealer customer message", dealerCustomerHistory.First().Content);

        var dealerStaffHistory = await _service.GetHistoryAsync(_testUserId, projectName: null, limit: 10, chatContext: "dealer-staff");
        Assert.Single(dealerStaffHistory);
        Assert.Contains("Dealer staff message", dealerStaffHistory.First().Content);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsAllContexts_WhenChatContextIsNull()
    {
        // Arrange
        await _service.SaveMessageAsync(_testUserId, "Framework message", "user", chatContext: "framework", projectName: null);
        await _service.SaveMessageAsync(_testUserId, "Dealer customer message", "user", chatContext: "dealer-customer", projectName: null);
        await _service.SaveMessageAsync(_testUserId, "Dealer staff message", "user", chatContext: "dealer-staff", projectName: null);

        // Act
        var allHistory = await _service.GetHistoryAsync(_testUserId, projectName: null, limit: 10, chatContext: null);

        // Assert
        var messages = allHistory.ToList();
        Assert.Equal(3, messages.Count);
    }

    // [已禁用] 禁止清空聊天记录 - 2026-04-03
    // [Fact]
    // public async Task ClearHistoryAsync_ClearsByChatContext()
    // {
    //     // Arrange
    //     await _service.SaveMessageAsync(_testUserId, "Framework message 1", "user", chatContext: "framework", projectName: null);
    //     await _service.SaveMessageAsync(_testUserId, "Framework message 2", "user", chatContext: "framework", projectName: null);
    //     await _service.SaveMessageAsync(_testUserId, "Dealer message", "user", chatContext: "dealer-customer", projectName: null);
    //
    //     // Act
    //     await _service.ClearHistoryAsync(_testUserId, chatContext: "framework", projectName: null);
    //
    //     // Assert
    //     var frameworkHistory = await _service.GetHistoryAsync(_testUserId, projectName: null, limit: 10, chatContext: "framework");
    //     Assert.Empty(frameworkHistory);
    //
    //     var dealerHistory = await _service.GetHistoryAsync(_testUserId, projectName: null, limit: 10, chatContext: "dealer-customer");
    //     Assert.Single(dealerHistory);
    // }

    [Fact]
    public async Task SaveMessageAsync_StoresProvider()
    {
        // Act
        await _service.SaveMessageAsync(_testUserId, "Message", "user", provider: "claude", chatContext: "framework", projectName: null);

        // Assert
        var history = await _service.GetHistoryAsync(_testUserId, projectName: null, limit: 10, chatContext: "framework");
        var message = history.First();
        Assert.Equal("claude", message.Provider);
    }

    [Fact]
    public async Task SaveMessageAsync_UsesProjectDatabase()
    {
        // Arrange
        var projectEnvMock = new Mock<IWebHostEnvironment>();
        var projectTestDb = Path.Combine(Path.GetTempPath(), $"test_project_chat_{Guid.NewGuid():N}.db");
        projectEnvMock.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());
        
        var projectConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatHistory:DbPath"] = _testDbPath
            })!
            .Build();
        var projectLogger = new LoggerFactory().CreateLogger<ChatHistoryService>();
        var projectService = new ChatHistoryService(projectConfig, projectEnvMock.Object, projectLogger);

        // Act - Save to project DB
        await projectService.SaveMessageAsync(_testUserId, "Project message", "user", chatContext: "auto-dealer-demo", projectName: "auto-dealer-demo");

        // Assert - Message should be in project DB, not global DB
        var projectHistory = await projectService.GetHistoryAsync(_testUserId, projectName: "auto-dealer-demo", limit: 10, chatContext: "auto-dealer-demo");
        Assert.Single(projectHistory);
        Assert.Contains("Project message", projectHistory.First().Content);

        // Clean up
        if (File.Exists(projectTestDb))
        {
            File.Delete(projectTestDb);
        }
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
