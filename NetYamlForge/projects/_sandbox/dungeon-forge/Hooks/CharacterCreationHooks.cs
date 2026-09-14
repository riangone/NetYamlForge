using System;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.DungeonForge.Hooks;

public class InitStartingStatsHook : IEntityHook
{
    private readonly ILogger<InitStartingStatsHook> _logger;

    public string Name => "init_starting_stats";

    public InitStartingStatsHook(ILogger<InitStartingStatsHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var cls = ctx.Values.GetValueOrDefault("Class")?.ToString() ?? "战士";
        var (hp, mp) = cls switch
        {
            "法师" => (60, 100),
            "牧师" => (70, 80),
            "盗贼" => (80, 30),
            _ => (100, 10),
        };
        ctx.Values["Hp"] = hp;
        ctx.Values["MaxHp"] = hp;
        ctx.Values["Mp"] = mp;
        ctx.Values["MaxMp"] = mp;
        ctx.Values["Level"] = 1;
        ctx.Values["Gold"] = 0;
        ctx.Values["Experience"] = 0;
        ctx.Values["CurrentRoomId"] = 1;
        ctx.Values["Status"] = "alive";

        _logger.LogInformation("初始化角色属性: Class={Class}, Hp={Hp}, Mp={Mp}", cls, hp, mp);
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
