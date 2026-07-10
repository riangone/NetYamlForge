using System;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.DungeonForge.Hooks;

/// <summary>
/// 房间叙事 Hook —— 方案 A（AI 只负责将结构化房间描述转为沉浸式文本）。
/// 实际调用应放在 RoomNarrationService 中，此处仅预留骨架。
/// </summary>
public class RoomNarrationHook : IEntityHook
{
    private readonly ILogger<RoomNarrationHook> _logger;

    public string Name => "room_narration";

    public RoomNarrationHook(ILogger<RoomNarrationHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        _logger.LogInformation("房间叙事 Hook 触发 (预留)");
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
