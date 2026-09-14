// dungeon-forge の探索/戦闘アクション API。DungeonMap.yaml が案内する
// POST /api/dungeon-forge/dungeon/{move,attack,use-item,pick-up} をここで実装する。
// ルーティング規約は ApiEntityController（api/{project}/{entity}）に倣い、
// {project} ルートパラメータ経由で ProjectMiddleware / ProjectScope / IDbConnection が
// 自動的に dungeon-forge プロジェクトへスコープされる。
//
// ゲームルール（ダメージ計算・経験値・在庫更新など）は一切ここに書かず、
// Services/DungeonCombatService.cs の IDungeonCombatService に委譲する
// （設計文書の「多エンティティにまたがる編成は Hook ではなく普通の Service に置く」方針に従う）。

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Controllers;
using NetYamlForge.Projects.DungeonForge.Services;
using NetYamlForge.Services;
using NetYamlForge.Services.Api;

namespace NetYamlForge.Projects.DungeonForge.Controllers;

[Authorize(AuthenticationSchemes = $"{CookieAuthenticationDefaults.AuthenticationScheme},ApiToken")]
[ApiController]
[Produces("application/json")]
[Route("api/{project}/dungeon")]
public class DungeonForgeController : BaseProjectController
{
    private readonly IDungeonCombatService _combat;
    private readonly IEntityMetadataProvider _meta;
    private readonly ApiEntityAccessGuard _accessGuard;

    public DungeonForgeController(
        IDungeonCombatService combat,
        IEntityMetadataProvider meta,
        ApiEntityAccessGuard accessGuard)
    {
        _combat = combat;
        _meta = meta;
        _accessGuard = accessGuard;
    }

    public record MoveRequest(string? Direction);
    public record UseItemRequest(int ItemId);

    /// <summary>
    /// 地下城の行動は characters 実体への書き込みと権限的に同じ意味を持つため、
    /// ApiEntityController と同じ apiWriteRoles / Admin 直通ロジックをそのまま再利用する
    /// （project.yaml の apiWriteRoles: [player, game_master] を再実装せず共有する）。
    /// </summary>
    private IActionResult? CheckWriteAccess()
    {
        var meta = _meta.Get("characters");
        return _accessGuard.ValidateApiAccess(meta, writeRequired: true);
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) ? id : null;
    }

    private IActionResult ToActionResult(DungeonActionResult result)
    {
        if (!result.Success)
            return StatusCode(result.StatusCode, new { error = result.Error });
        return Ok(result.Data);
    }

    // ─── GET /api/{project}/dungeon/state ──────────────────────────────
    // views/Dungeon/Explore.cshtml がページ読み込み時と各アクション後に呼ぶ、
    // 現在の部屋・出口・怪物HP・インベントリの読み取り専用スナップショット。
    [HttpGet("state")]
    public async Task<IActionResult> State(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _combat.GetStateAsync(userId.Value, ct);
        return ToActionResult(result);
    }

    // ─── POST /api/{project}/dungeon/move ─────────────────────────────
    [HttpPost("move")]
    public async Task<IActionResult> Move([FromBody] MoveRequest? body, CancellationToken ct)
    {
        var denied = CheckWriteAccess();
        if (denied != null) return denied;

        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _combat.MoveAsync(userId.Value, body?.Direction ?? "", ct);
        return ToActionResult(result);
    }

    // ─── POST /api/{project}/dungeon/attack ───────────────────────────
    [HttpPost("attack")]
    public async Task<IActionResult> Attack(CancellationToken ct)
    {
        var denied = CheckWriteAccess();
        if (denied != null) return denied;

        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _combat.AttackAsync(userId.Value, ct);
        return ToActionResult(result);
    }

    // ─── POST /api/{project}/dungeon/use-item ─────────────────────────
    [HttpPost("use-item")]
    public async Task<IActionResult> UseItem([FromBody] UseItemRequest? body, CancellationToken ct)
    {
        var denied = CheckWriteAccess();
        if (denied != null) return denied;

        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _combat.UseItemAsync(userId.Value, body?.ItemId ?? 0, ct);
        return ToActionResult(result);
    }

    // ─── POST /api/{project}/dungeon/pick-up ──────────────────────────
    [HttpPost("pick-up")]
    public async Task<IActionResult> PickUp(CancellationToken ct)
    {
        var denied = CheckWriteAccess();
        if (denied != null) return denied;

        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _combat.PickUpAsync(userId.Value, ct);
        return ToActionResult(result);
    }
}
