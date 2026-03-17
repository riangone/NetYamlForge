// ファイル概要: salesforce-crm プロジェクト固有のエンティティフック実装です。
// 注文データ整合性チェック（CrmOrderDataGuardHook）等 CRM 固有のビジネスルールを定義します。
// リード・ケース・見積などの CRM エンティティ操作時に呼び出されます。

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.SalesforceCrm.Hooks;

public class CrmOrderDataGuardHook : IEntityHook
{
    private readonly ILogger<CrmOrderDataGuardHook> _logger;
    public string Name => "crm_order_data_guard";

    public CrmOrderDataGuardHook(ILogger<CrmOrderDataGuardHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "order", StringComparison.OrdinalIgnoreCase))
        {
            return HookResult.Continue();
        }

        var isUpdate = ctx.Operation == CrudOperation.Update && ctx.Id.HasValue;
        var existing = isUpdate
            ? await db.QueryFirstOrDefaultAsync<OrderSnapshot>(
                "SELECT OrderDate, RequiredDate, ShippedDate, Freight, Status, CustomerId FROM Orders WHERE OrderId = @OrderId",
                new { OrderId = ctx.Id!.Value }, tx)
            : null;

        if (!TryGetDate(ctx.Values, "OrderDate", out var orderDate) && existing != null)
        {
            if (TryParseDate(existing.OrderDate, out var parsedOrderDate))
            {
                orderDate = parsedOrderDate;
            }
        }

        if (!TryGetDate(ctx.Values, "RequiredDate", out var requiredDate) && existing != null)
        {
            if (TryParseDate(existing.RequiredDate, out var parsedRequiredDate))
            {
                requiredDate = parsedRequiredDate;
            }
        }

        if (orderDate.HasValue && requiredDate.HasValue && requiredDate.Value.Date < orderDate.Value.Date)
        {
            return HookResult.Abort("RequiredDate は OrderDate 以降である必要があります。");
        }

        if (!TryGetDecimal(ctx.Values, "Freight", out var freight) && existing != null)
        {
            freight = existing.Freight;
        }

        if (freight < 0 || freight > 50000)
        {
            return HookResult.Abort("Freight は 0 以上 50000 以下で入力してください。");
        }

        var status = GetString(ctx.Values, "Status");
        if (string.IsNullOrWhiteSpace(status))
        {
            status = existing != null ? existing.Status : "Open";
            ctx.Values["Status"] = status;
        }

        if (!IsSupportedStatus(status))
        {
            return HookResult.Abort($"Status '{status}' はサポート外です。Open / Delayed / Shipped / Cancelled を指定してください。");
        }

        if (string.Equals(status, "Shipped", StringComparison.OrdinalIgnoreCase))
        {
            if (!ctx.Values.ContainsKey("ShippedDate") || string.IsNullOrWhiteSpace(GetString(ctx.Values, "ShippedDate")))
            {
                ctx.Values["ShippedDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
        }

        int customerId;
        if (TryGetInt(ctx.Values, "CustomerId", out customerId))
        {
            var active = await db.ExecuteScalarAsync<int?>(
                "SELECT Active FROM Customers WHERE CustomerId = @CustomerId",
                new { CustomerId = customerId }, tx);
            if (active == 0)
            {
                return HookResult.Abort("非アクティブ顧客には受注を登録・更新できません。");
            }
        }

        _logger.LogInformation("[CRM] Order data guard passed. op={Operation}, id={OrderId}", ctx.Operation, ctx.Id);
        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;

    private static bool IsSupportedStatus(string? status)
    {
        return string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "Delayed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "Shipped", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(IDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static bool TryGetInt(IDictionary<string, object?> values, string key, out int number)
    {
        number = 0;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }
        return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryGetDecimal(IDictionary<string, object?> values, string key, out decimal number)
    {
        number = 0;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }
        return decimal.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryGetDate(IDictionary<string, object?> values, string key, out DateTime? date)
    {
        date = null;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }
        if (raw is DateTime dt)
        {
            date = dt;
            return true;
        }
        if (TryParseDate(raw.ToString(), out var parsed))
        {
            date = parsed;
            return true;
        }
        return false;
    }

    private static bool TryParseDate(string? raw, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value)
               || DateTime.TryParse(raw, out value);
    }

    private sealed class OrderSnapshot
    {
        public string? OrderDate { get; set; }
        public string? RequiredDate { get; set; }
        public decimal Freight { get; set; }
        public string? Status { get; set; }
    }
}

public class CrmOrderStatusTransitionHook : IEntityHook
{
    private readonly ILogger<CrmOrderStatusTransitionHook> _logger;
    public string Name => "crm_order_status_transition";

    public CrmOrderStatusTransitionHook(ILogger<CrmOrderStatusTransitionHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "order", StringComparison.OrdinalIgnoreCase) ||
            ctx.Operation != CrudOperation.Update ||
            !ctx.Id.HasValue)
        {
            return HookResult.Continue();
        }

        if (!ctx.Values.TryGetValue("Status", out var nextObj) || string.IsNullOrWhiteSpace(nextObj?.ToString()))
        {
            return HookResult.Continue();
        }

        var nextStatus = nextObj!.ToString()!;
        var currentStatus = await db.ExecuteScalarAsync<string?>(
            "SELECT Status FROM Orders WHERE OrderId = @OrderId",
            new { OrderId = ctx.Id.Value }, tx);

        if (string.IsNullOrWhiteSpace(currentStatus))
        {
            return HookResult.Abort("対象の受注が見つかりません。");
        }

        if (!IsTransitionAllowed(currentStatus, nextStatus))
        {
            return HookResult.Abort($"状態遷移が不正です。{currentStatus} -> {nextStatus} は許可されていません。");
        }

        if (string.Equals(nextStatus, "Shipped", StringComparison.OrdinalIgnoreCase))
        {
            var detailCount = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM OrderDetails WHERE OrderId = @OrderId",
                new { OrderId = ctx.Id.Value }, tx);
            if (detailCount <= 0)
            {
                return HookResult.Abort("明細がない受注は Shipped にできません。");
            }
        }

        ctx.Data["crm.previous_status"] = currentStatus;
        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        _logger.LogDebug("[CRM] Order status transition checked. id={OrderId}", ctx.Id);
        return Task.CompletedTask;
    }

    private static bool IsTransitionAllowed(string currentStatus, string nextStatus)
    {
        if (string.Equals(currentStatus, nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(currentStatus, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(nextStatus, "Delayed", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(nextStatus, "Shipped", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(nextStatus, "Cancelled", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(currentStatus, "Delayed", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(nextStatus, "Open", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(nextStatus, "Shipped", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(nextStatus, "Cancelled", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(currentStatus, "Shipped", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(currentStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return false;
    }
}

public class CrmOrderAuditTrailHook : IEntityHook
{
    public string Name => "crm_order_audit_trail";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "order", StringComparison.OrdinalIgnoreCase) || !ctx.Id.HasValue)
        {
            return HookResult.Continue();
        }

        if (ctx.Operation == CrudOperation.Delete)
        {
            var currentStatus = await db.ExecuteScalarAsync<string?>(
                "SELECT Status FROM Orders WHERE OrderId = @OrderId",
                new { OrderId = ctx.Id.Value }, tx);
            if (!string.IsNullOrWhiteSpace(currentStatus))
            {
                ctx.Data["crm.previous_status"] = currentStatus;
            }
        }

        return HookResult.Continue();
    }

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var id = ctx.Id?.ToString() ?? "(new)";
        var prevStatus = ctx.Data.TryGetValue("crm.previous_status", out var ps) ? ps?.ToString() : null;
        var nextStatus = ctx.Values.TryGetValue("Status", out var ns) ? ns?.ToString() : null;
        string detail;
        if (ctx.Operation == CrudOperation.Delete)
        {
            detail = $"[CRM] Order {id} deleted. previous_status={prevStatus ?? "Unknown"}";
        }
        else
        {
            detail = prevStatus == null
                ? $"[CRM] Order {id} created. status={nextStatus ?? "Open"}"
                : $"[CRM] Order {id} status {prevStatus} -> {nextStatus}";
        }

        await db.ExecuteAsync(
            "INSERT INTO AuditLog(UserName, Action, Entity, Detail, CreatedAt) VALUES(@UserName,@Action,@Entity,@Detail,@CreatedAt)",
            new
            {
                UserName = ctx.UserName ?? "system",
                Action = "crm_order_event",
                Entity = "order",
                Detail = detail,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            }, tx);
    }
}

public class CrmOrderDeleteGuardHook : IEntityHook
{
    public string Name => "crm_order_delete_guard";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "order", StringComparison.OrdinalIgnoreCase) ||
            ctx.Operation != CrudOperation.Delete ||
            !ctx.Id.HasValue)
        {
            return HookResult.Continue();
        }

        var snapshot = await db.QueryFirstOrDefaultAsync<OrderDeleteSnapshot>(
            @"SELECT Status, (SELECT COUNT(*) FROM OrderDetails WHERE OrderId = @OrderId) AS DetailCount
              FROM Orders WHERE OrderId = @OrderId",
            new { OrderId = ctx.Id.Value }, tx);
        if (snapshot == null)
        {
            return HookResult.Abort("削除対象の受注が見つかりません。");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Status))
        {
            ctx.Data["crm.previous_status"] = snapshot.Status;
        }

        if (string.Equals(snapshot.Status, "Shipped", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return HookResult.Abort($"Shipped / Cancelled の受注は削除できません。(OrderId={ctx.Id.Value}) キャンセル履歴は監査対象のため保持してください。");
        }

        if (snapshot.DetailCount > 0)
        {
            return HookResult.Abort($"明細が残る受注は削除できません。(OrderId={ctx.Id.Value}, Details={snapshot.DetailCount}) 先に受注明細を削除または受注を整理してください。");
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;

    private sealed class OrderDeleteSnapshot
    {
        public string? Status { get; set; }
        public int DetailCount { get; set; }
    }
}

public class CrmOrderDetailGuardHook : IEntityHook
{
    public string Name => "crm_orderdetail_guard";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "orderdetail", StringComparison.OrdinalIgnoreCase))
        {
            return HookResult.Continue();
        }

        if (ctx.Operation == CrudOperation.Delete)
        {
            if (!ctx.Id.HasValue)
            {
                return HookResult.Abort("削除対象の明細IDが解決できません。");
            }

            var deleteOrderId = await db.ExecuteScalarAsync<int?>(
                "SELECT OrderId FROM OrderDetails WHERE OrderDetailId = @OrderDetailId",
                new { OrderDetailId = ctx.Id.Value }, tx);
            if (deleteOrderId.HasValue)
            {
                ctx.Data["orderdetail.order_id"] = deleteOrderId.Value;
            }

            var parentStatus = await db.ExecuteScalarAsync<string?>(
                "SELECT o.Status FROM OrderDetails od JOIN Orders o ON o.OrderId = od.OrderId WHERE od.OrderDetailId = @OrderDetailId",
                new { OrderDetailId = ctx.Id.Value }, tx);
            if (string.Equals(parentStatus, "Shipped", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parentStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                var orderInfo = deleteOrderId.HasValue ? $"(OrderId={deleteOrderId.Value}) " : string.Empty;
                return HookResult.Abort($"Shipped / Cancelled の受注には明細を追加・更新・削除できません。{orderInfo}");
            }

            return HookResult.Continue();
        }

        DetailSnapshot? existingDetail = null;
        if (ctx.Operation == CrudOperation.Update)
        {
            if (!ctx.Id.HasValue)
            {
                return HookResult.Abort("更新対象の明細IDが解決できません。");
            }

            existingDetail = await db.QueryFirstOrDefaultAsync<DetailSnapshot>(
                "SELECT OrderId, ProductId, Quantity, UnitPrice FROM OrderDetails WHERE OrderDetailId = @OrderDetailId",
                new { OrderDetailId = ctx.Id.Value }, tx);
            if (existingDetail == null)
            {
                return HookResult.Abort("更新対象の明細が見つかりません。");
            }

            ctx.Data["orderdetail.old_order_id"] = existingDetail.OrderId;
            ctx.Data["orderdetail.old_product_id"] = existingDetail.ProductId;
            ctx.Data["orderdetail.old_quantity"] = existingDetail.Quantity;
        }

        var orderId = existingDetail?.OrderId ?? 0;
        if (TryGetInt(ctx.Values, "OrderId", out var parsedOrderId))
        {
            orderId = parsedOrderId;
        }
        if (orderId <= 0)
        {
            return HookResult.Abort("OrderId が必要です。");
        }

        var productId = existingDetail?.ProductId ?? 0;
        if (TryGetInt(ctx.Values, "ProductId", out var parsedProductId))
        {
            productId = parsedProductId;
        }
        if (productId <= 0)
        {
            return HookResult.Abort("ProductId が必要です。");
        }

        var quantity = existingDetail?.Quantity ?? 0;
        if (TryGetInt(ctx.Values, "Quantity", out var parsedQuantity))
        {
            quantity = parsedQuantity;
        }
        if (quantity <= 0)
        {
            return HookResult.Abort("Quantity は 1 以上である必要があります。");
        }

        var unitPrice = existingDetail?.UnitPrice ?? 0m;
        if (TryGetDecimal(ctx.Values, "UnitPrice", out var parsedUnitPrice))
        {
            unitPrice = parsedUnitPrice;
        }
        if (unitPrice < 0)
        {
            return HookResult.Abort("UnitPrice は 0 以上である必要があります。");
        }

        if (!TryGetDecimal(ctx.Values, "Discount", out var discount))
        {
            discount = 0m;
        }
        if (discount < 0m || discount > 0.8m)
        {
            return HookResult.Abort("Discount は 0.00 ～ 0.80 の範囲で入力してください。");
        }

        var product = await db.QueryFirstOrDefaultAsync<ProductSnapshot>(
            "SELECT UnitsInStock, Discontinued FROM Products WHERE ProductId = @ProductId",
            new { ProductId = productId }, tx);
        if (product == null)
        {
            return HookResult.Abort("指定した Product が存在しません。");
        }

        var stock = product.UnitsInStock;
        var discontinued = product.Discontinued;
        if (discontinued == 1)
        {
            return HookResult.Abort("販売終了（Discontinued=1）の商品は明細に追加できません。");
        }

        var availableStock = stock;
        if (ctx.Operation == CrudOperation.Update &&
            existingDetail != null &&
            existingDetail.ProductId == productId)
        {
            availableStock += existingDetail.Quantity;
        }

        if (quantity > availableStock)
        {
            return HookResult.Abort($"在庫不足です。要求数量={quantity}, 利用可能在庫={availableStock}");
        }

        var orderStatus = await db.ExecuteScalarAsync<string?>(
            "SELECT Status FROM Orders WHERE OrderId = @OrderId",
            new { OrderId = orderId }, tx);
        if (string.Equals(orderStatus, "Shipped", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return HookResult.Abort("Shipped / Cancelled の受注には明細を追加・更新できません。");
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;

    private static bool TryGetInt(IDictionary<string, object?> values, string key, out int number)
    {
        number = 0;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }
        return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryGetDecimal(IDictionary<string, object?> values, string key, out decimal number)
    {
        number = 0;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }
        return decimal.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }

    private sealed class ProductSnapshot
    {
        public int UnitsInStock { get; set; }
        public int Discontinued { get; set; }
    }

    private sealed class DetailSnapshot
    {
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}

public class CrmOrderDetailProjectionHook : IEntityHook
{
    public string Name => "crm_orderdetail_projection";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "orderdetail", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var orderId = 0;
        if (!TryGetInt(ctx.Values, "OrderId", out orderId) && ctx.Id.HasValue)
        {
            orderId = await db.ExecuteScalarAsync<int>(
                "SELECT OrderId FROM OrderDetails WHERE OrderDetailId = @OrderDetailId",
                new { OrderDetailId = ctx.Id.Value }, tx);
        }
        if (orderId <= 0 &&
            ctx.Data.TryGetValue("orderdetail.order_id", out var fromHookData) &&
            int.TryParse(fromHookData?.ToString(), out var parsedOrderId))
        {
            orderId = parsedOrderId;
        }

        if (orderId <= 0)
        {
            return;
        }

        var productIds = (await db.QueryAsync<int>(
            "SELECT DISTINCT ProductId FROM OrderDetails WHERE OrderId = @OrderId ORDER BY ProductId",
            new { OrderId = orderId }, tx)).ToList();

        var related = string.Join(",", productIds);
        await db.ExecuteAsync(
            "UPDATE Orders SET RelatedProductIds = @RelatedProductIds WHERE OrderId = @OrderId",
            new { RelatedProductIds = related, OrderId = orderId }, tx);
    }

    private static bool TryGetInt(IDictionary<string, object?> values, string key, out int number)
    {
        number = 0;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }
        return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }
}

public class CrmOrderDetailInventorySyncHook : IEntityHook
{
    public string Name => "crm_orderdetail_inventory_sync";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "orderdetail", StringComparison.OrdinalIgnoreCase))
        {
            return HookResult.Continue();
        }

        if ((ctx.Operation == CrudOperation.Update || ctx.Operation == CrudOperation.Delete) && ctx.Id.HasValue)
        {
            var old = await db.QueryFirstOrDefaultAsync<OrderDetailStockSnapshot>(
                "SELECT ProductId, Quantity FROM OrderDetails WHERE OrderDetailId = @OrderDetailId",
                new { OrderDetailId = ctx.Id.Value }, tx);
            if (old != null)
            {
                ctx.Data["orderdetail.old_product_id"] = old.ProductId;
                ctx.Data["orderdetail.old_quantity"] = old.Quantity;
            }
        }

        return HookResult.Continue();
    }

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "orderdetail", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (ctx.Operation)
        {
            case CrudOperation.Create:
            {
                if (!TryGetInt(ctx.Values, "ProductId", out var productId) ||
                    !TryGetInt(ctx.Values, "Quantity", out var quantity))
                {
                    return;
                }

                await ApplyStockDeltaAsync(db, tx, productId, -quantity);
                break;
            }
            case CrudOperation.Update:
            {
                if (!ctx.Id.HasValue ||
                    !TryGetInt(ctx.Data, "orderdetail.old_product_id", out var oldProductId) ||
                    !TryGetInt(ctx.Data, "orderdetail.old_quantity", out var oldQuantity))
                {
                    return;
                }

                var current = await db.QueryFirstOrDefaultAsync<OrderDetailStockSnapshot>(
                    "SELECT ProductId, Quantity FROM OrderDetails WHERE OrderDetailId = @OrderDetailId",
                    new { OrderDetailId = ctx.Id.Value }, tx);
                if (current == null)
                {
                    return;
                }

                if (current.ProductId == oldProductId)
                {
                    var delta = oldQuantity - current.Quantity;
                    if (delta != 0)
                    {
                        await ApplyStockDeltaAsync(db, tx, current.ProductId, delta);
                    }
                }
                else
                {
                    await ApplyStockDeltaAsync(db, tx, oldProductId, oldQuantity);
                    await ApplyStockDeltaAsync(db, tx, current.ProductId, -current.Quantity);
                }

                break;
            }
            case CrudOperation.Delete:
            {
                if (!TryGetInt(ctx.Data, "orderdetail.old_product_id", out var oldProductId) ||
                    !TryGetInt(ctx.Data, "orderdetail.old_quantity", out var oldQuantity))
                {
                    return;
                }

                await ApplyStockDeltaAsync(db, tx, oldProductId, oldQuantity);
                break;
            }
        }
    }

    private static async Task ApplyStockDeltaAsync(IDbConnection db, IDbTransaction? tx, int productId, int delta)
    {
        if (productId <= 0 || delta == 0)
        {
            return;
        }

        await db.ExecuteAsync(
            "UPDATE Products SET UnitsInStock = UnitsInStock + @Delta WHERE ProductId = @ProductId",
            new { ProductId = productId, Delta = delta }, tx);
    }

    private static bool TryGetInt(IDictionary<string, object?> values, string key, out int number)
    {
        number = 0;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private sealed class OrderDetailStockSnapshot
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }
}

public class CrmCustomerLifecycleGuardHook : IEntityHook
{
    public string Name => "crm_customer_lifecycle_guard";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "customer", StringComparison.OrdinalIgnoreCase))
        {
            return HookResult.Continue();
        }

        var companyName = GetTrimmed(ctx.Values, "CompanyName");
        var contactName = GetTrimmed(ctx.Values, "ContactName");
        if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(contactName))
        {
            return HookResult.Abort("CompanyName / ContactName は必須です。");
        }

        ctx.Values["CompanyName"] = companyName;
        ctx.Values["ContactName"] = contactName;

        var country = GetTrimmed(ctx.Values, "Country");
        if (!string.IsNullOrWhiteSpace(country))
        {
            ctx.Values["Country"] = country;
        }

        var duplicateCount = await db.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
              FROM Customers
              WHERE lower(CompanyName) = lower(@CompanyName)
                AND lower(COALESCE(Country, '')) = lower(COALESCE(@Country, ''))
                AND (@CurrentId IS NULL OR CustomerId <> @CurrentId)",
            new
            {
                CompanyName = companyName,
                Country = country,
                CurrentId = ctx.Operation == CrudOperation.Update ? ctx.Id : null
            }, tx);
        if (duplicateCount > 0)
        {
            return HookResult.Abort("同一 CompanyName/Country の顧客が既に存在します。");
        }

        if (ctx.Operation == CrudOperation.Update && ctx.Id.HasValue && TryGetBool(ctx.Values, "Active", out var active) && !active)
        {
            var openOrders = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Orders WHERE CustomerId = @CustomerId AND Status IN ('Open','Delayed')",
                new { CustomerId = ctx.Id.Value }, tx);
            if (openOrders > 0)
            {
                return HookResult.Abort("Open/Delayed 受注が残っている顧客は非アクティブ化できません。");
            }
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;

    private static string? GetTrimmed(IDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var v) ? v?.ToString()?.Trim() : null;

    private static bool TryGetBool(IDictionary<string, object?> values, string key, out bool value)
    {
        value = false;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        var s = raw.ToString();
        if (string.Equals(s, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (string.Equals(s, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        return bool.TryParse(s, out value);
    }
}

public class CrmCustomerDeleteGuardHook : IEntityHook
{
    public string Name => "crm_customer_delete_guard";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "customer", StringComparison.OrdinalIgnoreCase) ||
            ctx.Operation != CrudOperation.Delete ||
            !ctx.Id.HasValue)
        {
            return HookResult.Continue();
        }

        var hasOrders = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Orders WHERE CustomerId = @CustomerId",
            new { CustomerId = ctx.Id.Value }, tx);
        if (hasOrders > 0)
        {
            return HookResult.Abort("受注履歴のある顧客は削除できません。必要な場合は Active=0 で無効化してください。");
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public class CrmCustomerAuditTrailHook : IEntityHook
{
    public string Name => "crm_customer_audit_trail";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "customer", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var id = ctx.Id?.ToString() ?? "(new)";
        var action = ctx.Operation switch
        {
            CrudOperation.Create => "created",
            CrudOperation.Update => "updated",
            CrudOperation.Delete => "deleted",
            _ => "changed"
        };
        var detail = $"[CRM] Customer {id} {action}.";

        await db.ExecuteAsync(
            "INSERT INTO AuditLog(UserName, Action, Entity, Detail, CreatedAt) VALUES(@UserName,@Action,@Entity,@Detail,@CreatedAt)",
            new
            {
                UserName = ctx.UserName ?? "system",
                Action = "crm_customer_event",
                Entity = "customer",
                Detail = detail,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            }, tx);
    }
}

public class CrmProductInventoryGuardHook : IEntityHook
{
    public string Name => "crm_product_inventory_guard";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "product", StringComparison.OrdinalIgnoreCase))
        {
            return HookResult.Continue();
        }

        if (!TryGetDecimal(ctx.Values, "UnitPrice", out var unitPrice) || unitPrice <= 0)
        {
            return HookResult.Abort("UnitPrice は 0 より大きい値が必要です。");
        }

        if (TryGetInt(ctx.Values, "UnitsInStock", out var stock) && stock < 0)
        {
            return HookResult.Abort("UnitsInStock は 0 以上である必要があります。");
        }

        if (TryGetInt(ctx.Values, "ReorderLevel", out var reorderLevel) && reorderLevel < 0)
        {
            return HookResult.Abort("ReorderLevel は 0 以上である必要があります。");
        }

        if (ctx.Operation == CrudOperation.Update &&
            ctx.Id.HasValue &&
            TryGetBool(ctx.Values, "Discontinued", out var discontinued) &&
            discontinued)
        {
            var activeUsage = await db.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM OrderDetails od
                  JOIN Orders o ON o.OrderId = od.OrderId
                  WHERE od.ProductId = @ProductId
                    AND o.Status IN ('Open','Delayed')",
                new { ProductId = ctx.Id.Value }, tx);

            if (activeUsage > 0)
            {
                return HookResult.Abort("Open/Delayed 受注で使用中の商品は Discontinued にできません。");
            }
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;

    private static bool TryGetInt(IDictionary<string, object?> values, string key, out int number)
    {
        number = 0;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }
        return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryGetDecimal(IDictionary<string, object?> values, string key, out decimal number)
    {
        number = 0;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }
        return decimal.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryGetBool(IDictionary<string, object?> values, string key, out bool value)
    {
        value = false;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        var s = raw.ToString();
        if (string.Equals(s, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (string.Equals(s, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        return bool.TryParse(s, out value);
    }
}

public class CrmProductDeleteGuardHook : IEntityHook
{
    public string Name => "crm_product_delete_guard";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "product", StringComparison.OrdinalIgnoreCase) ||
            ctx.Operation != CrudOperation.Delete ||
            !ctx.Id.HasValue)
        {
            return HookResult.Continue();
        }

        var linkedCount = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM OrderDetails WHERE ProductId = @ProductId",
            new { ProductId = ctx.Id.Value }, tx);
        if (linkedCount > 0)
        {
            return HookResult.Abort("受注明細で利用履歴のある商品は削除できません。Discontinued=1 で販売停止してください。");
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public class CrmProductAuditTrailHook : IEntityHook
{
    public string Name => "crm_product_audit_trail";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!string.Equals(ctx.Entity, "product", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var id = ctx.Id?.ToString() ?? "(new)";
        var action = ctx.Operation switch
        {
            CrudOperation.Create => "created",
            CrudOperation.Update => "updated",
            CrudOperation.Delete => "deleted",
            _ => "changed"
        };
        var detail = $"[CRM] Product {id} {action}.";

        await db.ExecuteAsync(
            "INSERT INTO AuditLog(UserName, Action, Entity, Detail, CreatedAt) VALUES(@UserName,@Action,@Entity,@Detail,@CreatedAt)",
            new
            {
                UserName = ctx.UserName ?? "system",
                Action = "crm_product_event",
                Entity = "product",
                Detail = detail,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            }, tx);
    }
}
