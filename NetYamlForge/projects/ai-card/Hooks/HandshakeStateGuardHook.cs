// 責務: Handshake セッションの状態遷移バリデーション。
// 不正な状態遷移を防止し、updated_at を自動更新する。

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;
using Dapper;

namespace NetYamlForge.Projects.AiCard.Hooks;

public sealed class HandshakeStateGuardHook : IEntityHook
{
    private readonly ILogger<HandshakeStateGuardHook> _logger;

    public string Name => "handshake_state_guard";

    // 状態遷移マトリクス: 各状態から遷移可能な状態のセット
    private static readonly Dictionary<string, HashSet<string>> ValidTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pending"]   = new(StringComparer.OrdinalIgnoreCase) { "connected", "expired", "rejected", "revoked" },
        ["connected"] = new(StringComparer.OrdinalIgnoreCase) { "completed", "expired", "rejected", "revoked" },
        ["completed"] = new(StringComparer.OrdinalIgnoreCase) { "revoked" },
        ["expired"]   = new(StringComparer.OrdinalIgnoreCase) { },
        ["rejected"]  = new(StringComparer.OrdinalIgnoreCase) { },
        ["revoked"]   = new(StringComparer.OrdinalIgnoreCase) { },
    };

    public HandshakeStateGuardHook(ILogger<HandshakeStateGuardHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // Create 時: session_token の自動生成と有効期限の設定
        if (ctx.Operation == CrudOperation.Create)
        {
            if (!ctx.Values.ContainsKey("session_token") ||
                string.IsNullOrWhiteSpace(ctx.Values["session_token"]?.ToString()))
            {
                ctx.Values["session_token"] = Guid.NewGuid().ToString("N");
            }

            if (!ctx.Values.ContainsKey("state") ||
                string.IsNullOrWhiteSpace(ctx.Values["state"]?.ToString()))
            {
                ctx.Values["state"] = "pending";
            }

            // デフォルトで24時間後に期限切れ
            if (!ctx.Values.ContainsKey("expires_at") ||
                string.IsNullOrWhiteSpace(ctx.Values["expires_at"]?.ToString()))
            {
                ctx.Values["expires_at"] = DateTime.UtcNow.AddHours(24).ToString("yyyy-MM-dd HH:mm:ss");
            }

            ctx.Values["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            ctx.Values["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            _logger.LogInformation("[AHP] New handshake session created: {Token}",
                ctx.Values["session_token"]);
            return HookResult.Continue();
        }

        // Update 時: 状態遷移のバリデーション
        if (ctx.Operation == CrudOperation.Update)
        {
            ctx.Values["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            if (ctx.Values.TryGetValue("state", out var newStateObj) &&
                newStateObj is string newState &&
                !string.IsNullOrWhiteSpace(newState) &&
                ctx.Id != null)
            {
                var currentState = await db.ExecuteScalarAsync<string>(
                    "SELECT state FROM handshake_session WHERE id = @id",
                    new { id = ctx.Id }, tx);

                if (currentState != null &&
                    !string.Equals(currentState, newState, StringComparison.OrdinalIgnoreCase))
                {
                    if (!ValidTransitions.TryGetValue(currentState, out var allowed) ||
                        !allowed.Contains(newState))
                    {
                        _logger.LogWarning(
                            "[AHP] Invalid state transition: {Current} -> {New} for session {Id}",
                            currentState, newState, ctx.Id);
                        return HookResult.Abort(
                            $"状態遷移エラー: '{currentState}' → '{newState}' は許可されていません。");
                    }

                    _logger.LogInformation(
                        "[AHP] State transition: {Current} -> {New} for session {Id}",
                        currentState, newState, ctx.Id);
                }
            }

            return HookResult.Continue();
        }

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