// ファイル概要: 「YAML 設定 → ランタイム挙動」パイプラインの E2E 統合テストです。
// WebApplicationFactory で blog サンプルプロジェクトを実際に起動し、
//   1. YAML 実体定義から構築された一覧ページの表示（GET）
//   2. HTTP 経由の CRUD 作成 → テナント SQLite DB への永続化
//   3. YAML 定義のカスタムアクション実行 → DB 変更
// を HTTP レイヤー越しに検証します。レンダリング層・コマンド層改修時の兜底です。

// DCS003 抑制理由: E2E テストはアプリ外部からテナント DB を直接検証するため、
// テスト専用の SQLite 接続を生成します（アプリ側 DI は使用できません）。
#pragma warning disable DCS003

using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NetYamlForge.Tests.Integration;

/// <summary>
/// blog プロジェクトを使った最小 E2E チェーン（一覧表示 → 作成 → アクション実行）。
/// </summary>
public class YamlPipelineEndToEndTests : IClassFixture<NetYamlForgeWebApplicationFactory>
{
    private const string IndexUrl = "/blog/DynamicEntity/Index?entity=post";

    private static readonly Regex AntiForgeryTokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly NetYamlForgeWebApplicationFactory _factory;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public YamlPipelineEndToEndTests(NetYamlForgeWebApplicationFactory factory, Xunit.Abstractions.ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    // ── 1. YAML 定義 → 一覧ページ表示 ─────────────────────────────────────

    [Fact]
    public async Task ListPage_BootsFromYamlConfig_ReturnsSeededRows()
    {
        // YAML(entities/post.yml) から構築された一覧ページが表示され、
        // 起動時に init_seed.sql で再構築されたシードデータが含まれること
        using var client = CreateClient();

        var response = await client.GetAsync(IndexUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // 一覧コンテナ（行は HTMX の ListPartial で遅延ロードされる）
        Assert.Contains("ListPartial", html);
        // フォーム POST 用の antiforgery トークンが描画されていること
        Assert.Matches(AntiForgeryTokenRegex, html);

        // HTMX が呼び出す一覧パーシャルにシード記事が含まれること
        var listResponse = await client.GetAsync("/blog/DynamicEntity/ListPartial?entity=post");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listHtml = await listResponse.Content.ReadAsStringAsync();

        // init_seed.sql のシード記事（Slug 列は一覧に表示される）
        Assert.Contains("oss-contribution-guide", listHtml);
    }

    [Fact]
    public async Task ListPage_UnknownEntity_ReturnsNotFound()
    {
        // YAML に存在しないエンティティは 404 になること
        using var client = CreateClient();

        var response = await client.GetAsync("/blog/DynamicEntity/Index?entity=no-such-entity");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── 2. HTTP 経由の CRUD 作成 → テナント DB 永続化 ───────────────────────

    [Fact]
    public async Task CreatePost_ThroughHttpLayer_PersistsRowInTenantDatabase()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var slug = $"e2e-create-{Guid.NewGuid():N}";
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Title"] = "E2E Created Post",
            ["Slug"] = slug,
            ["Summary"] = "Created by the YAML pipeline e2e test",
            ["Content"] = "## e2e\ncreated through the HTTP layer",
            ["AuthorName"] = "E2E Tester",
            ["Status"] = "draft",
            // 注意: コマンド層は YAML columns の全物理列を明示 INSERT する
            // （未送信列は NULL になり DB デフォルトが効かない）ため、
            // NOT NULL 列はすべて値を送信する。
            ["FeaturedFlag"] = "0",
            ["ViewCount"] = "0",
            ["CreatedAt"] = "2026-06-12 10:00:00",
            ["UpdatedAt"] = "2026-06-12 10:00:00"
        };

        var response = await client.PostAsync(
            "/blog/DynamicEntity/Create?entity=post",
            new FormUrlEncodedContent(form));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Create failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        // テナント SQLite DB に行が永続化されていること（hooks 経由の作成パス）
        await using var db = new SqliteConnection(_factory.TenantDbConnectionString);
        var created = await db.QuerySingleOrDefaultAsync<(string Title, string Status)?>(
            "SELECT Title, Status FROM Post WHERE Slug = @slug", new { slug });

        Assert.NotNull(created);
        Assert.Equal("E2E Created Post", created.Value.Title);
        Assert.Equal("draft", created.Value.Status);
    }

    // ── 3. YAML 定義アクション実行 → DB 変更 ───────────────────────────────

    [Fact]
    public async Task PublishAction_ThroughHttpLayer_UpdatesStatusInTenantDatabase()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        // 準備: 対象のドラフト記事を直接 DB に作成（テスト間の独立性を保つ）
        var slug = $"e2e-publish-{Guid.NewGuid():N}";
        long postId;
        await using (var db = new SqliteConnection(_factory.TenantDbConnectionString))
        {
            postId = await db.ExecuteScalarAsync<long>(
                """
                INSERT INTO Post (Title, Slug, AuthorName, Status)
                VALUES ('E2E Publish Target', @slug, 'E2E Tester', 'draft');
                SELECT last_insert_rowid();
                """,
                new { slug });
        }

        // 実行: YAML（entities/post.yml）の actions.publish を HTTP 経由で起動。
        // HX-Request ヘッダー付きで一覧パーシャルが返るパスを使う。
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/blog/DynamicEntity/InvokeAction?entity=post&actionKey=publish&id={postId}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            })
        };
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"InvokeAction failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        // 検証: publish_post ハンドラー（projects/blog/Hooks）による DB 変更
        await using (var db = new SqliteConnection(_factory.TenantDbConnectionString))
        {
            var row = await db.QuerySingleAsync<(string Status, string? PublishedAt)>(
                "SELECT Status, PublishedAt FROM Post WHERE Id = @postId", new { postId });

            Assert.Equal("published", row.Status);
            Assert.False(string.IsNullOrWhiteSpace(row.PublishedAt));
        }
    }

    [Fact]
    public async Task DetailPage_BootsFromYamlConfig_ReturnsDetailInfo()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/blog/DynamicEntity/DetailPage?entity=post&id=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var htmlRaw = await response.Content.ReadAsStringAsync();
        var html = System.Net.WebUtility.HtmlDecode(htmlRaw);

        Assert.Contains("NetYamlForge 入門", html);
        Assert.Contains("netyamlforge-getting-started", html);
    }

    [Fact]
    public async Task UpdatePost_ThroughHttpLayer_UpdatesRowInTenantDatabase()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var slug = $"e2e-update-{Guid.NewGuid():N}";
        long postId;
        await using (var db = new SqliteConnection(_factory.TenantDbConnectionString))
        {
            postId = await db.ExecuteScalarAsync<long>(
                """
                INSERT INTO Post (Title, Slug, AuthorName, Status)
                VALUES ('E2E Update Target', @slug, 'E2E Tester', 'draft');
                SELECT last_insert_rowid();
                """,
                new { slug });
        }

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Title"] = "E2E Updated Post Title",
            ["Slug"] = slug,
            ["Summary"] = "Updated by the YAML pipeline e2e test",
            ["Content"] = "## e2e updated content",
            ["AuthorName"] = "E2E Tester",
            ["Status"] = "draft",
            ["FeaturedFlag"] = "1",
            ["ViewCount"] = "0",
            ["CreatedAt"] = "2026-06-12 10:00:00",
            ["UpdatedAt"] = "2026-06-12 10:05:00"
        };

        var response = await client.PostAsync(
            $"/blog/DynamicEntity/Edit?entity=post&id={postId}",
            new FormUrlEncodedContent(form));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Edit failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var checkDb = new SqliteConnection(_factory.TenantDbConnectionString);
        var updated = await checkDb.QuerySingleOrDefaultAsync<(string Title, int FeaturedFlag)?>(
            "SELECT Title, FeaturedFlag FROM Post WHERE Id = @postId", new { postId });

        Assert.NotNull(updated);
        Assert.Equal("E2E Updated Post Title", updated.Value.Title);
        Assert.Equal(1, updated.Value.FeaturedFlag);
    }

    [Fact]
    public async Task DeletePost_ThroughHttpLayer_DeletesRowInTenantDatabase()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var slug = $"e2e-delete-{Guid.NewGuid():N}";
        long postId;
        await using (var db = new SqliteConnection(_factory.TenantDbConnectionString))
        {
            postId = await db.ExecuteScalarAsync<long>(
                """
                INSERT INTO Post (Title, Slug, AuthorName, Status)
                VALUES ('E2E Delete Target', @slug, 'E2E Tester', 'draft');
                SELECT last_insert_rowid();
                """,
                new { slug });
        }

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        };

        var response = await client.PostAsync(
            $"/blog/DynamicEntity/Delete?entity=post&id={postId}",
            new FormUrlEncodedContent(form));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Delete failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var checkDb = new SqliteConnection(_factory.TenantDbConnectionString);
        var row = await checkDb.QuerySingleOrDefaultAsync<long?>(
            "SELECT Id FROM Post WHERE Id = @postId", new { postId });

        Assert.Null(row);
    }

    [Fact]
    public async Task ExportCsv_ThroughHttpLayer_ReturnsCsvWithSeededRows()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/blog/DynamicEntity/ExportCsv?entity=post");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csvContent = await response.Content.ReadAsStringAsync();

        Assert.Contains("别名", csvContent);
        Assert.Contains("oss-contribution-guide", csvContent);
    }

    [Fact]
    public async Task BulkDelete_ThroughHttpLayer_DeletesSelectedRows()
    {
        using var client = CreateClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var slug1 = $"e2e-bulk-1-{Guid.NewGuid():N}";
        var slug2 = $"e2e-bulk-2-{Guid.NewGuid():N}";
        long id1, id2;

        await using (var db = new SqliteConnection(_factory.TenantDbConnectionString))
        {
            id1 = await db.ExecuteScalarAsync<long>(
                "INSERT INTO Post (Title, Slug, AuthorName, Status) VALUES ('Bulk Delete 1', @slug1, 'E2E Tester', 'draft'); SELECT last_insert_rowid();",
                new { slug1 });
            id2 = await db.ExecuteScalarAsync<long>(
                "INSERT INTO Post (Title, Slug, AuthorName, Status) VALUES ('Bulk Delete 2', @slug2, 'E2E Tester', 'draft'); SELECT last_insert_rowid();",
                new { slug2 });
        }

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ids[0]"] = id1.ToString(),
            ["ids[1]"] = id2.ToString()
        };

        var response = await client.PostAsync(
            "/blog/DynamicEntity/BulkDelete?entity=post",
            new FormUrlEncodedContent(form));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Bulk delete failed: {(int)response.StatusCode} {response.StatusCode}\n{Truncate(body)}");

        await using var checkDb = new SqliteConnection(_factory.TenantDbConnectionString);
        var row1 = await checkDb.QuerySingleOrDefaultAsync<long?>(
            "SELECT Id FROM Post WHERE Id = @id1", new { id1 });
        var row2 = await checkDb.QuerySingleOrDefaultAsync<long?>(
            "SELECT Id FROM Post WHERE Id = @id2", new { id2 });

        Assert.Null(row1);
        Assert.Null(row2);
    }

    // ── ヘルパー ───────────────────────────────────────────────────────────

    private HttpClient CreateClient() =>
        // CookieContainerHandler: antiforgery cookie を GET → POST 間で保持する
        _factory.CreateDefaultClient(new CookieContainerHandler());

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync(IndexUrl);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var match = AntiForgeryTokenRegex.Match(html);
        Assert.True(match.Success, "一覧ページから antiforgery トークンを取得できませんでした。");
        return match.Groups[1].Value;
    }

    private static string Truncate(string text) =>
        text.Length <= 2000 ? text : text[..2000];
}
