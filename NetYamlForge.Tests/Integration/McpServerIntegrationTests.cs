using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace NetYamlForge.Tests.Integration;

/// <summary>
/// Phase 4.2: `/mcp` エンドポイント（MCP HTTP transport）の統合テスト。
/// 公式 MCP C# SDK クライアント（<see cref="McpClient"/>）を使用して、
/// ツール一覧の取得とエンティティ CRUD ツールの呼び出しを検証します。
/// </summary>
public class McpServerIntegrationTests : IClassFixture<ApiTestWebApplicationFactory>
{
    private readonly ApiTestWebApplicationFactory _factory;

    public McpServerIntegrationTests(ApiTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<McpClient> CreateMcpClientAsync(string? bearerToken)
    {
        var httpClient = _factory.CreateClient();
        if (!string.IsNullOrEmpty(bearerToken))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://localhost/mcp") },
            httpClient,
            loggerFactory: null,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport);
    }

    private static JsonElement GetStructuredContent(CallToolResult result)
    {
        var text = string.Join("\n", result.Content
            .OfType<TextContentBlock>()
            .Select(c => c.Text));

        if (result.IsError == true)
        {
            Assert.Fail($"Tool call returned an error: {text}");
        }

        if (result.StructuredContent.HasValue)
            return result.StructuredContent.Value;

        Assert.False(string.IsNullOrEmpty(text), "Tool call returned no content.");
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    [Fact]
    public async Task ListTools_ShouldExposeEntityCrudTools()
    {
        await using var client = await CreateMcpClientAsync("admin-api-token");

        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToList();

        Assert.Contains("list_projects", names);
        Assert.Contains("list_entities", names);
        Assert.Contains("get_entity_meta", names);
        Assert.Contains("list_entity_records", names);
        Assert.Contains("get_entity_record", names);
        Assert.Contains("create_entity_record", names);
        Assert.Contains("update_entity_record", names);
        Assert.Contains("delete_entity_record", names);
        Assert.Contains("invoke_entity_action", names);
    }

    [Fact]
    public async Task ListEntityRecords_ShouldReturnSeedData()
    {
        await using var client = await CreateMcpClientAsync("admin-api-token");

        var result = await client.CallToolAsync("list_entity_records", new Dictionary<string, object?>
        {
            ["project"] = ApiTestWebApplicationFactory.ProjectName,
            ["entity"] = "post"
        });

        var content = GetStructuredContent(result);
        Assert.True(content.GetProperty("ok").GetBoolean());

        var data = content.GetProperty("data").GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
    }

    [Fact]
    public async Task CreateAndGetEntityRecord_ShouldRoundTrip()
    {
        await using var client = await CreateMcpClientAsync("admin-api-token");

        var uniqueSlug = $"mcp-test-{Guid.NewGuid():N}";
        var createArgs = new Dictionary<string, object?>
        {
            ["project"] = ApiTestWebApplicationFactory.ProjectName,
            ["entity"] = "post",
            ["data"] = new Dictionary<string, object?>
            {
                ["Title"] = "MCP Test Post",
                ["Slug"] = uniqueSlug,
                ["Summary"] = "Created via MCP create_entity_record tool",
                ["Content"] = "MCP testing content",
                ["AuthorName"] = "Hyperion MCP Test",
                ["Status"] = "draft"
            }
        };

        var createResult = await client.CallToolAsync("create_entity_record", createArgs);
        var createContent = GetStructuredContent(createResult);
        Assert.True(createContent.GetProperty("ok").GetBoolean());

        var createdId = createContent.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(createdId));

        var getResult = await client.CallToolAsync("get_entity_record", new Dictionary<string, object?>
        {
            ["project"] = ApiTestWebApplicationFactory.ProjectName,
            ["entity"] = "post",
            ["id"] = createdId!
        });

        var getContent = GetStructuredContent(getResult);
        Assert.True(getContent.GetProperty("ok").GetBoolean());
        Assert.Equal("MCP Test Post", getContent.GetProperty("data").GetProperty("data").GetProperty("Title").GetString());
    }

    [Fact]
    public async Task ToolCall_OnDisabledApiEntity_ShouldReturnError()
    {
        await using var client = await CreateMcpClientAsync("admin-api-token");

        // comment エンティティは entities/comment.yml で `api` が未設定（既定値 "disabled"）。
        var result = await client.CallToolAsync("list_entity_records", new Dictionary<string, object?>
        {
            ["project"] = ApiTestWebApplicationFactory.ProjectName,
            ["entity"] = "comment"
        });

        var content = GetStructuredContent(result);
        Assert.False(content.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrEmpty(content.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task AnonymousAccess_ToMcpEndpoint_ShouldBeRejected()
    {
        using var httpClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await httpClient.PostAsync("/mcp",
            new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}",
                System.Text.Encoding.UTF8, "application/json"));

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized
            || response.StatusCode == HttpStatusCode.Redirect
            || response.StatusCode == HttpStatusCode.Forbidden);
    }
}
