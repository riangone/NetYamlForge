// 責務: QR トークン作成時にトークンと URL を自動生成し、
// AI Identity の存在確認を行う。

using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;
using Dapper;

namespace NetYamlForge.Projects.AiCard.Hooks;

public sealed class QrTokenGeneratorHook : IEntityHook
{
    private readonly ILogger<QrTokenGeneratorHook> _logger;

    public string Name => "qr_token_generator";

    public QrTokenGeneratorHook(ILogger<QrTokenGeneratorHook> logger)
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

        // AI Identity 存在チェック
        if (!ctx.Values.TryGetValue("ai_identity_id", out var aiIdObj) ||
            aiIdObj == null)
        {
            return HookResult.Abort("ai_identity_id は必須です。");
        }

        var aiIdentityId = Convert.ToInt32(aiIdObj);
        var identity = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT id, ai_id, is_active FROM ai_identity WHERE id = @id",
            new { id = aiIdentityId }, tx);

        if (identity == null)
        {
            return HookResult.Abort($"AI Identity (id={aiIdentityId}) が存在しません。");
        }

        if (identity.is_active != 1)
        {
            return HookResult.Abort($"AI Identity (id={aiIdentityId}) は無効化されています。");
        }

        // トークン自動生成
        var token = Guid.NewGuid().ToString("N")[..16];
        ctx.Values["token"] = token;

        // QR URL 自動生成（相対パス — 実行時にホスト名が付与される）
        ctx.Values["qr_url"] = $"/ai-card/ahp/hs/{token}";

        // デフォルト値設定
        if (!ctx.Values.ContainsKey("scan_count"))
            ctx.Values["scan_count"] = 0;
        if (!ctx.Values.ContainsKey("is_active"))
            ctx.Values["is_active"] = 1;

        ctx.Values["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        _logger.LogInformation(
            "[AHP] QR token generated: {Token} for AI Identity {AiId}",
            token, (string)identity.ai_id);

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