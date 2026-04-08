using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.JpiereCs.Hooks;

/// <summary>
/// 契約番号の自動採番（CON-YYYYMM-XXXX）
/// </summary>
public class ContractDocumentNoHook : IEntityHook
{
    public string Name => "contract_document_no";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("DocumentNo", out var docNo) &&
            docNo != null && !string.IsNullOrWhiteSpace(docNo.ToString()))
        {
            // すでに設定されている場合はスキップ
            return HookResult.Continue();
        }

        var now = DateTime.Now;
        var prefix = $"CON-{now:yyyyMM}-";

        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1
            FROM contracts
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
/// 契約明細から合計金額を再計算
/// </summary>
public class ContractAmountCalculateHook : IEntityHook
{
    public string Name => "contract_amount_calculate";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("Id", out var idObj) || idObj == null)
        {
            // 新規作成時はIDがないのでスキップ
            return HookResult.Continue();
        }

        var contractId = Convert.ToInt32(idObj);

        const string sql = @"
            SELECT
                COALESCE(SUM(line_amt), 0) AS total,
                COALESCE(SUM(tax_amt), 0) AS tax_total
            FROM contract_lines
            WHERE contract_id = @contractId AND is_active = 1";

        var result = await db.QuerySingleAsync<(double total, double taxTotal)>(
            sql,
            new { contractId },
            tx);

        ctx.Values["TotalDocAmt"] = result.total;
        ctx.Values["MonthlyRevenueAmt"] = result.total;

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 契約ステータス遷移の妥当性チェック
/// </summary>
public class ContractStatusHook : IEntityHook
{
    // 許可された遷移: From -> [To]
    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new()
    {
        { "DR", new() { "IN", "VO" } },        // 下書き → 進行中 or 無効
        { "IN", new() { "CO", "VO" } },        // 進行中 → 確定 or 無効
        { "CO", new() { "CL" } },              // 確定 → 解約
        { "CL", new() { } },                    // 解約 → なし
        { "VO", new() { "DR" } },              // 無効 → 下書き（再開）
    };

    public string Name => "contract_status";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Update)
        {
            return Task.FromResult(HookResult.Continue());
        }

        if (!ctx.Values.TryGetValue("ContractStatus", out var newStatusObj) ||
            !ctx.Values.TryGetValue("DocStatus", out var docStatusObj))
        {
            return Task.FromResult(HookResult.Continue());
        }

        var newStatus = newStatusObj?.ToString() ?? string.Empty;
        var docStatus = docStatusObj?.ToString() ?? string.Empty;

        // 現在のステータスをDBから取得
        if (!ctx.Values.TryGetValue("Id", out var idObj) || idObj == null)
        {
            return Task.FromResult(HookResult.Continue());
        }

        var contractId = Convert.ToInt32(idObj);

        // 元のdoc_statusがValuesにない場合はDBから取得
        string? oldDocStatus = null;
        if (ctx.Data.TryGetValue("__originalDocStatus", out var origStatus))
        {
            oldDocStatus = origStatus?.ToString();
        }

        if (string.IsNullOrEmpty(oldDocStatus))
        {
            // 元のstatusはctxから取得できない場合、チェックをスキップ
            return Task.FromResult(HookResult.Continue());
        }

        if (AllowedTransitions.TryGetValue(oldDocStatus, out var allowedTargets))
        {
            if (!allowedTargets.Contains(newStatus) && !allowedTargets.Contains(docStatus))
            {
                return Task.FromResult(HookResult.Abort(
                    $"ステータス遷移が許可されていません: {oldDocStatus} → {docStatus}"));
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 契約期限切れ警告フラグのセット（AfterRead相当のチェック）
/// 更新時に期限切れステータスを自動設定
/// </summary>
public class ContractExpiryCheckHook : IEntityHook
{
    public string Name => "contract_expiry_check";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 作成・更新時に期限切れチェック
        if (!ctx.Values.TryGetValue("PeriodDateTo", out var periodToDate) ||
            periodToDate == null || string.IsNullOrWhiteSpace(periodToDate.ToString()))
        {
            return Task.FromResult(HookResult.Continue());
        }

        if (DateTime.TryParse(periodToDate.ToString(), out var endDate))
        {
            if (endDate < DateTime.Today && ctx.Values.TryGetValue("ContractStatus", out var status))
            {
                var statusStr = status?.ToString() ?? string.Empty;
                if (statusStr == "AC")
                {
                    // 有効なのに終了日過去 → 期限切れに
                    ctx.Values["ContractStatus"] = "EX";
                }
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
