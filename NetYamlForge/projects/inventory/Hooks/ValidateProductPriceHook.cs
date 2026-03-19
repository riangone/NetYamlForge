// 責務：商品価格のバリデーションを行うフック
// entities.yml の hooks.beforeCreate / hooks.beforeUpdate で使用

using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// 在庫管理フック：商品価格の検証
/// - 0 以上の価格のみ許可
/// - 上限 1,000,000 円
/// </summary>
public sealed class ValidateProductPriceHook : IEntityHook
{
    private readonly ILogger<ValidateProductPriceHook> _logger;

    public string Name => "validate_product_price";

    public ValidateProductPriceHook(ILogger<ValidateProductPriceHook> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create && ctx.Operation != CrudOperation.Update)
            return Task.FromResult(HookResult.Continue());

        // 価格フィールドの検証
        if (ctx.Values.TryGetValue("Price", out var priceObj) && priceObj is decimal price)
        {
            if (price < 0)
            {
                _logger.LogWarning("商品価格が負の値です：{Price}", price);
                return Task.FromResult(HookResult.Abort("価格は 0 以上である必要があります。"));
            }

            if (price > 1000000)
            {
                _logger.LogWarning("商品価格が上限を超えています：{Price}", price);
                return Task.FromResult(HookResult.Abort("価格の上限は 1,000,000 円です。"));
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    /// <inheritdoc />
    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
