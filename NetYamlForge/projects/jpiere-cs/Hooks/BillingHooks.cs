using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.JpiereCs.Hooks;

/// <summary>
/// 請求番号の自動採番（BILL-YYYYMM-XXXX）
/// </summary>
public class BillDocumentNoHook : IEntityHook
{
    public string Name => "bill_document_no";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("DocumentNo", out var docNo) &&
            docNo != null && !string.IsNullOrWhiteSpace(docNo.ToString()))
        {
            return HookResult.Continue();
        }

        var now = DateTime.Now;
        var prefix = $"BILL-{now:yyyyMM}-";

        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 13) AS INTEGER)), 0) + 1
            FROM bills
            WHERE document_no LIKE @prefix";

        var nextSeq = await db.ExecuteScalarAsync<int>(
            sql,
            new { prefix },
            tx);

        ctx.Values["DocumentNo"] = $"{prefix}{nextSeq:D4}";
        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 支払期限の自動計算（請求日 + payment_term_days）
/// </summary>
public class BillDueDateHook : IEntityHook
{
    public string Name => "bill_due_date";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 請求日を取得
        if (!ctx.Values.TryGetValue("DateBilled", out var dateBilledObj) ||
            dateBilledObj == null)
        {
            return Task.FromResult(HookResult.Continue());
        }

        // 支払期日（日数）を取得
        int termDays = 30; // デフォルト
        if (ctx.Values.TryGetValue("PaymentTermDays", out var termDaysObj) &&
            termDaysObj != null && int.TryParse(termDaysObj.ToString(), out var parsed))
        {
            termDays = parsed;
        }

        // 請求日をパース
        if (DateTime.TryParse(dateBilledObj.ToString(), out var billedDate))
        {
            var dueDate = billedDate.AddDays(termDays);
            ctx.Values["DateDue"] = dueDate.ToString("yyyy-MM-dd");
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 残高（grand_total - pay_amt）の更新
/// </summary>
public class BillOutstandingHook : IEntityHook
{
    public string Name => "bill_outstanding";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        double grandTotal = 0;
        double payAmt = 0;

        if (ctx.Values.TryGetValue("GrandTotal", out var gtObj) &&
            gtObj != null && double.TryParse(gtObj.ToString(), out var gt))
        {
            grandTotal = gt;
        }

        if (ctx.Values.TryGetValue("PayAmt", out var paObj) &&
            paObj != null && double.TryParse(paObj.ToString(), out var pa))
        {
            payAmt = pa;
        }

        var outstanding = Math.Max(0, grandTotal - payAmt);
        ctx.Values["OutstandingAmt"] = outstanding;

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
