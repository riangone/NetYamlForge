// ファイル概要: ai-card の ai_profile CRUD E2E テストです。
// --ai-scaffold によって自動生成されました。

#pragma warning disable DCS003

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NetYamlForge.Tests.Integration;

public class AiCardAiProfileCrudEndToEndTests : IClassFixture<AiCardTestFixture>
{
    private const string IndexUrl = "/ai-card/DynamicEntity/Index?entity=ai_profile";
    private const string CreateUrl = "/ai-card/DynamicEntity/Create?entity=ai_profile";

    private static readonly Regex AntiForgeryTokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AiCardTestFixture _fixture;

    public AiCardAiProfileCrudEndToEndTests(AiCardTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ── CREATE ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAiProfile_WithBlankOptionalDefaultedColumns_SucceedsAndAppliesDbDefaults()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ai_identity_id"] = $"e2e-ai_identity_id-{Guid.NewGuid():N}",
        };

        var response = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Create failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var count = await db.QuerySingleOrDefaultAsync<long>(
            $"SELECT COUNT(*) FROM [ai_profile] WHERE [ai_identity_id] = @val",
            new { val = form["ai_identity_id"] });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateAiProfile_WithExplicitOptionalValues_PersistsProvidedValues()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ai_identity_id"] = $"e2e-ai_identity_id-{Guid.NewGuid():N}",
            ["goals_json"] = $"e2e-goals_json-{Guid.NewGuid():N}",
            ["can_share_json"] = $"e2e-can_share_json-{Guid.NewGuid():N}",
            ["cannot_share_json"] = $"e2e-cannot_share_json-{Guid.NewGuid():N}",
            ["expertise_json"] = $"e2e-expertise_json-{Guid.NewGuid():N}",
            ["greeting_message"] = $"e2e-greeting_message-{Guid.NewGuid():N}",
            ["ai_instructions"] = $"e2e-ai_instructions-{Guid.NewGuid():N}",
            ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        var response = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Create failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var count = await db.QuerySingleOrDefaultAsync<long>(
            $"SELECT COUNT(*) FROM [ai_profile] WHERE [ai_identity_id] = @val",
            new { val = form["ai_identity_id"] });
        Assert.Equal(1, count);
    }

    // ── EDIT ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditAiProfile_ChangesAiIdentityId_Succeeds()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var originalVal = $"orig-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ai_identity_id"] = originalVal,
            ["goals_json"] = $"e2e-goals_json-{Guid.NewGuid():N}",
            ["can_share_json"] = $"e2e-can_share_json-{Guid.NewGuid():N}",
            ["cannot_share_json"] = $"e2e-cannot_share_json-{Guid.NewGuid():N}",
            ["expertise_json"] = $"e2e-expertise_json-{Guid.NewGuid():N}",
            ["greeting_message"] = $"e2e-greeting_message-{Guid.NewGuid():N}",
            ["ai_instructions"] = $"e2e-ai_instructions-{Guid.NewGuid():N}",
            ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            $"SELECT [id] FROM [ai_profile] WHERE [ai_identity_id] = @val",
            new { val = originalVal });
        Assert.NotNull(id);

        var newVal = $"edited-{Guid.NewGuid():N}";
        var editForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ai_identity_id"] = newVal,
            ["goals_json"] = $"e2e-goals_json-{Guid.NewGuid():N}",
            ["can_share_json"] = $"e2e-can_share_json-{Guid.NewGuid():N}",
            ["cannot_share_json"] = $"e2e-cannot_share_json-{Guid.NewGuid():N}",
            ["expertise_json"] = $"e2e-expertise_json-{Guid.NewGuid():N}",
            ["greeting_message"] = $"e2e-greeting_message-{Guid.NewGuid():N}",
            ["ai_instructions"] = $"e2e-ai_instructions-{Guid.NewGuid():N}",
            ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var editUrl = $"/ai-card/DynamicEntity/Edit?entity=ai_profile&id={id}";
        var editResp = await client.PostAsync(editUrl, new FormUrlEncodedContent(editForm));
        var editBody = await editResp.Content.ReadAsStringAsync();
        Assert.True(editResp.IsSuccessStatusCode,
            $"Edit failed: {(int)editResp.StatusCode} {editResp.StatusCode}\n{Truncate(editBody)}");

        var updated = await db.QuerySingleOrDefaultAsync<string>(
            $"SELECT [ai_identity_id] FROM [ai_profile] WHERE [id] = @id",
            new { id });
        Assert.Equal(newVal, updated);
    }

    [Fact]
    public async Task EditAiProfile_WithBlankOptionalField_PreservesOldValue()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var originalVal = $"preserve-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ai_identity_id"] = originalVal,
            ["goals_json"] = $"e2e-goals_json-{Guid.NewGuid():N}",
            ["can_share_json"] = $"e2e-can_share_json-{Guid.NewGuid():N}",
            ["cannot_share_json"] = $"e2e-cannot_share_json-{Guid.NewGuid():N}",
            ["expertise_json"] = $"e2e-expertise_json-{Guid.NewGuid():N}",
            ["greeting_message"] = $"e2e-greeting_message-{Guid.NewGuid():N}",
            ["ai_instructions"] = $"e2e-ai_instructions-{Guid.NewGuid():N}",
            ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            $"SELECT [id] FROM [ai_profile] WHERE [ai_identity_id] = @val",
            new { val = originalVal });
        Assert.NotNull(id);

        var blankField = "goals_json";
        var editForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ai_identity_id"] = $"e2e-ai_identity_id-{Guid.NewGuid():N}",
            ["goals_json"] = "",
            ["can_share_json"] = $"e2e-can_share_json-{Guid.NewGuid():N}",
            ["cannot_share_json"] = $"e2e-cannot_share_json-{Guid.NewGuid():N}",
            ["expertise_json"] = $"e2e-expertise_json-{Guid.NewGuid():N}",
            ["greeting_message"] = $"e2e-greeting_message-{Guid.NewGuid():N}",
            ["ai_instructions"] = $"e2e-ai_instructions-{Guid.NewGuid():N}",
            ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var editUrl = $"/ai-card/DynamicEntity/Edit?entity=ai_profile&id={id}";
        var editResp = await client.PostAsync(editUrl, new FormUrlEncodedContent(editForm));
        var editBody = await editResp.Content.ReadAsStringAsync();
        Assert.True(editResp.IsSuccessStatusCode,
            $"Edit with blank optional field should not crash: {(int)editResp.StatusCode} {editResp.StatusCode}\n{Truncate(editBody)}");

        var keptValue = await db.QuerySingleOrDefaultAsync<string>(
            $"SELECT [{blankField}] FROM [ai_profile] WHERE [id] = @id",
            new { id });
        Assert.Equal(createForm[blankField], keptValue);
    }

    // ── DELETE ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAiProfile_RemovesRecord()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var subjectVal = $"del-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ai_identity_id"] = subjectVal,
            ["goals_json"] = $"e2e-goals_json-{Guid.NewGuid():N}",
            ["can_share_json"] = $"e2e-can_share_json-{Guid.NewGuid():N}",
            ["cannot_share_json"] = $"e2e-cannot_share_json-{Guid.NewGuid():N}",
            ["expertise_json"] = $"e2e-expertise_json-{Guid.NewGuid():N}",
            ["greeting_message"] = $"e2e-greeting_message-{Guid.NewGuid():N}",
            ["ai_instructions"] = $"e2e-ai_instructions-{Guid.NewGuid():N}",
            ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            $"SELECT [id] FROM [ai_profile] WHERE [ai_identity_id] = @val",
            new { val = subjectVal });
        Assert.NotNull(id);

        var deleteUrl = $"/ai-card/DynamicEntity/Delete?entity=ai_profile&id={id}";
        var deleteForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        };
        var deleteResp = await client.PostAsync(deleteUrl, new FormUrlEncodedContent(deleteForm));
        var deleteBody = await deleteResp.Content.ReadAsStringAsync();
        Assert.True(deleteResp.IsSuccessStatusCode,
            $"Delete failed: {(int)deleteResp.StatusCode} {deleteResp.StatusCode}\n{Truncate(deleteBody)}");

        var count = await db.QuerySingleOrDefaultAsync<long>(
            $"SELECT COUNT(*) FROM [ai_profile] WHERE [id] = @id",
            new { id });
        Assert.Equal(0, count);
    }

    // ── SEARCH ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAiProfile_ByAiIdentityId_ReturnsMatchingResults()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var searchNs = $"search-{Guid.NewGuid():N}";
        var valAlpha = $"{searchNs}-alpha";
        var valBeta = $"{searchNs}-beta";

        var formA = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ai_identity_id"] = valAlpha,
            ["goals_json"] = $"e2e-goals_json-{Guid.NewGuid():N}",
            ["can_share_json"] = $"e2e-can_share_json-{Guid.NewGuid():N}",
            ["cannot_share_json"] = $"e2e-cannot_share_json-{Guid.NewGuid():N}",
            ["expertise_json"] = $"e2e-expertise_json-{Guid.NewGuid():N}",
            ["greeting_message"] = $"e2e-greeting_message-{Guid.NewGuid():N}",
            ["ai_instructions"] = $"e2e-ai_instructions-{Guid.NewGuid():N}",
            ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var respA = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(formA));
        Assert.True(respA.IsSuccessStatusCode, $"Create alpha failed: {respA.StatusCode}");

        var formB = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ai_identity_id"] = valBeta,
            ["goals_json"] = $"e2e-goals_json-{Guid.NewGuid():N}",
            ["can_share_json"] = $"e2e-can_share_json-{Guid.NewGuid():N}",
            ["cannot_share_json"] = $"e2e-cannot_share_json-{Guid.NewGuid():N}",
            ["expertise_json"] = $"e2e-expertise_json-{Guid.NewGuid():N}",
            ["greeting_message"] = $"e2e-greeting_message-{Guid.NewGuid():N}",
            ["ai_instructions"] = $"e2e-ai_instructions-{Guid.NewGuid():N}",
            ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var respB = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(formB));
        Assert.True(respB.IsSuccessStatusCode, $"Create beta failed: {respB.StatusCode}");

        var searchUrl = $"/ai-card/DynamicEntity/ListPartial?entity=ai_profile&search={searchNs}-alpha&count=true";
        var searchResp = await client.GetAsync(searchUrl);
        var searchBody = await searchResp.Content.ReadAsStringAsync();
        Assert.True(searchResp.IsSuccessStatusCode,
            $"Search failed: {(int)searchResp.StatusCode} {searchResp.StatusCode}\n{Truncate(searchBody)}");

        Assert.Contains(valAlpha, searchBody);
        Assert.DoesNotContain(valBeta, searchBody);
    }

    // ── ヘルパー ───────────────────────────────────────────────────────────

    private HttpClient CreateClient() =>
        _fixture.CreateDefaultClient(new CookieContainerHandler());

    private async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync(IndexUrl);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiForgeryTokenRegex.Match(html);
        Assert.True(match.Success, "Failed to get anti-forgery token from index page.");
        return match.Groups[1].Value;
    }

    private static string Truncate(string text) =>
        text.Length <= 2000 ? text : text[..2000];
}
