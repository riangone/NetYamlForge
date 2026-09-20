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
        Assert.True(model.Showcases.Count >= 4);

        // 验证四大核心柱石项目存在
        Assert.Contains(model.Showcases, s => s.Id == "security-demo");
        Assert.Contains(model.Showcases, s => s.Id == "ai-card");
        Assert.Contains(model.Showcases, s => s.Id == "form-forge");
        Assert.Contains(model.Showcases, s => s.Id == "jpiere-cs");

        // 验证宣发文案生成
        Assert.Contains("NetYamlForge", model.PromotionalCopyMarkdown);
        Assert.Contains("Security Guardrails", model.PromotionalCopyMarkdown);
        Assert.Contains("JPiere", model.PromotionalCopyMarkdown);
    }

    [Theory]
    [InlineData("security", "security-demo")]
    [InlineData("ai", "ai-card")]
    [InlineData("forms", "form-forge")]
    [InlineData("erp", "jpiere-cs")]
    [InlineData("workflow", "ai-doc-processor")]
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

    [Fact]
    public void GetSpec_ReturnsSuccessJson_WithCodeSnippet_ForSecurityDemo()
    {
        // Act
        var result = _controller.GetSpec("security-demo") as JsonResult;

        // Assert
        Assert.NotNull(result);
        dynamic data = result.Value!;
        Assert.True(data.success);
        Assert.Equal("security-demo", data.Id);
        Assert.Contains("AST Guardrail", (IEnumerable<string>)data.TechTags);
        Assert.False(string.IsNullOrWhiteSpace(data.CodeSnippet));
    }

    [Fact]
    public void GetSpec_ReturnsSuccessJson_WithCodeSnippet_ForJPiereCs()
    {
        // Act
        var result = _controller.GetSpec("jpiere-cs") as JsonResult;

        // Assert
        Assert.NotNull(result);
        dynamic data = result.Value!;
        Assert.True(data.success);
        Assert.Equal("jpiere-cs", data.Id);
        Assert.Contains("Enterprise ERP", (IEnumerable<string>)data.TechTags);
        Assert.Contains("c_contract", data.CodeSnippet);
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
