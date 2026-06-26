// 自動生成スタブ: dotnet run -- --scaffold-missing-hooks --project=framework で生成
// 各クラスの BeforeAsync / AfterAsync にビジネスロジックを実装してください。

using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Framework.Hooks;

public sealed class HashPasswordHook : IEntityHook
{
    private readonly ILogger<HashPasswordHook> _logger;
    public string Name => "hash_password";

    public HashPasswordHook(ILogger<HashPasswordHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class HashPasswordIfChangedHook : IEntityHook
{
    private readonly ILogger<HashPasswordIfChangedHook> _logger;
    public string Name => "hash_password_if_changed";

    public HashPasswordIfChangedHook(ILogger<HashPasswordIfChangedHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class ValidateUsernameHook : IEntityHook
{
    private readonly ILogger<ValidateUsernameHook> _logger;
    public string Name => "validate_username";

    public ValidateUsernameHook(ILogger<ValidateUsernameHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
