// ui-showcase プロジェクト固有フック
// Section Table Demo で使用するカスタムフックのサンプル実装です。

using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.UiShowcase.Hooks;

/// <summary>
/// 在庫数が 0 以上であることを検証するフック。
/// catalog_items テーブルの quantity フィールドに適用します。
/// YAML: beforeCreate: [validate_quantity_positive]
/// </summary>
public class ValidateQuantityPositiveHook : IEntityHook
{
    public string Name => "validate_quantity_positive";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("quantity", out var qty))
        {
            var qtyStr = qty?.ToString() ?? "0";
            if (int.TryParse(qtyStr, out var qtyInt) && qtyInt < 0)
                return Task.FromResult(HookResult.Abort("在庫数は 0 以上を入力してください。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 単価（unit_price）が 0 より大きいことを検証するフック。
/// YAML: beforeCreate: [validate_price_positive]
/// </summary>
public class ValidatePricePositiveHook : IEntityHook
{
    public string Name => "validate_price_positive";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("unit_price", out var price))
        {
            var priceStr = price?.ToString() ?? "0";
            if (decimal.TryParse(priceStr, out var priceVal) && priceVal <= 0)
                return Task.FromResult(HookResult.Abort("単価は 0 より大きい値を入力してください。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 商品名を正規化するフック（前後の空白除去 + 先頭文字を大文字に）。
/// YAML: beforeCreate: [normalize_item_name]
/// </summary>
public class NormalizeItemNameHook : IEntityHook
{
    public string Name => "normalize_item_name";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("item_name", out var name) && name is string s)
        {
            var trimmed = s.Trim();
            ctx.Values["item_name"] = trimmed.Length > 0
                ? char.ToUpperInvariant(trimmed[0]) + trimmed[1..]
                : trimmed;
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
