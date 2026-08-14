// 責務: AiChatHandlerHook のユニットテスト。
// 正常系・異常系・フィールド欠損の3パターンを最低限テストする。
//
// 修正メモ: 元は --ai-scaffold が生成した汎用プレースホルダ("fieldName"/"valid_value" 等)の
// ままで、実装(AiChatHandlerHook)が要求する実際のフィールド(session_id)と噛み合っておらず、
// 常に Abort が返って全ケース失敗していた。実装の実挙動(handshake_session の存在/状態チェック)
// に合わせてシナリオを作り直した。

using System;
using Dapper;
using Microsoft.Data.Sqlite;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Projects.AiCard.Hooks;
using Xunit;

namespace NetYamlForge.Tests.Hooks;

public class AiChatHandlerHookTests
{
    // ─── ヘルパー ───────────────────────────────────────────
    private static EntityHookContext MakeCtx(
        Dictionary<string, object?> values,
        CrudOperation op = CrudOperation.Create)
        => new() { Entity = "chat_message", Operation = op, Values = values };

    private static async Task<SqliteConnection> OpenDbWithSessionAsync(string state)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "CREATE TABLE handshake_session (id INTEGER PRIMARY KEY, state TEXT, updated_at TEXT)");
        await conn.ExecuteAsync(
            "INSERT INTO handshake_session (id, state, updated_at) VALUES (1, @state, '2026-01-01 00:00:00')",
            new { state });
        return conn;
    }

    // ─── 正常系 ─────────────────────────────────────────────

    [Fact]
    public async Task BeforeAsync_ValidInput_ReturnsContinue()
    {
        await using var db = await OpenDbWithSessionAsync("pending");
        var sut = new AiChatHandlerHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiChatHandlerHook>.Instance);
        var ctx = MakeCtx(new() { ["session_id"] = 1 });

        var result = await sut.BeforeAsync(ctx, db, null);

        Assert.False(result.Cancel);
        // pending だったセッションは初回メッセージで connected に遷移する
        var newState = await db.QueryFirstAsync<string>(
            "SELECT state FROM handshake_session WHERE id = 1");
        Assert.Equal("connected", newState);
        // role 未指定時は human が既定値として補完される
        Assert.Equal("human", ctx.Values["role"]);
        Assert.True(ctx.Values.ContainsKey("created_at"));
    }

    // ─── 異常系 ─────────────────────────────────────────────

    [Fact]
    public async Task BeforeAsync_SessionNotFound_ReturnsAbort()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "CREATE TABLE handshake_session (id INTEGER PRIMARY KEY, state TEXT, updated_at TEXT)");
        var sut = new AiChatHandlerHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiChatHandlerHook>.Instance);
        var ctx = MakeCtx(new() { ["session_id"] = 999 });

        var result = await sut.BeforeAsync(ctx, conn, null);

        Assert.True(result.Cancel);
        Assert.Contains("存在しません", result.CancelMessage);
    }

    [Fact]
    public async Task BeforeAsync_SessionClosed_ReturnsAbort()
    {
        await using var db = await OpenDbWithSessionAsync("closed");
        var sut = new AiChatHandlerHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiChatHandlerHook>.Instance);
        var ctx = MakeCtx(new() { ["session_id"] = 1 });

        var result = await sut.BeforeAsync(ctx, db, null);

        Assert.True(result.Cancel);
        Assert.Contains("closed", result.CancelMessage);
    }

    // ─── フィールド欠損（必須チェック確認）──────────────────

    [Fact]
    public async Task BeforeAsync_MissingSessionId_ReturnsAbort()
    {
        var sut = new AiChatHandlerHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiChatHandlerHook>.Instance);
        var ctx = MakeCtx(new()); // session_id なし

        // db への到達前に必須チェックで弾かれるため、db は null のままでよい
        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.True(result.Cancel);
        Assert.Contains("session_id は必須", result.CancelMessage);
    }

    // ─── Create 以外の操作はスキップされる ────────────────────

    [Fact]
    public async Task BeforeAsync_NonCreateOperation_SkipsValidationAndReturnsContinue()
    {
        var sut = new AiChatHandlerHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiChatHandlerHook>.Instance);
        // Update 時は session_id が無くても早期 Continue で db に触れない
        var ctx = MakeCtx(new(), op: CrudOperation.Update);

        var result = await sut.BeforeAsync(ctx, null!, null);

        Assert.False(result.Cancel);
    }

    // ─── Hook 名の確認 ──────────────────────────────────────

    [Fact]
    public void Name_IsCorrectYamlKey()
    {
        var sut = new AiChatHandlerHook(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiChatHandlerHook>.Instance);
        Assert.Equal("ai_chat_handler", sut.Name);
    }
}
