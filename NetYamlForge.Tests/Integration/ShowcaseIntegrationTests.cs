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
    public async Task GetShowcaseIndex_Anonymous_ShouldReturnOk_WithAllPillars()
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

        // 验证四大核心样板间全部展现
        Assert.Contains("Security Guardrails", html);
        Assert.Contains("AI Card", html);
        Assert.Contains("FormForge", html);
        Assert.Contains("JPiere Contract Service", html);

        // 验证宣发推广卡片文案存在
        Assert.Contains("NetYamlForge", html);
    }

    [Theory]
    [InlineData("security-demo", "AST Guardrail")]
    [InlineData("ai-card", "AHP Protocol")]
    [InlineData("form-forge", "Form Builder")]
    [InlineData("jpiere-cs", "Enterprise ERP")]
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
