// dungeon-forge：核心玩法引擎（方案 A —— 规则由确定性 C# 计算，AI 只负责把结算结果包装成叙事文本）。
// 对应设计文档 docs/projects/dungeon-forge/DUNGEON-FORGE-DESIGN.md 第 5/7 章遗留的缺口：
// IDungeonCombatService 在此实现，供 Controllers/DungeonForgeController.cs 调用，
// 打通 move / attack / use-item / pick-up 四个此前只存在于页面文案里、从未落地的动作。
//
// 已知设计取舍（与设计文档"开放问题 2"一致，未做多人隔离的完整方案）：
// monsters 表是共享模板（同一只怪物可能出现在多个房间，如种子数据里的骷髅兵同时出现在房间2和房间4），
// 因此战斗中不直接扣减 monsters.Hp，而是从"该角色 + 该怪物"最近一条 battle_logs 记录里恢复
// 进行中的血量，从而做到按角色隔离遭遇战，且不破坏 monsters 实体的 api: readonly 语义。

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Projects.DungeonForge.Services;

/// <summary>戦闘/移動/道具/拾得アクションの結果を表す統一エンベロープ。</summary>
public class DungeonActionResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Error { get; init; }
    public object? Data { get; init; }

    public static DungeonActionResult Ok(object data) => new() { Success = true, StatusCode = 200, Data = data };
    public static DungeonActionResult Fail(int statusCode, string error) => new() { Success = false, StatusCode = statusCode, Error = error };
}

public interface IDungeonCombatService
{
    Task<DungeonActionResult> MoveAsync(int ownerUserId, string direction, CancellationToken ct = default);
    Task<DungeonActionResult> AttackAsync(int ownerUserId, CancellationToken ct = default);
    Task<DungeonActionResult> UseItemAsync(int ownerUserId, int itemId, CancellationToken ct = default);
    Task<DungeonActionResult> PickUpAsync(int ownerUserId, CancellationToken ct = default);

    /// <summary>
    /// UI（views/Dungeon/Explore.cshtml）が move/attack を呼ぶ前に現在の状況（部屋・出口・怪物の生存HP・
    /// インベントリ）を描画するための読み取り専用スナップショット。ゲームルールの変更は行わない。
    /// </summary>
    Task<DungeonActionResult> GetStateAsync(int ownerUserId, CancellationToken ct = default);
}

public class DungeonCombatService : IDungeonCombatService
{
    private readonly IDbConnection _db;
    private readonly ICliChainService _cliChain;
    private readonly ILogger<DungeonCombatService> _logger;

    public DungeonCombatService(IDbConnection db, ICliChainService cliChain, ILogger<DungeonCombatService> logger)
    {
        _db = db;
        _cliChain = cliChain;
        _logger = logger;
    }

    // ─── DTO（Dapper 用の行マッピング） ──────────────────────────────

    private class CharacterRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Race { get; set; } = "";
        public string Class { get; set; } = "";
        public int Level { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        public int Gold { get; set; }
        public int Experience { get; set; }
        public int CurrentRoomId { get; set; }
        public string? InventoryJson { get; set; }
        public string Status { get; set; } = "alive";
        public int? OwnerUserId { get; set; }
    }

    private class RoomRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ExitsJson { get; set; } = "{}";
        public int? MonsterId { get; set; }
        public int? ItemId { get; set; }
        public int? RequiredKeyItemId { get; set; }
        public bool IsBossRoom { get; set; }
        public bool IsCleared { get; set; }
    }

    private class MonsterRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int Hp { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int? LootItemId { get; set; }
        public string? FlavorText { get; set; }
    }

    private class ItemRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string? EffectJson { get; set; }
        public int Value { get; set; }
    }

    private class InventoryEntry
    {
        public int ItemId { get; set; }
        public int Quantity { get; set; }
    }

    // ─── 移動 ────────────────────────────────────────────────────────

    public async Task<DungeonActionResult> MoveAsync(int ownerUserId, string direction, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(direction))
            return DungeonActionResult.Fail(400, "请指定移动方向");

        var character = await ResolveCharacterAsync(ownerUserId);
        if (character == null)
            return DungeonActionResult.Fail(404, "未找到你的冒险者角色，请先创建角色");
        if (character.Status != "alive")
            return DungeonActionResult.Fail(400, StatusBlockedMessage(character.Status, "继续探索"));

        var room = await _db.QueryFirstOrDefaultAsync<RoomRow>(
            "SELECT * FROM rooms WHERE Id=@Id", new { Id = character.CurrentRoomId });
        if (room == null)
            return DungeonActionResult.Fail(500, "当前房间数据丢失");

        var exits = ParseExits(room.ExitsJson);
        var dir = direction.Trim();
        if (!exits.TryGetValue(dir, out var targetId) || targetId == null)
            return DungeonActionResult.Fail(400, $"'{direction}' 方向没有出口");

        var target = await _db.QueryFirstOrDefaultAsync<RoomRow>(
            "SELECT * FROM rooms WHERE Id=@Id", new { Id = targetId.Value });
        if (target == null)
            return DungeonActionResult.Fail(500, "目标房间数据丢失");

        if (target.RequiredKeyItemId.HasValue)
        {
            var inv = ParseInventory(character.InventoryJson);
            var hasKey = inv.Any(i => i.ItemId == target.RequiredKeyItemId.Value && i.Quantity > 0);
            if (!hasKey)
            {
                var keyItem = await _db.QueryFirstOrDefaultAsync<ItemRow>(
                    "SELECT * FROM items WHERE Id=@Id", new { Id = target.RequiredKeyItemId.Value });
                return DungeonActionResult.Fail(403, $"这扇门似乎被锁住了，你需要「{keyItem?.Name ?? "特殊钥匙"}」才能进入");
            }
        }

        await _db.ExecuteAsync(
            "UPDATE characters SET CurrentRoomId=@RoomId WHERE Id=@Id",
            new { RoomId = target.Id, Id = character.Id });

        var monsterHint = "";
        if (target.MonsterId.HasValue && !target.IsCleared)
        {
            var monster = await _db.QueryFirstOrDefaultAsync<MonsterRow>(
                "SELECT * FROM monsters WHERE Id=@Id", new { Id = target.MonsterId.Value });
            if (monster != null) monsterHint = $"房间里潜伏着{monster.Name}！";
        }

        var fallback = $"你走向{direction}，来到了「{target.Name}」。{target.Description}{monsterHint}";
        var prompt = $$"""
你是一位哥特奇幻风格的地下城主(DM)，正在为一位名为「{{character.Name}}」的{{character.Race}}{{character.Class}}讲述探索见闻。
请把下面这段结构化房间信息改写为一段沉浸式的中文叙事文字（2到4句话，保持哥特奇幻氛围，不要使用markdown、不要出现列表符号）：
房间名称：{{target.Name}}
房间描述：{{target.Description}}
{{(string.IsNullOrEmpty(monsterHint) ? "" : "警示：" + monsterHint)}}
""";
        var narrative = await GenerateNarrativeAsync(prompt, fallback, ct);

        return DungeonActionResult.Ok(new
        {
            room = target,
            narrative
        });
    }

    // ─── 戦闘 ────────────────────────────────────────────────────────

    public async Task<DungeonActionResult> AttackAsync(int ownerUserId, CancellationToken ct = default)
    {
        var character = await ResolveCharacterAsync(ownerUserId);
        if (character == null)
            return DungeonActionResult.Fail(404, "未找到你的冒险者角色，请先创建角色");
        if (character.Status != "alive")
            return DungeonActionResult.Fail(400, StatusBlockedMessage(character.Status, "战斗"));

        var room = await _db.QueryFirstOrDefaultAsync<RoomRow>(
            "SELECT * FROM rooms WHERE Id=@Id", new { Id = character.CurrentRoomId });
        if (room == null || !room.MonsterId.HasValue || room.IsCleared)
            return DungeonActionResult.Fail(400, "这个房间里没有可以战斗的怪物");

        var monster = await _db.QueryFirstOrDefaultAsync<MonsterRow>(
            "SELECT * FROM monsters WHERE Id=@Id", new { Id = room.MonsterId.Value });
        if (monster == null)
            return DungeonActionResult.Fail(500, "怪物数据丢失");

        var (charAtk, charDef) = GetCombatStats(character.Class, character.Level);

        var lastMonsterHp = await _db.QueryFirstOrDefaultAsync<int?>(
            "SELECT MonsterHpAfter FROM battle_logs WHERE CharacterId=@Cid AND MonsterId=@Mid ORDER BY Id DESC LIMIT 1",
            new { Cid = character.Id, Mid = monster.Id });
        var monsterHpBefore = (lastMonsterHp.HasValue && lastMonsterHp.Value > 0) ? lastMonsterHp.Value : monster.Hp;

        var roundNumber = (await _db.QueryFirstOrDefaultAsync<int?>(
            "SELECT MAX(RoundNumber) FROM battle_logs WHERE CharacterId=@Cid AND MonsterId=@Mid",
            new { Cid = character.Id, Mid = monster.Id })) ?? 0;
        roundNumber += 1;

        var playerDamage = RollDamage(charAtk, monster.Defense);
        var monsterHpAfter = Math.Max(0, monsterHpBefore - playerDamage);

        if (monsterHpAfter <= 0)
            return await ResolveVictoryAsync(character, room, monster, roundNumber, playerDamage, ct);

        return await ResolveCounterAttackAsync(character, monster, roundNumber, playerDamage, monsterHpAfter, ct);
    }

    private async Task<DungeonActionResult> ResolveVictoryAsync(
        CharacterRow character, RoomRow room, MonsterRow monster, int roundNumber, int playerDamage, CancellationToken ct)
    {
        var expGain = monster.Level * 20;
        var goldGain = monster.Level * 10;
        var (newLevel, newExp, newMaxHp, newMaxMp, leveledUp) =
            ApplyExperience(character.Level, character.Experience + expGain, character.MaxHp, character.MaxMp);
        var newHp = leveledUp ? newMaxHp : character.Hp;
        var newMp = leveledUp ? newMaxMp : character.Mp;

        // 通关判定：Boss 房间里的怪物被击败 = 通关。此后角色状态转为 victorious，
        // 与 Status != "alive" 的通用门禁一起，自然地阻止胜利后继续 move/attack/use-item/pick-up。
        var gameWon = room.IsBossRoom;
        var newStatus = gameWon ? "victorious" : "alive";

        await _db.ExecuteAsync(
            "UPDATE characters SET Level=@Level, Experience=@Experience, Gold=Gold+@Gold, MaxHp=@MaxHp, MaxMp=@MaxMp, Hp=@Hp, Mp=@Mp, Status=@Status WHERE Id=@Id",
            new { Level = newLevel, Experience = newExp, Gold = goldGain, MaxHp = newMaxHp, MaxMp = newMaxMp, Hp = newHp, Mp = newMp, Status = newStatus, Id = character.Id });

        await _db.ExecuteAsync(
            "UPDATE rooms SET IsCleared=1, MonsterId=NULL, ItemId=COALESCE(ItemId, @LootItemId) WHERE Id=@Id",
            new { LootItemId = monster.LootItemId, Id = room.Id });

        var fallback = gameWon
            ? $"经过殊死搏斗，你终于击败了{monster.Name}！地下城的诅咒随之消散，你获得了 {expGain} 点经验值和 {goldGain} 枚金币——这座地下城，你已经征服了。"
            : $"经过一番激战，你击败了{monster.Name}！获得了 {expGain} 点经验值和 {goldGain} 枚金币。" +
              (leveledUp ? $"你的等级提升到了 {newLevel} 级！" : "");
        var prompt = $$"""
你是一位哥特奇幻风格的地下城主(DM)。请把下面的战斗结算结果改写成一段{{(gameWon ? "史诗级、荡气回肠的通关结局" : "热血、有画面感的战斗结局")}}中文叙述（2到4句话，不要markdown）：
角色：{{character.Name}}（{{character.Race}} {{character.Class}}，Lv.{{character.Level}}）
对手：{{monster.Name}} —— {{monster.FlavorText}}
本回合造成伤害：{{playerDamage}}，怪物被击败。
获得经验：{{expGain}}，获得金币：{{goldGain}}{{(leveledUp ? $"，角色升级到 {newLevel} 级" : "")}}
{{(gameWon ? "这是地下城最深处的 Boss，击败它意味着整座地下城被彻底征服，请写出通关的荣耀感。" : "")}}
""";
        var narrative = await GenerateNarrativeAsync(prompt, fallback, ct);

        await _db.ExecuteAsync(
            """
            INSERT INTO battle_logs (CharacterId, MonsterId, RoundNumber, PlayerAction, DamageDealt, DamageTaken, MonsterHpAfter, PlayerHpAfter, PlayerMpAfter, NarrativeText, CreatedAt)
            VALUES (@CharacterId, @MonsterId, @RoundNumber, 'attack', @DamageDealt, 0, 0, @PlayerHpAfter, @PlayerMpAfter, @NarrativeText, @CreatedAt)
            """,
            new
            {
                CharacterId = character.Id,
                MonsterId = monster.Id,
                RoundNumber = roundNumber,
                DamageDealt = playerDamage,
                PlayerHpAfter = newHp,
                PlayerMpAfter = newMp,
                NarrativeText = narrative,
                CreatedAt = DateTime.UtcNow
            });

        return DungeonActionResult.Ok(new
        {
            victory = true,
            characterDead = false,
            gameWon,
            damageDealt = playerDamage,
            monsterHpAfter = 0,
            playerHpAfter = newHp,
            expGained = expGain,
            goldGained = goldGain,
            leveledUp,
            level = newLevel,
            narrative
        });
    }

    private async Task<DungeonActionResult> ResolveCounterAttackAsync(
        CharacterRow character, MonsterRow monster, int roundNumber, int playerDamage, int monsterHpAfter, CancellationToken ct)
    {
        var (_, charDef) = GetCombatStats(character.Class, character.Level);
        var monsterDamage = RollDamage(monster.Attack, charDef);
        var playerHpAfter = Math.Max(0, character.Hp - monsterDamage);
        var characterDead = playerHpAfter <= 0;

        await _db.ExecuteAsync(
            "UPDATE characters SET Hp=@Hp, Status=@Status WHERE Id=@Id",
            new { Hp = playerHpAfter, Status = characterDead ? "dead" : "alive", Id = character.Id });

        var fallback = characterDead
            ? $"你对{monster.Name}造成了 {playerDamage} 点伤害，但对方的反击终结了你的冒险……"
            : $"你对{monster.Name}造成了 {playerDamage} 点伤害，对方反击造成 {monsterDamage} 点伤害。战斗仍在继续。";
        var prompt = $$"""
你是一位哥特奇幻风格的地下城主(DM)。请把下面这一回合的战斗过程改写成一段紧张刺激的中文叙述（2到4句话，不要markdown）：
角色：{{character.Name}}（{{character.Race}} {{character.Class}}）对战 {{monster.Name}}（{{monster.FlavorText}}）
本回合：角色造成 {{playerDamage}} 伤害（怪物剩余HP {{monsterHpAfter}}/{{monster.Hp}}），怪物反击造成 {{monsterDamage}} 伤害（角色剩余HP {{playerHpAfter}}/{{character.MaxHp}}）。
{{(characterDead ? "角色力竭倒地，战斗以失败告终。" : "战斗仍在继续。")}}
""";
        var narrative = await GenerateNarrativeAsync(prompt, fallback, ct);

        await _db.ExecuteAsync(
            """
            INSERT INTO battle_logs (CharacterId, MonsterId, RoundNumber, PlayerAction, DamageDealt, DamageTaken, MonsterHpAfter, PlayerHpAfter, PlayerMpAfter, NarrativeText, CreatedAt)
            VALUES (@CharacterId, @MonsterId, @RoundNumber, 'attack', @DamageDealt, @DamageTaken, @MonsterHpAfter, @PlayerHpAfter, @PlayerMpAfter, @NarrativeText, @CreatedAt)
            """,
            new
            {
                CharacterId = character.Id,
                MonsterId = monster.Id,
                RoundNumber = roundNumber,
                DamageDealt = playerDamage,
                DamageTaken = monsterDamage,
                MonsterHpAfter = monsterHpAfter,
                PlayerHpAfter = playerHpAfter,
                PlayerMpAfter = character.Mp,
                NarrativeText = narrative,
                CreatedAt = DateTime.UtcNow
            });

        return DungeonActionResult.Ok(new
        {
            victory = false,
            characterDead,
            damageDealt = playerDamage,
            damageTaken = monsterDamage,
            monsterHpAfter,
            monsterMaxHp = monster.Hp,
            playerHpAfter,
            narrative
        });
    }

    // ─── 道具使用 ────────────────────────────────────────────────────

    public async Task<DungeonActionResult> UseItemAsync(int ownerUserId, int itemId, CancellationToken ct = default)
    {
        var character = await ResolveCharacterAsync(ownerUserId);
        if (character == null)
            return DungeonActionResult.Fail(404, "未找到你的冒险者角色，请先创建角色");
        if (character.Status != "alive")
            return DungeonActionResult.Fail(400, StatusBlockedMessage(character.Status, "使用物品"));

        var inventory = ParseInventory(character.InventoryJson);
        var entry = inventory.FirstOrDefault(i => i.ItemId == itemId && i.Quantity > 0);
        if (entry == null)
            return DungeonActionResult.Fail(400, "背包中没有这个物品");

        var item = await _db.QueryFirstOrDefaultAsync<ItemRow>("SELECT * FROM items WHERE Id=@Id", new { Id = itemId });
        if (item == null)
            return DungeonActionResult.Fail(500, "物品数据丢失");

        if (!string.Equals(item.Type, "potion", StringComparison.OrdinalIgnoreCase))
            return DungeonActionResult.Fail(400, $"「{item.Name}」无法直接使用（装备/关键道具暂不支持在探索中直接使用）");

        var effect = ParseEffect(item.EffectJson);
        var newHp = character.Hp;
        var newMp = character.Mp;
        var effectDescriptions = new List<string>();
        if (effect.TryGetValue("heal", out var heal))
        {
            var healed = Math.Min(character.MaxHp, character.Hp + heal) - character.Hp;
            newHp = character.Hp + healed;
            effectDescriptions.Add($"恢复了 {healed} 点生命值");
        }
        if (effect.TryGetValue("mp", out var mp))
        {
            var restored = Math.Min(character.MaxMp, character.Mp + mp) - character.Mp;
            newMp = character.Mp + restored;
            effectDescriptions.Add($"恢复了 {restored} 点法力值");
        }
        if (effectDescriptions.Count == 0)
            effectDescriptions.Add("但似乎没有产生任何效果");

        entry.Quantity -= 1;
        inventory = inventory.Where(i => i.Quantity > 0).ToList();

        await _db.ExecuteAsync(
            "UPDATE characters SET Hp=@Hp, Mp=@Mp, InventoryJson=@Inventory WHERE Id=@Id",
            new { Hp = newHp, Mp = newMp, Inventory = SerializeInventory(inventory), Id = character.Id });

        var fallback = $"你使用了「{item.Name}」，{string.Join("，", effectDescriptions)}。";
        var prompt = $$"""
你是一位哥特奇幻风格的地下城主(DM)。请把下面这次道具使用改写成一段简短生动的中文描述（1到2句话，不要markdown）：
角色 {{character.Name}} 使用了「{{item.Name}}」，效果：{{string.Join("，", effectDescriptions)}}。
""";
        var narrative = await GenerateNarrativeAsync(prompt, fallback, ct);

        return DungeonActionResult.Ok(new
        {
            item = item.Name,
            hp = newHp,
            maxHp = character.MaxHp,
            mp = newMp,
            maxMp = character.MaxMp,
            narrative
        });
    }

    // ─── 拾得 ────────────────────────────────────────────────────────

    public async Task<DungeonActionResult> PickUpAsync(int ownerUserId, CancellationToken ct = default)
    {
        var character = await ResolveCharacterAsync(ownerUserId);
        if (character == null)
            return DungeonActionResult.Fail(404, "未找到你的冒险者角色，请先创建角色");
        if (character.Status != "alive")
            return DungeonActionResult.Fail(400, StatusBlockedMessage(character.Status, "拾取物品"));

        var room = await _db.QueryFirstOrDefaultAsync<RoomRow>(
            "SELECT * FROM rooms WHERE Id=@Id", new { Id = character.CurrentRoomId });
        if (room == null || !room.ItemId.HasValue)
            return DungeonActionResult.Fail(400, "这里没有可以拾取的物品");

        var item = await _db.QueryFirstOrDefaultAsync<ItemRow>("SELECT * FROM items WHERE Id=@Id", new { Id = room.ItemId.Value });
        if (item == null)
            return DungeonActionResult.Fail(500, "物品数据丢失");

        var inventory = ParseInventory(character.InventoryJson);
        var entry = inventory.FirstOrDefault(i => i.ItemId == item.Id);
        if (entry != null) entry.Quantity += 1;
        else inventory.Add(new InventoryEntry { ItemId = item.Id, Quantity = 1 });

        await _db.ExecuteAsync(
            "UPDATE characters SET InventoryJson=@Inventory WHERE Id=@Id",
            new { Inventory = SerializeInventory(inventory), Id = character.Id });
        await _db.ExecuteAsync(
            "UPDATE rooms SET ItemId=NULL WHERE Id=@Id",
            new { Id = room.Id });

        var fallback = $"你捡起了「{item.Name}」，收入了背包。";
        var prompt = $$"""
你是一位哥特奇幻风格的地下城主(DM)。请把下面这次拾取物品的场景改写成一段简短生动的中文描述（1到2句话，不要markdown）：
角色 {{character.Name}} 在「{{room.Name}}」中拾取了「{{item.Name}}」。
""";
        var narrative = await GenerateNarrativeAsync(prompt, fallback, ct);

        return DungeonActionResult.Ok(new
        {
            item = item.Name,
            narrative
        });
    }

    // ─── 状態スナップショット（UI 用・読み取り専用） ──────────────────

    public async Task<DungeonActionResult> GetStateAsync(int ownerUserId, CancellationToken ct = default)
    {
        var character = await ResolveCharacterAsync(ownerUserId);
        if (character == null)
            return DungeonActionResult.Fail(404, "未找到你的冒险者角色，请先创建角色");

        var room = await _db.QueryFirstOrDefaultAsync<RoomRow>(
            "SELECT * FROM rooms WHERE Id=@Id", new { Id = character.CurrentRoomId });
        if (room == null)
            return DungeonActionResult.Fail(500, "当前房间数据丢失");

        var exits = ParseExits(room.ExitsJson)
            .Where(kv => kv.Value.HasValue)
            .Select(kv => new { direction = kv.Key, roomId = kv.Value!.Value })
            .ToList();

        object? monster = null;
        if (room.MonsterId.HasValue && !room.IsCleared)
        {
            var m = await _db.QueryFirstOrDefaultAsync<MonsterRow>(
                "SELECT * FROM monsters WHERE Id=@Id", new { Id = room.MonsterId.Value });
            if (m != null)
            {
                // attack と同じ「最後の battle_logs から進行中HPを復元する」ルールを再利用し、
                // 表示上のHPと実際の戦闘解決ロジックが食い違わないようにする。
                var lastHp = await _db.QueryFirstOrDefaultAsync<int?>(
                    "SELECT MonsterHpAfter FROM battle_logs WHERE CharacterId=@Cid AND MonsterId=@Mid ORDER BY Id DESC LIMIT 1",
                    new { Cid = character.Id, Mid = m.Id });
                var hp = (lastHp.HasValue && lastHp.Value > 0) ? lastHp.Value : m.Hp;
                monster = new { id = m.Id, name = m.Name, hp, maxHp = m.Hp, attack = m.Attack, flavorText = m.FlavorText };
            }
        }

        object? roomItem = null;
        if (room.ItemId.HasValue)
        {
            var it = await _db.QueryFirstOrDefaultAsync<ItemRow>("SELECT * FROM items WHERE Id=@Id", new { Id = room.ItemId.Value });
            if (it != null) roomItem = new { id = it.Id, name = it.Name, type = it.Type };
        }

        var inventory = ParseInventory(character.InventoryJson);
        var invDetails = new List<object>();
        foreach (var entry in inventory.Where(e => e.Quantity > 0))
        {
            var it = await _db.QueryFirstOrDefaultAsync<ItemRow>("SELECT * FROM items WHERE Id=@Id", new { Id = entry.ItemId });
            if (it != null) invDetails.Add(new { itemId = it.Id, name = it.Name, type = it.Type, quantity = entry.Quantity });
        }

        return DungeonActionResult.Ok(new
        {
            character = new
            {
                character.Name,
                character.Race,
                character.Class,
                character.Level,
                character.Hp,
                character.MaxHp,
                character.Mp,
                character.MaxMp,
                character.Gold,
                character.Experience,
                character.Status
            },
            room = new { room.Id, room.Name, room.Description, room.IsBossRoom, room.IsCleared },
            exits,
            monster,
            roomItem,
            inventory = invDetails,
            gameWon = character.Status == "victorious",
            characterDead = character.Status == "dead"
        });
    }

    // ─── 共通ヘルパー ────────────────────────────────────────────────

    /// <summary>角色处于非 alive 状态（死亡/已通关）时，给出区分清晰的阻断提示，而不是笼统的"已死亡"。</summary>
    private static string StatusBlockedMessage(string status, string actionLabel) => status switch
    {
        "victorious" => "你已经击败了地下城最深处的暗影领主，本局冒险已经通关，无法继续操作。",
        "dead" => $"你的角色已经死亡，无法{actionLabel}。",
        _ => $"你的角色当前状态（{status}）无法{actionLabel}。"
    };

    private async Task<CharacterRow?> ResolveCharacterAsync(int ownerUserId)
    {
        return await _db.QueryFirstOrDefaultAsync<CharacterRow>(
            "SELECT * FROM characters WHERE OwnerUserId=@OwnerUserId ORDER BY Id DESC LIMIT 1",
            new { OwnerUserId = ownerUserId });
    }

    /// <summary>职业 + 等级 决定基础攻击/防御，用于确定性的战斗数值判定（不交给 AI 自由发挥）。</summary>
    private static (int Attack, int Defense) GetCombatStats(string className, int level)
    {
        var (baseAtk, baseDef) = className switch
        {
            "法师" => (16, 2),
            "盗贼" => (14, 4),
            "牧师" => (10, 5),
            _ => (12, 6) // 战士 / 默认
        };
        return (baseAtk + (level - 1) * 3, baseDef + (level - 1) * 1);
    }

    private static int RollDamage(int attack, int defense)
    {
        var variance = Random.Shared.Next(-2, 3); // -2..2
        var raw = attack - defense + variance;
        return Math.Max(1, raw);
    }

    private static int ExpToNextLevel(int level) => level * 100;

    private static (int Level, int Experience, int MaxHp, int MaxMp, bool LeveledUp) ApplyExperience(
        int level, int experience, int maxHp, int maxMp)
    {
        var leveledUp = false;
        while (experience >= ExpToNextLevel(level))
        {
            experience -= ExpToNextLevel(level);
            level += 1;
            maxHp += 10;
            maxMp += 5;
            leveledUp = true;
        }
        return (level, experience, maxHp, maxMp, leveledUp);
    }

    private static List<InventoryEntry> ParseInventory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<InventoryEntry>();
        try { return JsonSerializer.Deserialize<List<InventoryEntry>>(json) ?? new List<InventoryEntry>(); }
        catch { return new List<InventoryEntry>(); }
    }

    private static string SerializeInventory(List<InventoryEntry> inventory) => JsonSerializer.Serialize(inventory);

    private static Dictionary<string, int?> ParseExits(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, int?>>(json) ?? new Dictionary<string, int?>();
            return new Dictionary<string, int?>(raw, StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase); }
    }

    private static Dictionary<string, int> ParseEffect(string? json)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return dict;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var value))
                    dict[prop.Name] = value;
            }
        }
        catch { /* 忽略格式错误的 EffectJson，视为无效果 */ }
        return dict;
    }

    /// <summary>
    /// AI 叙事最佳努力调用：8 秒超时保护，失败/超时/异常一律回退到 fallback 文本，
    /// 确保游戏规则结算（已在调用前完成并即将持久化）不会被 AI 服务的不稳定性拖垮或阻塞。
    /// </summary>
    private async Task<string> GenerateNarrativeAsync(string prompt, string fallback, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var result = await _cliChain.PromptAsync(prompt, projectName: "dungeon-forge", cancellationToken: cts.Token);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
                return result.Text.Trim();
            _logger.LogWarning("地下城叙事 AI 调用失败或为空，使用兜底文本。原因: {Error}", result.Error);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("地下城叙事 AI 调用超时，使用兜底文本。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "地下城叙事 AI 调用异常，使用兜底文本。");
        }
        return fallback;
    }
}
