// 自動生成スタブ: dotnet run -- --scaffold-missing-hooks --project=bizdocs で生成
// 各クラスの BeforeAsync / AfterAsync にビジネスロジックを実装してください。

using System.Data;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.BizDocs.Hooks;

public sealed class ValidateJpContractStatusHook : IEntityHook
{
    private readonly ILogger<ValidateJpContractStatusHook> _logger;
    public string Name => "validate_jp_contract_status";

    public ValidateJpContractStatusHook(ILogger<ValidateJpContractStatusHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class ValidateJpDeliveryStatusHook : IEntityHook
{
    private readonly ILogger<ValidateJpDeliveryStatusHook> _logger;
    public string Name => "validate_jp_delivery_status";

    public ValidateJpDeliveryStatusHook(ILogger<ValidateJpDeliveryStatusHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class ValidateJpEstimateStatusHook : IEntityHook
{
    private readonly ILogger<ValidateJpEstimateStatusHook> _logger;
    public string Name => "validate_jp_estimate_status";

    public ValidateJpEstimateStatusHook(ILogger<ValidateJpEstimateStatusHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class ValidateJpInvoiceStatusHook : IEntityHook
{
    private readonly ILogger<ValidateJpInvoiceStatusHook> _logger;
    public string Name => "validate_jp_invoice_status";

    public ValidateJpInvoiceStatusHook(ILogger<ValidateJpInvoiceStatusHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
