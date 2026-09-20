using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NetYamlForge.Tests.Integration;

public class ApiEntityIntegrationTests : IClassFixture<ApiTestWebApplicationFactory>
{
    private readonly ApiTestWebApplicationFactory _factory;

    public ApiEntityIntegrationTests(ApiTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnonymousRequest_ToApiEndpoint_ShouldBeRedirectedOrUnauthorized()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/security-demo/note");

        Assert.True(response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidTokenRequest_ShouldPerformFullApiCrudOperations()
    {
        using var client = _factory.CreateClient();
        
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-api-token");

        // 1. GET /api/security-demo/note/meta
        var metaResponse = await client.GetAsync("/api/security-demo/note/meta");
        Assert.Equal(HttpStatusCode.OK, metaResponse.StatusCode);
        var metaData = await metaResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("note", metaData.GetProperty("entity").GetString());

        // 2. GET /api/security-demo/note
        var listResponse = await client.GetAsync("/api/security-demo/note");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listData = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(listData.GetProperty("data").ValueKind == JsonValueKind.Array);

        // 3. POST /api/security-demo/note
        var uniqueSlug = $"api-test-{Guid.NewGuid():N}";
        var newPost = new Dictionary<string, object>
        {
            ["Title"] = "REST API Post",
            ["Slug"] = uniqueSlug,
            ["Summary"] = "Created via REST API E2E Integration test",
            ["Content"] = "REST API testing content",
            ["AuthorName"] = "Hyperion Test",
            ["Status"] = "draft"
        };
        var createResponse = await client.PostAsJsonAsync("/api/security-demo/note", newPost);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdDto = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = createdDto.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(createdId));

        // 4. GET /api/security-demo/note/{id}
        var getResponse = await client.GetAsync($"/api/security-demo/note/{createdId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getDto = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REST API Post", getDto.GetProperty("data").GetProperty("Title").GetString());

        // 5. PUT /api/security-demo/note/{id}
        var updatedPost = new Dictionary<string, object>
        {
            ["Title"] = "REST API Post Updated",
            ["Slug"] = uniqueSlug,
            ["Summary"] = "Updated via REST API E2E Integration test",
            ["Content"] = "REST API updated testing content",
            ["AuthorName"] = "Hyperion Test",
            ["Status"] = "published"
        };
        var updateResponse = await client.PutAsJsonAsync($"/api/security-demo/note/{createdId}", updatedPost);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updateDto = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REST API Post Updated", updateDto.GetProperty("data").GetProperty("Title").GetString());

        // 6. DELETE /api/security-demo/note/{id}
        var deleteResponse = await client.DeleteAsync($"/api/security-demo/note/{createdId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // 7. GET /api/security-demo/note/{id}
        var getAfterDeleteResponse = await client.GetAsync($"/api/security-demo/note/{createdId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
    }
}
