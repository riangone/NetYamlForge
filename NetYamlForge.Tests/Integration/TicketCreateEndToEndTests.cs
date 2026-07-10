// ファイル概要: golden-template の ticket CRUD を HTTP レイヤー越しに検証する
// E2E 統合テストです。Create / Edit / Delete / Search の全操作を網羅し、
// これまで抜けていた「フォーム送信の end-to-end CRUD スモーク」を補完する。
//
// 背景:
//   ticket の status / priority は DB では「NOT NULL DEFAULT '...'」だが、
//   （逆生成された）YAML フォームでは任意項目。空欄のまま送信すると、修正前は
//   FormValueValidationService が values に明示的 null を投入し、INSERT で
//   NOT NULL 制約違反 → 500 → 前面は沈黙して「反応なし」に見えていた。
//   修正: 空欄の任意項目は values から除外し、DB の DEFAULT を効かせる。
//   UPDATE では逆に空欄を空文字として保存できるように isUpdate 分岐を追加。

// DCS003 抑制理由: E2E テストはアプリ外部からテナント DB を直接検証するため、
// テスト専用の SQLite 接続を生成します（アプリ側 DI は使用できません）。
#pragma warning disable DCS003

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NetYamlForge.Tests.Integration;

/// <summary>
/// golden-template ticket の CRUD E2E（フォーム送信 → DB 永続化）。
/// </summary>
public class TicketCreateEndToEndTests : IClassFixture<GoldenTemplateWebApplicationFactory>
{
    private const string IndexUrl = "/golden-template/DynamicEntity/Index?entity=ticket";
    private const string CreateUrl = "/golden-template/DynamicEntity/Create?entity=ticket";

    private static readonly Regex AntiForgeryTokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly GoldenTemplateWebApplicationFactory _factory;

    public TicketCreateEndToEndTests(GoldenTemplateWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── CREATE ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTicket_WithBlankOptionalDefaultedColumns_SucceedsAndAppliesDbDefaults()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var subject = $"e2e-ticket-{Guid.NewGuid():N}";
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["subject"] = subject,
            ["status"] = "",
            ["priority"] = "",
            ["created_at"] = ""
        };

        var response = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode,
            $"Create failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var db = new SqliteConnection(_factory.TenantDbConnectionString);
        var created = await db.QuerySingleOrDefaultAsync<(string Subject, string Status, string Priority, string? CreatedAt)?>(
            "SELECT subject AS Subject, status AS Status, priority AS Priority, created_at AS CreatedAt FROM ticket WHERE subject = @subject",
            new { subject });

        Assert.NotNull(created);
        Assert.Equal(subject, created!.Value.Subject);
        Assert.Equal("open", created.Value.Status);
        Assert.Equal("normal", created.Value.Priority);
        Assert.False(string.IsNullOrWhiteSpace(created.Value.CreatedAt));
    }

    [Fact]
    public async Task CreateTicket_WithExplicitOptionalValues_PersistsProvidedValues()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var subject = $"e2e-ticket-explicit-{Guid.NewGuid():N}";
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["subject"] = subject,
            ["status"] = "closed",
            ["priority"] = "high"
        };

        var response = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode,
            $"Create failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var db = new SqliteConnection(_factory.TenantDbConnectionString);
        var created = await db.QuerySingleOrDefaultAsync<(string Status, string Priority)?>(
            "SELECT status AS Status, priority AS Priority FROM ticket WHERE subject = @subject",
            new { subject });

        Assert.NotNull(created);
        Assert.Equal("closed", created!.Value.Status);
        Assert.Equal("high", created.Value.Priority);
    }

    // ── EDIT ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditTicket_ChangesSubject_Succeeds()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var subject = $"edit-before-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["subject"] = subject,
            ["status"] = "open",
            ["priority"] = "normal"
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_factory.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            "SELECT id FROM ticket WHERE subject = @subject", new { subject });
        Assert.NotNull(id);

        var newSubject = $"edit-after-{Guid.NewGuid():N}";
        var editForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["subject"] = newSubject,
            ["status"] = "open",
            ["priority"] = "normal"
        };
        var editUrl = $"/golden-template/DynamicEntity/Edit?entity=ticket&id={id}";
        var editResp = await client.PostAsync(editUrl, new FormUrlEncodedContent(editForm));
        var editBody = await editResp.Content.ReadAsStringAsync();
        Assert.True(editResp.IsSuccessStatusCode,
            $"Edit failed: {(int)editResp.StatusCode} {editResp.StatusCode}\n{Truncate(editBody)}");

        var updated = await db.QuerySingleOrDefaultAsync<string>(
            "SELECT subject FROM ticket WHERE id = @id", new { id });
        Assert.Equal(newSubject, updated);
    }

    [Fact]
    public async Task EditTicket_WithBlankOptionalField_PreservesOldValue()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var subject = $"edit-preserve-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["subject"] = subject,
            ["status"] = "closed",
            ["priority"] = "normal"
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_factory.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            "SELECT id FROM ticket WHERE subject = @subject", new { subject });
        Assert.NotNull(id);

        // Edit: leave status blank (simulates user clearing the field)
        // For NOT NULL columns, blank optional fields are excluded from the SET clause,
        // preserving the existing DB value. This is safe and avoids 500 errors.
        var editForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["subject"] = subject,
            ["status"] = "",
            ["priority"] = "normal"
        };
        var editUrl = $"/golden-template/DynamicEntity/Edit?entity=ticket&id={id}";
        var editResp = await client.PostAsync(editUrl, new FormUrlEncodedContent(editForm));
        var editBody = await editResp.Content.ReadAsStringAsync();
        Assert.True(editResp.IsSuccessStatusCode,
            $"Edit with blank optional field should not crash: {(int)editResp.StatusCode} {editResp.StatusCode}\n{Truncate(editBody)}");

        var updated = await db.QuerySingleOrDefaultAsync<string>(
            "SELECT status FROM ticket WHERE id = @id", new { id });
        Assert.NotNull(updated);
        Assert.Equal("closed", updated);
    }

    // ── DELETE ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTicket_RemovesRecord()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var subject = $"delete-{Guid.NewGuid():N}";
        var createForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["subject"] = subject,
        };
        var createResp = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(createForm));
        Assert.True(createResp.IsSuccessStatusCode, $"Create failed: {createResp.StatusCode}");

        await using var db = new SqliteConnection(_factory.TenantDbConnectionString);
        var id = await db.QuerySingleOrDefaultAsync<long?>(
            "SELECT id FROM ticket WHERE subject = @subject", new { subject });
        Assert.NotNull(id);

        var deleteUrl = $"/golden-template/DynamicEntity/Delete?entity=ticket&id={id}";
        var deleteForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        };
        var deleteResp = await client.PostAsync(deleteUrl, new FormUrlEncodedContent(deleteForm));
        var deleteBody = await deleteResp.Content.ReadAsStringAsync();
        Assert.True(deleteResp.IsSuccessStatusCode,
            $"Delete failed: {(int)deleteResp.StatusCode} {deleteResp.StatusCode}\n{Truncate(deleteBody)}");

        var count = await db.QuerySingleOrDefaultAsync<long>(
            "SELECT COUNT(*) FROM ticket WHERE id = @id", new { id });
        Assert.Equal(0, count);
    }

    // ── SEARCH ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchTicket_BySubject_ReturnsMatchingResults()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var searchNs = $"search-{Guid.NewGuid():N}";
        var subjectAlpha = $"{searchNs}-alpha";
        var subjectBeta = $"{searchNs}-beta";

        var formA = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["subject"] = subjectAlpha,
        };
        var respA = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(formA));
        Assert.True(respA.IsSuccessStatusCode, $"Create alpha failed: {respA.StatusCode}");

        var formB = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["subject"] = subjectBeta,
        };
        var respB = await client.PostAsync(CreateUrl, new FormUrlEncodedContent(formB));
        Assert.True(respB.IsSuccessStatusCode, $"Create beta failed: {respB.StatusCode}");

        var searchUrl = $"/golden-template/DynamicEntity/ListPartial?entity=ticket&search={searchNs}-alpha&count=true";
        var searchResp = await client.GetAsync(searchUrl);
        var searchBody = await searchResp.Content.ReadAsStringAsync();
        Assert.True(searchResp.IsSuccessStatusCode,
            $"Search failed: {(int)searchResp.StatusCode} {searchResp.StatusCode}\n{Truncate(searchBody)}");

        Assert.Contains(subjectAlpha, searchBody);
        Assert.DoesNotContain(subjectBeta, searchBody);
    }

    // ── ヘルパー ───────────────────────────────────────────────────────────

    private HttpClient CreateClient() =>
        _factory.CreateDefaultClient(new CookieContainerHandler());

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
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
