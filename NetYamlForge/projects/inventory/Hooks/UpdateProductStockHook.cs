// 責務: 在庫移動登録後に product.stock_qty を自動更新する。
// stock_movement.afterCreate で movement_type に応じて加減算する。

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// [在庫管理] 在庫移動登録後に商品の在庫数を自動更新するフック。
/// movement_type:
///   in          → stock_qty += quantity
///   out         → stock_qty -= quantity
///   adjustment  → stock_qty = quantity（絶対値セット）
///
/// entities.yml での使用例:
///   hooks:
///     afterCreate: [update_product_stock]
/// </summary>
public sealed class UpdateProductStockHook : IEntityHook
{
    private readonly ILogger<UpdateProductStockHook> _logger;

    public string Name => "update_product_stock";

    public UpdateProductStockHook(ILogger<UpdateProductStockHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("product_id", out var productIdObj) || productIdObj == null)
            return;
        if (!int.TryParse(productIdObj.ToString(), out var productId))
            return;

        if (!ctx.Values.TryGetValue("quantity", out var quantityObj) || quantityObj == null)
            return;
        if (!int.TryParse(quantityObj.ToString(), out var quantity))
            return;

        if (!ctx.Values.TryGetValue("movement_type", out var typeObj))
            return;
        var movementType = typeObj?.ToString()?.ToLowerInvariant();

        string sql;
        switch (movementType)
        {
            case "in":
#pragma warning disable DCS001
                sql = "UPDATE product SET stock_qty = stock_qty + @Quantity WHERE id = @ProductId";
#pragma warning restore DCS001
                break;
            case "out":
#pragma warning disable DCS001
                sql = "UPDATE product SET stock_qty = stock_qty - @Quantity WHERE id = @ProductId";
#pragma warning restore DCS001
                break;
            case "adjustment":
#pragma warning disable DCS001
                sql = "UPDATE product SET stock_qty = @Quantity WHERE id = @ProductId";
#pragma warning restore DCS001
                break;
            default:
                _logger.LogWarning("[Hook:update_product_stock] 未知の movement_type: {Type}", movementType);
                return;
        }

        await db.ExecuteAsync(sql, new { ProductId = productId, Quantity = quantity }, tx);
        _logger.LogInformation(
            "[Hook:update_product_stock] 在庫更新: product_id={ProductId}, type={Type}, qty={Qty}",
            productId, movementType, quantity);
    }
}
