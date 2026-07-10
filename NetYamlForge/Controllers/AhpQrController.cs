// ファイル概要: AHP QR トークンの発行・自動更新 API。
// 「相手に見せる QR」画面を常に 1 枚の有効な QR で満たすためのエンドポイント群。
//
//   POST /{project}/api/qr/ensure    → 現在有効な QR を返す。無ければ自動発行（自己修復）
//   POST /{project}/api/qr/rotate    → 既存の有効 QR を退役させ、新しい QR を発行
//   POST /{project}/api/qr/generate  → 用途を指定して QR を発行（QrGenerator ページ用）
//
// 設計方針: ユーザーに「スキャン上限」を意識させない。
//   通常利用では max_scans は未設定（無制限）。上限・期限・無効化に達した QR は
//   ensure/rotate が自動で新しい QR に差し替えるため、画面には常に有効な QR が出る。

using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services;

namespace NetYamlForge.Controllers;

[Route("{project}/api/qr")]
public class AhpQrController(
    ProjectScope scope,
    IDbConnection db,
    ILogger<AhpQrController> logger) : BaseProjectController
{
    private string ProjectName => scope.IsSet ? scope.Current.Name : "ai-card";
    private static string NowUtc() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

    public record QrGenRequest(long? ai_identity_id, string? intent_type, string? intent_topic);

    // ── 有効な QR を保証（無ければ自動発行） ───────────────
    // POST /{project}/api/qr/ensure
    [HttpPost("ensure")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Ensure([FromBody] QrGenRequest? req)
    {
        var qr = await GetUsableQrAsync(req?.ai_identity_id)
                 ?? await CreateQrAsync(req?.ai_identity_id, req?.intent_type, req?.intent_topic);

        if (qr == null)
            return Json(new { ok = false, error = "有効な AI ID がありません。先に AI ID を作成してください。" });

        return Json(QrPayload(qr, created: false));
    }

    // ── 強制的に新しい QR に差し替え ───────────────────────
    // POST /{project}/api/qr/rotate
    [HttpPost("rotate")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Rotate([FromBody] QrGenRequest? req)
    {
        var aiIdentityId = await ResolveIdentityIdAsync(req?.ai_identity_id);
        if (aiIdentityId == null)
            return Json(new { ok = false, error = "有効な AI ID がありません。先に AI ID を作成してください。" });

        // 同じ AI ID の有効 QR を退役させてから新規発行
        await db.ExecuteAsync(
            "UPDATE qr_token SET is_active = 0 WHERE ai_identity_id = @id AND is_active = 1",
            new { id = aiIdentityId.Value });

        var qr = await CreateQrAsync(aiIdentityId.Value, req?.intent_type, req?.intent_topic);
        if (qr == null)
            return Json(new { ok = false, error = "QR の発行に失敗しました。" });

        return Json(QrPayload(qr, created: true));
    }

    // ── 用途を指定して発行（QrGenerator ページのフォーム用） ─
    // POST /{project}/api/qr/generate
    [HttpPost("generate")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Generate([FromForm] QrGenRequest req)
    {
        var qr = await CreateQrAsync(req.ai_identity_id, req.intent_type, req.intent_topic);
        if (qr == null)
        {
            TempData["QrError"] = "有効な AI ID を選択してください。";
            return Redirect($"/{ProjectName}/Page/QrGenerator");
        }

        // 発行後は「QR を見せる」画面へ戻し、そのまま相手に提示できるようにする
        return Redirect($"/{ProjectName}/Page/StarterOverview");
    }

    // ── ヘルパー ─────────────────────────────────────────

    // 現在使える QR（有効・未期限切れ・上限未達）を 1 枚返す
    private async Task<dynamic?> GetUsableQrAsync(long? aiIdentityId)
    {
        var now = NowUtc();
        return await db.QueryFirstOrDefaultAsync<dynamic>($"""
            SELECT qt.*, ai.display_name AS ai_name
            FROM qr_token qt
            LEFT JOIN ai_identity ai ON qt.ai_identity_id = ai.id
            WHERE qt.is_active = 1
              AND (@aiId IS NULL OR qt.ai_identity_id = @aiId)
              AND (qt.max_scans IS NULL OR qt.max_scans <= 0 OR qt.scan_count < qt.max_scans)
              AND (qt.expires_at IS NULL OR qt.expires_at = '' OR qt.expires_at > @now)
            ORDER BY qt.created_at DESC
            LIMIT 1
            """, new { aiId = aiIdentityId, now });
    }

    // 新しい QR を作成して返す（max_scans は既定で無制限）
    private async Task<dynamic?> CreateQrAsync(long? aiIdentityId, string? intentType, string? intentTopic)
    {
        var resolved = await ResolveIdentityIdAsync(aiIdentityId);
        if (resolved == null) return null;

        var token = Guid.NewGuid().ToString("N")[..16];
        var qrUrl = $"/{ProjectName}/ahp/hs/{token}";
        var now = NowUtc();

        await db.ExecuteAsync("""
            INSERT INTO qr_token
                (ai_identity_id, token, intent_type, intent_topic, qr_url,
                 scan_count, max_scans, is_active, created_at)
            VALUES
                (@aiId, @token, @intentType, @intentTopic, @qrUrl,
                 0, NULL, 1, @now)
            """,
            new
            {
                aiId = resolved.Value,
                token,
                intentType = string.IsNullOrWhiteSpace(intentType) ? null : intentType,
                intentTopic = string.IsNullOrWhiteSpace(intentTopic) ? null : intentTopic,
                qrUrl,
                now
            });

        logger.LogInformation("[AHP] QR auto-issued: {Token} for AI Identity {AiId}", token, resolved.Value);

        return await db.QueryFirstOrDefaultAsync<dynamic>("""
            SELECT qt.*, ai.display_name AS ai_name
            FROM qr_token qt
            LEFT JOIN ai_identity ai ON qt.ai_identity_id = ai.id
            WHERE qt.token = @token
            """, new { token });
    }

    // AI ID を決定: 指定があればそれ、無ければ直近で有効な AI ID
    private async Task<long?> ResolveIdentityIdAsync(long? aiIdentityId)
    {
        if (aiIdentityId is > 0)
        {
            var exists = await db.ExecuteScalarAsync<long?>(
                "SELECT id FROM ai_identity WHERE id = @id AND is_active = 1",
                new { id = aiIdentityId.Value });
            if (exists != null) return exists;
        }

        return await db.ExecuteScalarAsync<long?>(
            "SELECT id FROM ai_identity WHERE is_active = 1 ORDER BY updated_at DESC LIMIT 1");
    }

    private object QrPayload(dynamic qr, bool created) => new
    {
        ok = true,
        created,
        token = (string)qr.token,
        qr_url = (string)qr.qr_url,
        ai_name = (string?)qr.ai_name ?? "AI ID",
        intent_type = (string?)qr.intent_type,
        intent_topic = (string?)qr.intent_topic,
        scan_count = qr.scan_count == null ? 0L : (long)qr.scan_count,
        created_at = (string?)qr.created_at
    };
}
