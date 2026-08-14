// 責務: QrTokenGeneratorHook のユニットテスト。
// 正常系・異常系・フィールド欠損の3パターンを最低限テストする。
//
// 修正メモ: 元は --ai-scaffold が生成した汎用プレースホルダ("fieldName"/"valid_value" 等)の
// ままで、実装(QrTokenGeneratorHook)が要求する実際のフィールド(ai_identity_id)と噛み合っておらず、
// 常に Abort が返って全ケース失敗していた。実装の実挙動(ai_identity の存在/有効性チェックと
// トークン自動生成)に合わせてシナリオを作り直した。

using Dapper;
using Microsoft.Data.Sqlite;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Projects.AiCard.Hooks;
using Xunit;

namespace NetYamlForge.Tests.Hooks;

public class QrTokenGeneratorHookTests
{
    // ─── ヘルパー ───────────────────────────────────────────
    private static EntityHookContext MakeCtx(
        Dictionary<string, object?> values,
        CrudOperation op = CrudOperation.Create)
        => new() { Entity = "qr_token", Operation = op, Values = values };

    private static async Task<SqliteConnection> OpenDbWithIdentityAsync(bool isActive)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "CREATE TABLE ai_identity (id INTEGER PRIMARY KEY, ai_id TEXT, is_active INTEGER)");
        await conn.ExecuteAsync(
            "INSERT INTO ai_identity (id, ai_id, is_active) VALUES (1, 'ai-001', @isActive)",
            new { isActive = isActive ? 1 : 0 });
        return conn;
    }

    // ─── 正常系 ─────────────────────────────────────────────

    [Fact]
    public async Task BeforeAsync_ValidInput_ReturnsContinue()
    {
        await using var db = await OpenDbWithIdentityAsync(isActive: true);
        var sut = new QrTokenGeneratorHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QrTokenGeneratorHook>.Instance);
        var ctx = MakeCtx(new() { ["ai_identity_id"] = 1 });

        var result = await sut.BeforeAsync(ctx, db, null);

        Assert.False(result.Cancel);
        // トークンと QR URL が自動生成される
        Assert.True(ctx.Values.ContainsKey("token"));
        Assert.Equal($"/ai-card/ahp/hs/{ctx.Values["token"]}", ctx.Values["qr_url"]);
        // 既定値が補完される
        Assert.Equal(0, ctx.Values["scan_count"]);
        Assert.Equal(1, ctx.Values["is_active"]);
    }

    // ─── 異常系 ─────────────────────────────────────────────

    [Fact]
    public async Task BeforeAsync_AiIdentityNotFound_ReturnsAbort()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "CREATE TABLE ai_identity (id INTEGER PRIMARY KEY, ai_id TEXT, is_active INTEGER)");
        var sut = new QrTokenGeneratorHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QrTokenGeneratorHook>.Instance);
        var ctx = MakeCtx(new() { ["ai_identity_id"] = 999 });

        var result = await sut.BeforeAsync(ctx, conn, null);

        Assert.True(result.Cancel);
        Assert.Contains("存在しません", result.CancelMessage);
    }

    [Fact]
    public async Task BeforeAsync_AiIdentityInactive_ReturnsAbort()
    {
        await using var db = await OpenDbWithIdentityAsync(isActive: false);
        var sut = new QrTokenGeneratorHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QrTokenGeneratorHook>.Instance);
        var ctx = MakeCtx(new() { ["ai_identity_id"] = 1 });

        var result = await sut.BeforeAsync(ctx, db, null);

        Assert.True(result.Cancel);
        Assert.Contains("無効化", result.CancelMessage);
    }

    // ─── フィールド欠損（必須チェック確認）──────────────────

    [Fact]
    public async Task BeforeAsync_MissingAiIdentityId_ReturnsAbort()
    {
        var sut = new QrTokenGeneratorHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QrTokenGeneratorHook>.Instance);
        var ctx = MakeCtx(new()); // ai_identity_id なし

        // db への到達前に必須チェックで弾かれるため、db は null のままでよい
        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.True(result.Cancel);
        Assert.Contains("ai_identity_id は必須", result.CancelMessage);
    }

    // ─── Create 以外の操作はスキップされる ────────────────────

    [Fact]
    public async Task BeforeAsync_NonCreateOperation_SkipsValidationAndReturnsContinue()
    {
        var sut = new QrTokenGeneratorHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QrTokenGeneratorHook>.Instance);
        var ctx = MakeCtx(new(), op: CrudOperation.Update);

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    // ─── Hook 名の確認 ──────────────────────────────────────

    [Fact]
    public void Name_IsCorrectYamlKey()
    {
        var sut = new QrTokenGeneratorHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QrTokenGeneratorHook>.Instance);
        Assert.Equal("qr_token_generator", sut.Name);
    }
}
