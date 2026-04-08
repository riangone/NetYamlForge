using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.JpiereCs.Hooks;

/// <summary>
/// 見積番号の自動採番（EST-YYYYMM-XXXX）
/// </summary>
public class EstimationDocumentNoHook : IEntityHook
{
    public string Name => "estimation_document_no";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("DocumentNo", out var docNo) &&
            docNo != null && !string.IsNullOrWhiteSpace(docNo.ToString()))
        {
            return HookResult.Continue();
        }

        var now = DateTime.Now;
        var prefix = $"EST-{now:yyyyMM}-";

        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1
            FROM estimations
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
/// 見積明細から合計・税額を再計算
/// </summary>
public class EstimationTotalHook : IEntityHook
{
    public string Name => "estimation_total";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 見積IDを取得（更新時のみ）
        int? estimationId = null;
        if (ctx.Values.TryGetValue("Id", out var idObj) && idObj != null)
        {
            estimationId = Convert.ToInt32(idObj);
        }

        // 明細IDからestimation_idを取得（明細作成時）
        if (!estimationId.HasValue && ctx.Values.TryGetValue("EstimationId", out var estIdObj) && estIdObj != null)
        {
            estimationId = Convert.ToInt32(estIdObj);
        }

        if (!estimationId.HasValue)
        {
            return HookResult.Continue();
        }

        const string sql = @"
            SELECT
                COALESCE(SUM(line_amt), 0) AS subtotal,
                COALESCE(SUM(tax_amt), 0) AS tax_total
            FROM estimation_lines
            WHERE estimation_id = @estId AND is_active = 1";

        var result = await db.QuerySingleAsync<(double subtotal, double taxTotal)>(
            sql,
            new { estId = estimationId.Value },
            tx);

        var grandTotal = result.subtotal + result.taxTotal;

        // 合計をヘッダに反映
        ctx.Values["TotalLines"] = result.subtotal;
        ctx.Values["TaxBaseAmt"] = result.subtotal;
        ctx.Values["TaxAmt"] = result.taxTotal;
        ctx.Values["GrandTotal"] = grandTotal;

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 見積の受注確定時に契約レコードを自動生成
/// </summary>
public class EstimationToContractHook : IEntityHook
{
    public string Name => "estimation_to_contract";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // doc_status = CO (受注確定) の場合のみ実行
        if (!ctx.Values.TryGetValue("DocStatus", out var docStatusObj))
        {
            return;
        }

        var docStatus = docStatusObj?.ToString() ?? string.Empty;
        if (docStatus != "CO")
        {
            return;
        }

        var estimationId = Convert.ToInt32(ctx.Values["Id"]);

        // すでに連携契約がある場合はスキップ
        if (ctx.Values.TryGetValue("LinkedContractId", out var linkedId) &&
            linkedId != null && Convert.ToInt32(linkedId) > 0)
        {
            return;
        }

        // 見積データを取得
        const string selectEst = "SELECT * FROM estimations WHERE id = @id";
        var est = await db.QuerySingleAsync<dynamic>(
            selectEst,
            new { id = estimationId },
            tx);

        // 契約番号を採番
        var now = DateTime.Now;
        var prefix = $"CON-{now:yyyyMM}-";
        const string seqSql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1
            FROM contracts
            WHERE document_no LIKE @prefix";
        var nextSeq = await db.ExecuteScalarAsync<int>(seqSql, new { prefix }, tx);
        var contractDocNo = $"{prefix}{nextSeq:D4}";

        // 契約レコードを挿入
        const string insertContract = @"
            INSERT INTO contracts (
                document_no, name, contract_type, doc_status, contract_status,
                business_partner_id, sales_rep, date_acct,
                period_date_from, period_date_to,
                currency, description, is_active, created_at, updated_at
            ) VALUES (
                @documentNo, @name, 'SAL', 'DR', 'WP',
                @businessPartnerId, @salesRep, @dateAcct,
                @datePromised, NULL,
                'JPY', @description, 1, @now, @now
            );
            SELECT last_insert_rowid();";

        var contractId = await db.ExecuteScalarAsync<int>(
            insertContract,
            new
            {
                documentNo = contractDocNo,
                name = $"見積 {est.document_no} より受注",
                businessPartnerId = (int)est.business_partner_id,
                salesRep = (string?)est.sales_rep,
                dateAcct = now.ToString("yyyy-MM-dd"),
                datePromised = (string?)est.date_promised,
                description = $"見積番号: {est.document_no} より自動生成",
                now = now.ToString("yyyy-MM-dd HH:mm:ss")
            },
            tx);

        // 見積のlinked_contract_idを更新
        const string updateEst = @"
            UPDATE estimations
            SET linked_contract_id = @contractId, updated_at = @now
            WHERE id = @estId";

        await db.ExecuteAsync(updateEst, new
        {
            contractId,
            now = now.ToString("yyyy-MM-dd HH:mm:ss"),
            estId = estimationId
        }, tx);

        // 見積明細を契約明細にコピー
        const string selectLines = "SELECT * FROM estimation_lines WHERE estimation_id = @estId AND is_active = 1 ORDER BY line_no";
        var lines = await db.QueryAsync<dynamic>(selectLines, new { estId = estimationId }, tx);

        var lineNo = 10;
        foreach (var line in lines)
        {
            const string insertLine = @"
                INSERT INTO contract_lines (
                    contract_id, line_no, product_id, description,
                    qty, uom, unit_price, line_amt, tax_rate, tax_amt,
                    billing_policy, billing_start_date, billing_end_date,
                    is_active, created_at, updated_at
                ) VALUES (
                    @contractId, @lineNo, @productId, @description,
                    @qty, @uom, @unitPrice, @lineAmt, @taxRate, @taxAmt,
                    'M', @startDate, NULL,
                    1, @now, @now
                )";

            await db.ExecuteAsync(insertLine, new
            {
                contractId,
                lineNo,
                productId = (int?)line.product_id,
                description = (string?)line.description,
                qty = (double)line.qty_ordered,
                uom = (string?)line.uom ?? "EA",
                unitPrice = (double)line.unit_price,
                lineAmt = (double)line.line_amt,
                taxRate = (double?)line.tax_rate ?? 0.10,
                taxAmt = (double?)line.tax_amt ?? 0,
                startDate = (string?)line.date_ordered,
                now = now.ToString("yyyy-MM-dd HH:mm:ss")
            }, tx);

            lineNo += 10;
        }

        // 契約の合計金額を更新
        const string updateContractAmt = @"
            UPDATE contracts
            SET total_doc_amt = (SELECT COALESCE(SUM(line_amt), 0) FROM contract_lines WHERE contract_id = @id),
                monthly_revenue_amt = (SELECT COALESCE(SUM(line_amt), 0) FROM contract_lines WHERE contract_id = @id),
                updated_at = @now
            WHERE id = @id";

        await db.ExecuteAsync(updateContractAmt, new
        {
            id = contractId,
            now = now.ToString("yyyy-MM-dd HH:mm:ss")
        }, tx);
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());
}
