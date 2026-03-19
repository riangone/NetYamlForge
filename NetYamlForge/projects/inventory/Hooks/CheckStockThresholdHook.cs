// 責務：在庫閾値をチェックするフック

using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// 在庫管理フック：最小在庫閾値の警告
/// 在庫が最小在庫を下回った場合に警告をログ出力
/// </summary>
public sealed class CheckStockThresholdHook : IEntityHook
{
    private readonly ILogger<CheckStockThresholdHook> _logger;
    private readonly IDbConnection _db;

    public string Name => "check_stock_threshold";

    public CheckStockThresholdHook(ILogger<CheckStockThresholdHook> logger, IDbConnection db)
    {
        _logger = logger;
        _db = db;
    }

    /// <inheritdoc />
    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.FromResult(HookResult.Continue());
    }

    /// <inheritdoc />
    public async Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create && ctx.Operation != CrudOperation.Update)
            return;

        // 商品エンティティのみ処理
        if (ctx.Entity != "product")
            return;

        // 商品 ID から現在の商品情報を取得
        if (ctx.Id is int productId)
        {
            var sql = @"
                SELECT p.Name, p.Stock, p.MinStock, c.Name as CategoryName
                FROM Products p
                LEFT JOIN Categories c ON p.CategoryId = c.Id
                WHERE p.Id = @ProductId";

            var product = await (tx != null ?
                db.QueryFirstOrDefaultAsync<ProductRow>(sql, new { ProductId = productId }, tx) :
                db.QueryFirstOrDefaultAsync<ProductRow>(sql, new { ProductId = productId }));

            if (product != null && product.Stock < product.MinStock)
            {
                _logger.LogWarning(
                    "[在庫アラート] 商品「{ProductName}」(カテゴリ：{CategoryName}) の在庫が閾値を下回っています。現在：{Stock}, 最小：{MinStock}",
                    product.Name, product.CategoryName ?? "未設定", product.Stock, product.MinStock);

                // 必要に応じてメール通知や外部システム連携を実装
            }
        }
    }

    private class ProductRow
    {
        public string Name { get; set; } = "";
        public int Stock { get; set; }
        public int MinStock { get; set; }
        public string? CategoryName { get; set; }
    }
}
