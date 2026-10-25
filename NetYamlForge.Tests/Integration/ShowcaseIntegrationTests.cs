using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NetYamlForge.Tests.Integration;

public class ShowcaseIntegrationTests : IClassFixture<ApiTestWebApplicationFactory>
{
    private readonly ApiTestWebApplicationFactory _factory;

    public ShowcaseIntegrationTests(ApiTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetShowcaseIndex_Anonymous_ShouldReturnOk_WithAgentStudio()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var response = await client.GetAsync("/showcase");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // 验证四大核心自举演进样板间全面展现
        Assert.Contains("Agent Studio", html);
        Assert.Contains("Todo Workspace", html);
        Assert.Contains("Modern Blog", html);
        Assert.Contains("Wiki Hub", html);

        // 验证宣发推广卡片文案存在
        Assert.Contains("NetYamlForge", html);
    }

    [Theory]
    [InlineData("agent-studio", "Agent Studio")]
    [InlineData("agent-studio", "MCP Protocol")]
    [InlineData("todo-app", "Todo App")]
    [InlineData("blog-app", "Blog App")]
    [InlineData("wiki-app", "Wiki Hub")]
    public async Task GetShowcaseSpec_ShouldReturnJsonMetadata_AndCodeSnippet(string projectId, string expectedTag)
    {
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/showcase/api/spec/{projectId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(projectId, json.GetProperty("id").GetString());

        var tags = json.GetProperty("techTags");
        Assert.True(tags.ValueKind == JsonValueKind.Array);
        Assert.Contains(tags.EnumerateArray(), t => t.GetString() == expectedTag);

        var codeSnippet = json.GetProperty("codeSnippet").GetString();
        Assert.False(string.IsNullOrWhiteSpace(codeSnippet));
    }
}
