// 責務: チャットメッセージ作成時の前処理。
// セッションの存在確認、状態検証、タイムスタンプ自動付与。

using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;
using Dapper;

namespace NetYamlForge.Projects.AiCard.Hooks;

public sealed class AiChatHandlerHook : IEntityHook
{
    private readonly ILogger<AiChatHandlerHook> _logger;

    public string Name => "ai_chat_handler";

    public AiChatHandlerHook(ILogger<AiChatHandlerHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create)
            return HookResult.Continue();

        // セッション存在チェック
        if (!ctx.Values.TryGetValue("session_id", out var sessionIdObj) ||
            sessionIdObj == null)
        {
            return HookResult.Abort("session_id は必須です。");
        }

        var sessionId = Convert.ToInt32(sessionIdObj);
        var session = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT id, state FROM handshake_session WHERE id = @id",
            new { id = sessionId }, tx);

        if (session == null)
        {
            return HookResult.Abort($"セッション (id={sessionId}) が存在しません。");
        }

        string state = session.state;
        if (state != "pending" && state != "connected")
        {
            return HookResult.Abort(
                $"セッション (id={sessionId}) は '{state}' 状態のため、メッセージを追加できません。");
        }

        // セッションが pending の場合、connected に遷移
        if (string.Equals(state, "pending", StringComparison.OrdinalIgnoreCase))
        {
            await db.ExecuteAsync(
                "UPDATE handshake_session SET state = 'connected', updated_at = @now WHERE id = @id",
                new { id = sessionId, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }, tx);
            _logger.LogInformation("[AHP] Session {Id} transitioned to 'connected' on first message", sessionId);
        }

        // role のバリデーション
        if (ctx.Values.TryGetValue("role", out var roleObj))
        {
            var role = roleObj?.ToString() ?? "";
            if (role != "ai" && role != "human" && role != "system")
            {
                ctx.Values["role"] = "human";
            }
        }
        else
        {
            ctx.Values["role"] = "human";
        }

        // タイムスタンプ
        ctx.Values["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        _logger.LogInformation("[AHP] Chat message added to session {Id} (role={Role})",
            sessionId, ctx.Values["role"]);

        return HookResult.Continue();
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}