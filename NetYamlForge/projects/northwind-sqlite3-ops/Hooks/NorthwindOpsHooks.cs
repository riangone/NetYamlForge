// ファイル概要: northwind-sqlite3-ops プロジェクト固有のエンティティフック実装です。
// 注文日付の未来日チェック（NorthwindOrderDateGuardHook）等のビジネスルールを定義します。
// YAML の hooks セクションに hook 名を指定することでフックが有効になります。

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.NorthwindSqlite3Ops.Hooks;

public class NorthwindOrderDateGuardHook : IEntityHook
{
    private readonly ILogger<NorthwindOrderDateGuardHook> _logger;

    public string Name => "nw_order_date_guard";

    public NorthwindOrderDateGuardHook(ILogger<NorthwindOrderDateGuardHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var hasOrderDate = TryGetDate(ctx.Values, "OrderDate", out var orderDate);
        var hasRequiredDate = TryGetDate(ctx.Values, "RequiredDate", out var requiredDate);

        if (hasOrderDate && hasRequiredDate && requiredDate < orderDate)
        {
            return Task.FromResult(HookResult.Abort("RequiredDate は OrderDate 以降である必要があります。"));
        }

        if (!ctx.Values.TryGetValue("Status", out var statusObj) || string.IsNullOrWhiteSpace(statusObj?.ToString()))
        {
            ctx.Values["Status"] = "Open";
        }

        if (ctx.Values.TryGetValue("Freight", out var freightObj) && freightObj != null)
        {
            if (!decimal.TryParse(freightObj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var freight))
            {
                return Task.FromResult(HookResult.Abort("Freight の値が不正です。"));
            }

            if (freight < 0 || freight > 5000)
            {
                return Task.FromResult(HookResult.Abort("Freight は 0 以上 5000 以下で入力してください。"));
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }

    private static bool TryGetDate(IDictionary<string, object?> values, string key, out DateTime date)
    {
        date = default;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        if (raw is DateTime dt)
        {
            date = dt.Date;
            return true;
        }

        return DateTime.TryParse(raw.ToString(), out date);
    }
}

public class NorthwindOrderStatusTransitionHook : IEntityHook
{
    private readonly ILogger<NorthwindOrderStatusTransitionHook> _logger;

    public string Name => "nw_order_status_transition";

    public NorthwindOrderStatusTransitionHook(ILogger<NorthwindOrderStatusTransitionHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Update || ctx.Id is null)
        {
            return HookResult.Continue();
        }

        var currentStatus = await db.ExecuteScalarAsync<string?>(
            "SELECT Status FROM Orders WHERE OrderId = @OrderId",
            new { OrderId = ctx.Id.Value }, tx);

        var nextStatus = ctx.Values.TryGetValue("Status", out var statusObj)
            ? statusObj?.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(nextStatus) || string.IsNullOrWhiteSpace(currentStatus))
        {
            return HookResult.Continue();
        }

        var invalid = currentStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
            && !nextStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

        if (invalid)
        {
            return HookResult.Abort("Cancelled の受注は他ステータスへ戻せません。" );
        }

        _logger.LogInformation("[NW-Ops] Order {OrderId} status {Current} -> {Next}", ctx.Id, currentStatus, nextStatus);
        return HookResult.Continue();
    }

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Update || ctx.Id is null)
        {
            return;
        }

        var sql = @"
            INSERT INTO AuditLog(Action, Entity, Detail, UserName, CreatedAt)
            VALUES(@Action, @Entity, @Detail, @UserName, @CreatedAt)";

        await db.ExecuteAsync(sql, new
        {
            Action = "hook",
            Entity = "order",
            Detail = $"[NW-Ops] Order {ctx.Id.Value} status updated by hook",
            UserName = ctx.UserName ?? "system",
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        }, tx);
    }
}

public class NorthwindOrderDetailStockGuardHook : IEntityHook
{
    public string Name => "nw_orderdetail_stock_guard";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!TryGetInt(ctx.Values, "ProductId", out var productId) || !TryGetInt(ctx.Values, "Quantity", out var requestedQty))
        {
            return HookResult.Continue();
        }

        if (requestedQty <= 0)
        {
            return HookResult.Abort("Quantity は 1 以上である必要があります。" );
        }

        var stock = await db.ExecuteScalarAsync<int>(
            "SELECT UnitsInStock FROM Products WHERE ProductId = @ProductId",
            new { ProductId = productId }, tx);

        if (requestedQty > stock)
        {
            return HookResult.Abort($"在庫不足です。要求数量={requestedQty}, 在庫={stock}" );
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }

    private static bool TryGetInt(IDictionary<string, object?> values, string key, out int number)
    {
        number = 0;
        if (!values.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        return int.TryParse(raw.ToString(), out number);
    }
}
