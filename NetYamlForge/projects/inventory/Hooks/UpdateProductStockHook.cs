// 責務：在庫移動時に商品在庫を更新するフック

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// 在庫管理フック：在庫移動に基づく商品在庫数の自動更新
/// </summary>
public sealed class UpdateProductStockHook : IEntityHook
{
    private readonly ILogger<UpdateProductStockHook> _logger;

    public string Name => "update_product_stock";

    public UpdateProductStockHook(ILogger<UpdateProductStockHook> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create)
            return Task.FromResult(HookResult.Continue());

        // 在庫移動のバリデーション
        if (!ctx.Values.TryGetValue("ProductId", out var productIdObj) || productIdObj is not int productId)
            return Task.FromResult(HookResult.Abort("商品 ID が指定されていません。"));

        if (!ctx.Values.TryGetValue("Quantity", out var quantityObj) || quantityObj is not int quantity)
            return Task.FromResult(HookResult.Abort("数量が指定されていません。"));

        if (!ctx.Values.TryGetValue("MovementType", out var movementTypeObj) || movementTypeObj is not string movementType)
            return Task.FromResult(HookResult.Abort("移動種別が指定されていません。"));

        // 移動種別による数量の符号決定
        int stockChange = movementType switch
        {
            "IN" => quantity,      // 入荷：増加
            "OUT" => -quantity,    // 出荷：減少
            "ADJUST" => quantity,  // 調整：指定値
            _ => 0
        };

        // 現在の在庫数を取得
        var currentStockSql = "SELECT Stock FROM Products WHERE Id = @ProductId";
        var currentStock = db.ExecuteScalar<int?>(currentStockSql, new { ProductId = productId }, tx) ?? 0;

        var newStock = currentStock + stockChange;

        if (newStock < 0)
        {
            _logger.LogWarning("在庫数が負になります。商品 ID: {ProductId}, 現在：{CurrentStock}, 変更：{StockChange}", 
                productId, currentStock, stockChange);
            return Task.FromResult(HookResult.Abort($"在庫不足です。現在：{currentStock}, 必要：{quantity}"));
        }

        // 商品在庫を更新
        var updateSql = "UPDATE Products SET Stock = @Stock, UpdatedAt = datetime('now') WHERE Id = @ProductId";
        db.Execute(updateSql, new { Stock = newStock, ProductId = productId }, tx);

        _logger.LogInformation(
            "商品 ID {ProductId} の在庫を更新：{Before} → {After}",
            productId, currentStock, newStock);

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
