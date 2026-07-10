// ファイル概要: todo-app の todo_item CRUD E2E テストです。
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

public class TodoItemCrudEndToEndTests : IClassFixture<TodoAppTestFixture>
{
    private const string IndexUrl = "/todo-app/DynamicEntity/Index?entity=todo_item";
    private const string CreateUrl = "/todo-app/DynamicEntity/Create?entity=todo_item";

    private static readonly Regex AntiForgeryTokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly TodoAppTestFixture _fixture;

    public TodoItemCrudEndToEndTests(TodoAppTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ── CREATE ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTodoItem_WithBlankOptionalDefaultedColumns_SucceedsAndAppliesDbDefaults()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["title"] = $"e2e-title-{Guid.NewGuid():N}",
        };

        var response = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Create failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var count = await db.QuerySingleOrDefaultAsync<long>(
            $"SELECT COUNT(*) FROM [todo_item] WHERE [title] = @val",
            new { val = form["title"] });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateTodoItem_WithExplicitOptionalValues_PersistsProvidedValues()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["title"] = $"e2e-title-{Guid.NewGuid():N}",
            ["is_done"] = "1",
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        var response = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Create failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var count = await db.QuerySingleOrDefaultAsync<long>(
            $"SELECT COUNT(*) FROM [todo_item] WHERE [title] = @val",
            new { val = form["title"] });
        Assert.Equal(1, count);
    }

    // ── EDIT ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditTodoItem_ChangesTitle_Succeeds()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var originalVal = $"orig-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["title"] = originalVal,
            ["is_done"] = "1",
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            $"SELECT [id] FROM [todo_item] WHERE [title] = @val",
            new { val = originalVal });
        Assert.NotNull(id);

        var newVal = $"edited-{Guid.NewGuid():N}";
        var editForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["title"] = newVal,
            ["is_done"] = "1",
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var editUrl = $"/todo-app/DynamicEntity/Edit?entity=todo_item&id={id}";
        var editResp = await client.PostAsync(editUrl, new FormUrlEncodedContent(editForm));
        var editBody = await editResp.Content.ReadAsStringAsync();
        Assert.True(editResp.IsSuccessStatusCode,
            $"Edit failed: {(int)editResp.StatusCode} {editResp.StatusCode}\n{Truncate(editBody)}");

        var updated = await db.QuerySingleOrDefaultAsync<string>(
            $"SELECT [title] FROM [todo_item] WHERE [id] = @id",
            new { id });
        Assert.Equal(newVal, updated);
    }

    [Fact]
    public async Task EditTodoItem_WithBlankOptionalField_PreservesOldValue()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var originalVal = $"preserve-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["title"] = originalVal,
            ["is_done"] = "1",
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            $"SELECT [id] FROM [todo_item] WHERE [title] = @val",
            new { val = originalVal });
        Assert.NotNull(id);

        // No optional columns to test blank-preserve behavior.
    }

    // ── DELETE ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTodoItem_RemovesRecord()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var subjectVal = $"del-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["title"] = subjectVal,
            ["is_done"] = "1",
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_fixture.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            $"SELECT [id] FROM [todo_item] WHERE [title] = @val",
            new { val = subjectVal });
        Assert.NotNull(id);

        var deleteUrl = $"/todo-app/DynamicEntity/Delete?entity=todo_item&id={id}";
        var deleteForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        };
        var deleteResp = await client.PostAsync(deleteUrl, new FormUrlEncodedContent(deleteForm));
        var deleteBody = await deleteResp.Content.ReadAsStringAsync();
        Assert.True(deleteResp.IsSuccessStatusCode,
            $"Delete failed: {(int)deleteResp.StatusCode} {deleteResp.StatusCode}\n{Truncate(deleteBody)}");

        var count = await db.QuerySingleOrDefaultAsync<long>(
            $"SELECT COUNT(*) FROM [todo_item] WHERE [id] = @id",
            new { id });
        Assert.Equal(0, count);
    }

    // ── SEARCH ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchTodoItem_ByTitle_ReturnsMatchingResults()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var searchNs = $"search-{Guid.NewGuid():N}";
        var valAlpha = $"{searchNs}-alpha";
        var valBeta = $"{searchNs}-beta";

        var formA = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["title"] = valAlpha,
            ["is_done"] = "1",
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var respA = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(formA));
        Assert.True(respA.IsSuccessStatusCode, $"Create alpha failed: {respA.StatusCode}");

        var formB = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["title"] = valBeta,
            ["is_done"] = "1",
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var respB = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(formB));
        Assert.True(respB.IsSuccessStatusCode, $"Create beta failed: {respB.StatusCode}");

        var searchUrl = $"/todo-app/DynamicEntity/ListPartial?entity=todo_item&search={searchNs}-alpha&count=true";
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