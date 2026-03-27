// inventory プロジェクト固有フック
// 商品・在庫・入出庫の入力検証・自動補完フックのサンプル実装です。
//
// YAML での参照例:
//   product.yml:
//     hooks:
//       beforeCreate: [normalize_product_code, validate_product_price]
//       beforeUpdate: [normalize_product_code, validate_product_price]
//   stock.yml:
//     hooks:
//       beforeCreate: [validate_stock_quantity, set_stock_updated_at]
//       beforeUpdate: [validate_stock_quantity, set_stock_updated_at]
//   stock_movement.yml:
//     hooks:
//       beforeCreate: [set_moved_at_default, validate_movement_type, validate_movement_quantity]
//       beforeUpdate: [validate_movement_type, validate_movement_quantity]
//       afterCreate:  [update_stock_after_movement]

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// 商品コードをトリム＋大文字変換するフック。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class NormalizeProductCodeHook : IEntityHook
{
    public string Name => "normalize_product_code";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("Code", out var code) && code is string s)
        {
            ctx.Values["Code"] = s.Trim().ToUpperInvariant();
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 原価・販売価格が 0 以上であることを検証するフック。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateProductPriceHook : IEntityHook
{
    public string Name => "validate_product_price";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        foreach (var field in new[] { "CostPrice", "SellingPrice" })
        {
            if (ctx.Values.TryGetValue(field, out var val) && val != null)
            {
                var str = val.ToString() ?? "0";
                if (decimal.TryParse(str, out var price) && price < 0)
                    return Task.FromResult(HookResult.Abort($"{field} は 0 以上の値を入力してください。"));
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 在庫数量が 0 以上であることを検証するフック。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateStockQuantityHook : IEntityHook
{
    public string Name => "validate_stock_quantity";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("Quantity", out var val) && val != null)
        {
            var str = val.ToString() ?? "0";
            if (decimal.TryParse(str, out var qty) && qty < 0)
                return Task.FromResult(HookResult.Abort("在庫数量は 0 以上の値を入力してください。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 在庫作成・更新時に UpdatedAt を今日の日付に自動設定するフック。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class SetStockUpdatedAtHook : IEntityHook
{
    public string Name => "set_stock_updated_at";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Values["UpdatedAt"] = DateTime.Today.ToString("yyyy-MM-dd");
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 入出庫数量が 0 より大きいことを検証するフック。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateMovementQuantityHook : IEntityHook
{
    public string Name => "validate_movement_quantity";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("Quantity", out var val) && val != null)
        {
            var str = val.ToString() ?? "0";
            if (decimal.TryParse(str, out var qty) && qty <= 0)
                return Task.FromResult(HookResult.Abort("入出庫数量は 0 より大きい値を入力してください。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 入出庫種別が in/out/transfer のいずれかであることを検証するフック。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateMovementTypeHook : IEntityHook
{
    public string Name => "validate_movement_type";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("MovementType", out var val))
        {
            var validTypes = new[] { "in", "out", "transfer" };
            if (!Array.Exists(validTypes, t => t == val?.ToString()))
                return Task.FromResult(HookResult.Abort("種別は無効な値です（in/out/transfer のいずれか）。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 入出庫日（MovedAt）が未設定の場合に今日の日付を自動補完するフック。
/// YAML: beforeCreate に指定します。
/// </summary>
public class SetMovedAtDefaultHook : IEntityHook
{
    public string Name => "set_moved_at_default";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var hasValue = ctx.Values.TryGetValue("MovedAt", out var val)
                       && val != null
                       && !string.IsNullOrWhiteSpace(val.ToString());
        if (!hasValue)
        {
            ctx.Values["MovedAt"] = DateTime.Today.ToString("yyyy-MM-dd");
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 入出庫登録後に Stock テーブルの数量を自動更新するフック（AfterAsync の高度な例）。
///
/// - MovementType = "in"       → Quantity を加算
/// - MovementType = "out"      → Quantity を減算（0 未満にはならない）
/// - MovementType = "transfer" → 本フックでは数量を変更しない（別途処理が必要）
///
/// YAML: afterCreate に指定します。
/// </summary>
public class StockUpdateAfterMovementHook : IEntityHook
{
    public string Name => "update_stock_after_movement";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("ProductId", out var productIdObj)
            || !ctx.Values.TryGetValue("WarehouseId", out var warehouseIdObj)
            || !ctx.Values.TryGetValue("MovementType", out var typeObj)
            || !ctx.Values.TryGetValue("Quantity", out var qtyObj))
        {
            return;
        }

        if (!int.TryParse(productIdObj?.ToString(), out var productId)
            || !int.TryParse(warehouseIdObj?.ToString(), out var warehouseId)
            || !decimal.TryParse(qtyObj?.ToString(), out var quantity))
        {
            return;
        }

        var movementType = typeObj?.ToString();
        if (movementType == "transfer") return;   // 移動は呼び出し元で制御

        // 在庫レコードが存在するか確認
        const string selectSql = @"
            SELECT COUNT(1) FROM Stock
            WHERE ProductId = @productId AND WarehouseId = @warehouseId";

        var exists = await db.ExecuteScalarAsync<int>(
            selectSql, new { productId, warehouseId }, tx) > 0;

        if (exists)
        {
            var delta = movementType == "in" ? quantity : -quantity;
            const string updateSql = @"
                UPDATE Stock
                SET Quantity  = MAX(0, Quantity + @delta),
                    UpdatedAt = @today
                WHERE ProductId   = @productId
                  AND WarehouseId = @warehouseId";

            await db.ExecuteAsync(
                updateSql,
                new { delta, today = DateTime.Today.ToString("yyyy-MM-dd"), productId, warehouseId },
                tx);
        }
        else
        {
            // 在庫レコードが存在しない場合は初期作成（入庫のみ）
            if (movementType == "in")
            {
                const string insertSql = @"
                    INSERT INTO Stock (ProductId, WarehouseId, Quantity, UpdatedAt)
                    VALUES (@productId, @warehouseId, @quantity, @today)";

                await db.ExecuteAsync(
                    insertSql,
                    new { productId, warehouseId, quantity, today = DateTime.Today.ToString("yyyy-MM-dd") },
                    tx);
            }
        }
    }
}
