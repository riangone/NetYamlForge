using NetYamlForge.AI.Services;
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
        // Arrange - 使用独立的数据库文件避免测试干扰
        var projectTestDbPath = Path.Combine(Path.GetTempPath(), $"test_project_chat_{Guid.NewGuid():N}.db");
        var projectEnvMock = new Mock<IWebHostEnvironment>();
        // 设置为临时目录的根，这样 projects/auto-dealer-demo/chat.db 会创建在这里
        projectEnvMock.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var projectConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatHistory:DbPath"] = projectTestDbPath  // 这个配置实际上不会被使用
            })!
            .Build();
        var projectLogger = new LoggerFactory().CreateLogger<ChatHistoryService>();
        var projectService = new ChatHistoryService(projectConfig, projectEnvMock.Object, projectLogger);

        // Act - Save to project DB (会使用 projects/auto-dealer-demo/chat.db)
        await projectService.SaveMessageAsync(_testUserId, "Project message", "user", chatContext: "auto-dealer-demo", projectName: "auto-dealer-demo");

        // Assert - Message should be in project DB
        var projectDbPath = Path.Combine(Path.GetTempPath(), "projects", "auto-dealer-demo", "chat.db");
        Assert.True(File.Exists(projectDbPath), $"Project DB should exist at {projectDbPath}");
        
        var projectHistory = await projectService.GetHistoryAsync(_testUserId, projectName: "auto-dealer-demo", limit: 10, chatContext: "auto-dealer-demo");
        Assert.Single(projectHistory);
        Assert.Contains("Project message", projectHistory.First().Content);

        // Clean up
        if (File.Exists(projectTestDbPath))
        {
            File.Delete(projectTestDbPath);
        }
    }

    [Fact]
    public async Task SaveMessageAsync_ProjectChat_UsesProjectNameAsUserId()
    {
        // 模拟子项目聊天记录：使用项目名称作为 userId（修复后的行为）
        var projectEnvMock = new Mock<IWebHostEnvironment>();
        projectEnvMock.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var projectConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatHistory:DbPath"] = _testDbPath
            })!
            .Build();
        var projectLogger = new LoggerFactory().CreateLogger<ChatHistoryService>();
        var projectService = new ChatHistoryService(projectConfig, projectEnvMock.Object, projectLogger);

        // Act - 使用独立的项目名称避免测试间冲突
        var projectName = $"test-project-{Guid.NewGuid():N}";
        await projectService.SaveMessageAsync(projectName, "Customer message 1", "user",
            provider: "qwen", chatContext: "dealer-customer", projectName: projectName);
        await projectService.SaveMessageAsync(projectName, "AI response 1", "assistant",
            provider: "qwen", chatContext: "dealer-customer", projectName: projectName);
        await projectService.SaveMessageAsync(projectName, "Staff message 1", "user",
            provider: "qwen", chatContext: "dealer-staff", projectName: projectName);

        // Assert - 验证可以通过项目名称查询到聊天记录
        var customerHistory = await projectService.GetHistoryAsync(projectName, projectName: projectName, limit: 10, chatContext: "dealer-customer");
        var customerMessages = customerHistory.ToList();
        Assert.Equal(2, customerMessages.Count);
        Assert.Contains("Customer message 1", customerMessages[0].Content);
        Assert.Contains("AI response 1", customerMessages[1].Content);

        var staffHistory = await projectService.GetHistoryAsync(projectName, projectName: projectName, limit: 10, chatContext: "dealer-staff");
        Assert.Single(staffHistory);
        Assert.Contains("Staff message 1", staffHistory.First().Content);

        // 验证不会查询到其他上下文的消息
        Assert.DoesNotContain("Staff message 1", customerMessages.Select(m => m.Content));

        // Clean up - 删除测试数据库
        var projectDbPath = Path.Combine(Path.GetTempPath(), "projects", projectName, "chat.db");
        if (File.Exists(projectDbPath))
        {
            File.Delete(projectDbPath);
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
