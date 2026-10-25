using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Controllers;
using NetYamlForge.Models;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests.Controllers;

public class ShowcaseControllerTests
{
    private readonly Mock<ILogger<ShowcaseController>> _loggerMock;
    private readonly ProjectScope _projectScope;
    private readonly ShowcaseController _controller;

    private class TestProjectManager : ProjectManager
    {
        public TestProjectManager() : base() { }
    }

    public ShowcaseControllerTests()
    {
        _loggerMock = new Mock<ILogger<ShowcaseController>>();
        _projectScope = new ProjectScope();

        var pm = new TestProjectManager();
        _controller = new ShowcaseController(pm, _projectScope, _loggerMock.Object);

        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public void Index_ReturnsAllShowcases_WhenCategoryIsNullOrAll()
    {
        // Act
        var result = _controller.Index(null) as ViewResult;

        // Assert
        Assert.NotNull(result);
        var model = Assert.IsType<ShowcaseIndexViewModel>(result.Model);
        Assert.NotEmpty(model.Showcases);
        Assert.True(model.Showcases.Count >= 1);

        // 验证核心自举演进落地项目均存在
        Assert.Contains(model.Showcases, s => s.Id == "agent-studio");
        Assert.Contains(model.Showcases, s => s.Id == "todo-app");
        Assert.Contains(model.Showcases, s => s.Id == "blog-app");
        Assert.Contains(model.Showcases, s => s.Id == "wiki-app");

        // 验证宣发文案生成
        Assert.Contains("NetYamlForge", model.PromotionalCopyMarkdown);
        Assert.Contains("Agent Studio", model.PromotionalCopyMarkdown);
        Assert.Contains("Todo Workspace", model.PromotionalCopyMarkdown);
        Assert.Contains("Modern Blog", model.PromotionalCopyMarkdown);
        Assert.Contains("Wiki Hub", model.PromotionalCopyMarkdown);
    }

    [Theory]
    [InlineData("ai", "agent-studio")]
    [InlineData("productivity", "todo-app")]
    [InlineData("content", "blog-app")]
    [InlineData("knowledge", "wiki-app")]
    public void Index_FiltersByCategory_Correctly(string category, string expectedProjectId)
    {
        // Act
        var result = _controller.Index(category) as ViewResult;

        // Assert
        Assert.NotNull(result);
        var model = Assert.IsType<ShowcaseIndexViewModel>(result.Model);
        Assert.NotEmpty(model.Showcases);
        Assert.All(model.Showcases, s => Assert.Equal(category, s.Category));
        Assert.Contains(model.Showcases, s => s.Id == expectedProjectId);
    }

    [Theory]
    [InlineData("agent-studio", "Agent Studio")]
    [InlineData("todo-app", "Todo App")]
    [InlineData("blog-app", "Blog App")]
    [InlineData("wiki-app", "Wiki Hub")]
    public void GetSpec_ReturnsSuccessJson_WithCodeSnippet_ForLiveApps(string projectId, string expectedTag)
    {
        // Act
        var result = _controller.GetSpec(projectId) as JsonResult;

        // Assert
        Assert.NotNull(result);
        dynamic data = result.Value!;
        Assert.True(data.success);
        Assert.Equal(projectId, data.Id);
        Assert.Contains(expectedTag, (IEnumerable<string>)data.TechTags);
        Assert.False(string.IsNullOrWhiteSpace(data.CodeSnippet));
    }

    [Fact]
    public void GetSpec_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var result = _controller.GetSpec("non-existent-project") as NotFoundObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(404, result.StatusCode);
    }
}
