// 責務: 出庫時に在庫数が足りるかチェックする。
// stock_movement.beforeCreate で movement_type = "out" の場合のみ実行。

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// [在庫管理] 出庫時に商品の在庫数が要求数量以上あるかを検証するフック。
/// movement_type = "out" のときのみ在庫チェックを実行する。
///
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: [validate_stock_out]
/// </summary>
public sealed class ValidateStockOutHook : IEntityHook
{
    private readonly ILogger<ValidateStockOutHook> _logger;

    public string Name => "validate_stock_out";

    public ValidateStockOutHook(ILogger<ValidateStockOutHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // movement_type が "out" の場合のみチェック
        if (!ctx.Values.TryGetValue("movement_type", out var typeObj)
            || !string.Equals(typeObj?.ToString(), "out", StringComparison.OrdinalIgnoreCase))
            return HookResult.Continue();

        if (!ctx.Values.TryGetValue("product_id", out var productIdObj) || productIdObj == null)
            return HookResult.Abort("商品が指定されていません。");

        if (!int.TryParse(productIdObj.ToString(), out var productId))
            return HookResult.Abort("無効な商品IDです。");

        if (!ctx.Values.TryGetValue("quantity", out var quantityObj) || quantityObj == null)
            return HookResult.Abort("数量が指定されていません。");

        if (!int.TryParse(quantityObj.ToString(), out var quantity) || quantity <= 0)
            return HookResult.Abort("数量は1以上を指定してください。");

        var stockQty = await db.ExecuteScalarAsync<int>(
            "SELECT stock_qty FROM product WHERE id = @ProductId",
            new { ProductId = productId },
            tx
        );

        if (stockQty < quantity)
        {
            _logger.LogWarning(
                "[Hook:validate_stock_out] 在庫不足: product_id={ProductId}, 在庫={Stock}, 要求={Qty}",
                productId, stockQty, quantity);
            return HookResult.Abort($"在庫が不足しています（現在庫: {stockQty}、要求: {quantity}）。");
        }

        _logger.LogInformation(
            "[Hook:validate_stock_out] 在庫確認OK: product_id={ProductId}, 在庫={Stock}, 要求={Qty}",
            productId, stockQty, quantity);
        return HookResult.Continue();
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
        => Task.CompletedTask;
}
