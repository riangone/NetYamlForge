// ファイル概要: Salesforce-CRM プロジェクトのページ行更新・削除検証。
// 受注状態遷移ルール、明細・顧客・商品の削除ガード、ShippedDate 自動スタンプを実装します。

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.SalesforceCrm.Hooks;

public class SalesforceCrmPageMutationValidator : IProjectPageMutationValidator
{
    public string ProjectName => "salesforce-crm";

    public async Task<(bool ok, string? message)> ValidatePageUpdateAsync(
        string targetTable, int rowId, string field, string value,
        IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(targetTable, "Orders", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(field, "Status", StringComparison.OrdinalIgnoreCase))
            return (true, null);

        // 状態遷移バリデーション
        var currentStatus = await db.ExecuteScalarAsync<string?>(
            "SELECT Status FROM Orders WHERE OrderId = @OrderId",
            new { OrderId = rowId }, tx);

        if (string.IsNullOrWhiteSpace(currentStatus))
            return (false, "対象の受注が見つかりません。");

        if (!IsSupportedOrderStatus(value))
            return (false, $"Status '{value}' はサポート外です。");

        if (!IsOrderTransitionAllowed(currentStatus, value))
            return (false, $"状態遷移が不正です。{currentStatus} -> {value} は許可されていません。");

        if (string.Equals(value, "Shipped", StringComparison.OrdinalIgnoreCase))
        {
            var detailCount = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM OrderDetails WHERE OrderId = @OrderId",
                new { OrderId = rowId }, tx);
            if (detailCount <= 0)
                return (false, "明細がない受注は Shipped にできません。");

            // ShippedDate を自動スタンプ（副作用：検証と同時に実行）
            var shippedDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            await db.ExecuteAsync(
                "UPDATE \"Orders\" SET \"ShippedDate\" = @ShippedDate WHERE \"OrderId\" = @OrderId",
                new { ShippedDate = shippedDate, OrderId = rowId }, tx);
        }

        return (true, null);
    }

    public async Task<(bool ok, string? message)> ValidatePageDeleteAsync(
        string targetTable, int rowId, IDbConnection db, IDbTransaction? tx)
    {
        if (string.Equals(targetTable, "Orders", StringComparison.OrdinalIgnoreCase))
        {
            var row = await db.QueryFirstOrDefaultAsync<(string? Status, int DetailCount)>(
                @"SELECT Status,
                         (SELECT COUNT(*) FROM OrderDetails WHERE OrderId = @OrderId) AS DetailCount
                  FROM Orders WHERE OrderId = @OrderId",
                new { OrderId = rowId }, tx);

            if (string.IsNullOrWhiteSpace(row.Status))
                return (false, "対象の受注が見つかりません。");
            if (string.Equals(row.Status, "Shipped", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(row.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                return (false, $"Shipped/Cancelled の受注は削除できません。(OrderId={rowId})");
            if (row.DetailCount > 0)
                return (false, $"明細が残る受注は削除できません。(OrderId={rowId}, Details={row.DetailCount})");
        }

        if (string.Equals(targetTable, "OrderDetails", StringComparison.OrdinalIgnoreCase))
        {
            var parent = await db.QueryFirstOrDefaultAsync<(int OrderId, string? Status)>(
                "SELECT o.OrderId, o.Status FROM OrderDetails od JOIN Orders o ON o.OrderId = od.OrderId WHERE od.OrderDetailId = @Id",
                new { Id = rowId }, tx);
            if (parent.OrderId <= 0)
                return (false, $"対象の受注明細が見つかりません。(OrderDetailId={rowId})");
            if (string.Equals(parent.Status, "Shipped", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parent.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                return (false, $"Shipped/Cancelled 受注の明細は削除できません。(OrderId={parent.OrderId})");
        }

        if (string.Equals(targetTable, "Customers", StringComparison.OrdinalIgnoreCase))
        {
            var hasOrders = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Orders WHERE CustomerId = @CustomerId",
                new { CustomerId = rowId }, tx);
            if (hasOrders > 0)
                return (false, "受注履歴のある顧客は削除できません。Active=0 で無効化してください。");
        }

        if (string.Equals(targetTable, "Products", StringComparison.OrdinalIgnoreCase))
        {
            var linked = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM OrderDetails WHERE ProductId = @ProductId",
                new { ProductId = rowId }, tx);
            if (linked > 0)
                return (false, "受注明細で利用履歴のある商品は削除できません。Discontinued=1 で販売停止してください。");
        }

        return (true, null);
    }

    private static bool IsSupportedOrderStatus(string s) =>
        string.Equals(s, "Open",      StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s, "Delayed",   StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s, "Shipped",   StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s, "Cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool IsOrderTransitionAllowed(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return true;
        var toLower = to.ToLowerInvariant();
        return from.ToLowerInvariant() switch
        {
            "open"    => toLower is "delayed" or "shipped" or "cancelled",
            "delayed" => toLower is "open"    or "shipped" or "cancelled",
            _         => false
        };
    }
}
