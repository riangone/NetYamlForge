// 自動生成スタブ: dotnet run -- --scaffold-missing-hooks --project=redmineclone で生成
// 各クラスの BeforeAsync / AfterAsync にビジネスロジックを実装してください。

using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.RedmineClone.Hooks;

public sealed class AuditLogHook : IEntityHook
{
    private readonly ILogger<AuditLogHook> _logger;
    public string Name => "audit_log";

    public AuditLogHook(ILogger<AuditLogHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class CurrentUserHook : IEntityHook
{
    private readonly ILogger<CurrentUserHook> _logger;
    public string Name => "current_user";

    public CurrentUserHook(ILogger<CurrentUserHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class NowHook : IEntityHook
{
    private readonly ILogger<NowHook> _logger;
    public string Name => "now";

    public NowHook(ILogger<NowHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
