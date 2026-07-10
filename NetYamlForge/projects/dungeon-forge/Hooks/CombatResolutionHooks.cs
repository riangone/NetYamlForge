using System;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.DungeonForge.Hooks;

/// <summary>
/// 战斗结算 Hook —— 方案 A（AI 只叙事，不参与规则判定）。
/// 实际战斗逻辑应放在独立的 IDungeonCombatService + API Controller 中，
/// 此处仅预留骨架作为实体 Hook 的注册占位。
/// </summary>
public class CombatResolutionHook : IEntityHook
{
    private readonly ILogger<CombatResolutionHook> _logger;

    public string Name => "combat_resolution";

    public CombatResolutionHook(ILogger<CombatResolutionHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        _logger.LogInformation("战斗结算 Hook 触发 (预留)");
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
