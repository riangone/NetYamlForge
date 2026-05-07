using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.JpiereCs.Hooks;

/// <summary>
/// 受入確定時の処理（在庫移動＋発注書更新）
/// purchase_receipts.doc_status: DR → CO
/// 1. purchase_order_lines.qty_received を加算
/// 2. 在庫品（product_type='I'）の場合、stock_moves に IN レコードを挿入
/// 3. 全明細受入完了の場合、purchase_orders.doc_status = 'CO'
/// </summary>
public class PurchaseReceiptCompleteHook : IEntityHook
{
    public string Name => "purchase_receipt_complete";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("DocStatus", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != "CO") return;

        if (!ctx.Values.TryGetValue("Id", out var idObj) || idObj == null) return;

        var receiptId = Convert.ToInt32(idObj);

        // 受入データを取得
        var receipt = await db.QuerySingleAsync<Dictionary<string, object?>>(
            "SELECT * FROM purchase_receipts WHERE id = @id", new { id = receiptId }, tx);

        var purchaseOrderId = DictHelper.Get<int>(receipt, "PurchaseOrderId", "purchase_order_id", 0);
        if (purchaseOrderId == 0) return;

        // 受入明細を取得
        var receiptLines = await db.QueryAsync<Dictionary<string, object?>>(
            "SELECT * FROM purchase_receipt_lines WHERE ReceiptId = @receiptId",
            new { receiptId }, tx);

        foreach (var line in receiptLines)
        {
            var poLineId = DictHelper.Get<int>(line, "PoLineId", "po_line_id", 0);
            var productId = DictHelper.Get<int>(line, "ProductId", "product_id", 0);
            var qtyReceived = DictHelper.Get<double>(line, "QtyReceived", "qty_received", 0.0);
            var unitCost = DictHelper.Get<double>(line, "UnitCost", "unit_cost", 0.0);

            // 発注明細の qty_received を更新
            if (poLineId > 0)
            {
                await db.ExecuteAsync(@"
                    UPDATE purchase_order_lines
                    SET QtyReceived = QtyReceived + @qty
                    WHERE id = @poLineId",
                    new { qty = qtyReceived, poLineId }, tx);
            }

            // 在庫品の場合、stock_moves に記録
            if (productId > 0)
            {
                var product = await db.QueryFirstOrDefaultAsync<Dictionary<string, object?>>(
                    "SELECT ProductType FROM products WHERE id = @id", new { id = productId }, tx);

                var productType = DictHelper.GetStr(product, "ProductType", "product_type") ?? string.Empty;

                if (productType == "I")
                {
                    var dateMoved = DictHelper.GetStr(receipt, "DateReceived", "date_received") ?? DateTime.Now.ToString("yyyy-MM-dd");

                    await db.ExecuteAsync(@"
                        INSERT INTO stock_moves (move_type, product_id, qty, unit_cost, date_moved, source_table, source_id, description)
                        VALUES ('IN', @productId, @qty, @unitCost, @dateMoved, 'purchase_receipts', @receiptId, @desc)",
                        new { productId, qty = qtyReceived, unitCost, dateMoved, receiptId, desc = $"受入: {receiptId}" }, tx);
                }
            }
        }

        // 全明細受入完了チェック
        var allLinesReceived = await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*)
            FROM purchase_order_lines
            WHERE PurchaseOrderId = @poId
              AND QtyReceived < QtyOrdered",
            new { poId = purchaseOrderId }, tx);

        if (allLinesReceived == 0)
        {
            // 全明細受入完了 → 発注書を完了状態に
            await db.ExecuteAsync(@"
                UPDATE purchase_orders
                SET doc_status = 'CO'
                WHERE id = @poId AND doc_status = 'IP'",
                new { poId = purchaseOrderId }, tx);
        }
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());
}

/// <summary>
/// 仕入請求確定時の仕訳起票（APInvoice.doc_status: DR → CO）
/// 借方: 仕入高 (5100) = tax_base_amt
/// 借方: 仮払消費税 (2410) = tax_amt
/// 貸方: 買掛金 (2100) = grand_total
/// </summary>
public class APInvoiceCompleteHook : IEntityHook
{
    public string Name => "ap_invoice_complete";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("DocStatus", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != "CO") return;

        if (!ctx.Values.TryGetValue("Id", out var idObj) || idObj == null) return;

        var invoiceId = Convert.ToInt32(idObj);

        // 仕入請求データを取得
        var invoice = await db.QuerySingleAsync<Dictionary<string, object?>>(
            "SELECT * FROM ap_invoices WHERE id = @id", new { id = invoiceId }, tx);

        var existingJournal = await db.ExecuteScalarAsync<int?>(
            "SELECT id FROM journals WHERE source_table = 'ap_invoices' AND source_id = @invoiceId AND doc_status = 'CO'",
            new { invoiceId }, tx);

        if (existingJournal.HasValue) return;

        var journalNo = await GenerateJournalNoAsync(db, tx);

        var grandTotal = DictHelper.Get<double>(invoice, "GrandTotal", "grand_total", 0.0);
        var taxBaseAmt = DictHelper.Get<double>(invoice, "TaxBaseAmt", "tax_base_amt", 0.0);
        var taxAmt = DictHelper.Get<double>(invoice, "TaxAmt", "tax_amt", 0.0);
        var dateInvoiced = DictHelper.GetStr(invoice, "DateInvoiced", "date_invoiced") ?? DateTime.Now.ToString("yyyy-MM-dd");
        var documentNo = DictHelper.GetStr(invoice, "DocumentNo", "document_no") ?? string.Empty;

        // journals ヘッダ挿入
        var journalId = await db.ExecuteScalarAsync<long>(@"
            INSERT INTO journals (document_no, doc_status, journal_type, date_acct,
                                  description, source_table, source_id,
                                  total_debit, total_credit, is_balanced)
            VALUES (@no, 'CO', 'AP', @dateAcct, @desc, 'ap_invoices', @invoiceId,
                    @grandTotal, @grandTotal, 1);
            SELECT last_insert_rowid()",
            new
            {
                no = journalNo,
                dateAcct = dateInvoiced,
                desc = $"仕入請求確定: {documentNo}",
                invoiceId,
                grandTotal
            }, tx);

        // 明細1: 仕入高 (DR) - 5100
        var purchaseAccountId = await GetAccountIdAsync(db, tx, "5100");
        await InsertJournalLineAsync(db, tx, journalId, 10, purchaseAccountId,
            debit: taxBaseAmt, credit: 0, desc: "仕入計上");

        // 明細2: 仮払消費税 (DR) - 2410
        var taxPaidAccountId = await GetAccountIdAsync(db, tx, "2410");
        await InsertJournalLineAsync(db, tx, journalId, 20, taxPaidAccountId,
            debit: taxAmt, credit: 0, desc: "仮払消費税計上");

        // 明細3: 買掛金 (CR) - 2100
        var apAccountId = await GetAccountIdAsync(db, tx, "2100");
        await InsertJournalLineAsync(db, tx, journalId, 30, apAccountId,
            debit: 0, credit: grandTotal, desc: "買掛金計上");
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    private async Task<string> GenerateJournalNoAsync(IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now;
        var prefix = $"JNL-{now:yyyyMM}-";

        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1
            FROM journals
            WHERE document_no LIKE @prefix";

        var nextSeq = await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
        return $"{prefix}{nextSeq:D4}";
    }

    private async Task<int> GetAccountIdAsync(IDbConnection db, IDbTransaction? tx, string code)
    {
        var id = await db.ExecuteScalarAsync<int?>(
            "SELECT id FROM accounts WHERE code = @code AND is_active = 1", new { code }, tx);

        if (!id.HasValue)
        {
            throw new InvalidOperationException($"勘定科目コード {code} が見つかりません");
        }

        return id.Value;
    }

    private async Task InsertJournalLineAsync(IDbConnection db, IDbTransaction? tx, long journalId,
        int lineNo, int accountId, double debit, double credit, string desc)
    {
        await db.ExecuteAsync(@"
            INSERT INTO journal_lines (journal_id, line_no, account_id, debit_amt, credit_amt, description)
            VALUES (@journalId, @lineNo, @accountId, @debit, @credit, @desc)",
            new { journalId, lineNo, accountId, debit, credit, desc }, tx);
    }
}

/// <summary>
/// 支払確定時の処理（Payment.doc_status: DR → CO）
/// AR入金: bills.pay_amt 加算、outstanding_amt 再計算
/// AP支払: ap_invoices.pay_amt 加算、outstanding_amt 再計算
/// 仕訳起票:
///   AR: 現金(1900)(DR) / 売掛金(1100)(CR)
///   AP: 買掛金(2100)(DR) / 現金(1900)(CR)
/// </summary>
public class PaymentCompleteHook : IEntityHook
{
    public string Name => "payment_complete";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("DocStatus", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != "CO") return;

        if (!ctx.Values.TryGetValue("Id", out var idObj) || idObj == null) return;

        var paymentId = Convert.ToInt32(idObj);

        // 支払データを取得
        var payment = await db.QuerySingleAsync<Dictionary<string, object?>>(
            "SELECT * FROM payments WHERE id = @id", new { id = paymentId }, tx);

        var paymentType = DictHelper.GetStr(payment, "PaymentType", "payment_type");
        if (string.IsNullOrEmpty(paymentType)) return;

        var payAmt = DictHelper.Get<double>(payment, "PayAmt", "pay_amt", 0.0);
        var paymentDate = DictHelper.GetStr(payment, "PaymentDate", "payment_date") ?? DateTime.Now.ToString("yyyy-MM-dd");
        var documentNo = DictHelper.GetStr(payment, "DocumentNo", "document_no") ?? string.Empty;

        // 仕訳起票
        var journalNo = await GenerateJournalNoAsync(db, tx);
        var journalId = await db.ExecuteScalarAsync<long>(@"
            INSERT INTO journals (document_no, doc_status, journal_type, date_acct,
                                  description, source_table, source_id,
                                  total_debit, total_credit, is_balanced)
            VALUES (@no, 'CO', @jnlType, @dateAcct, @desc, 'payments', @paymentId,
                    @payAmt, @payAmt, 1);
            SELECT last_insert_rowid()",
            new
            {
                no = journalNo,
                jnlType = paymentType,
                dateAcct = paymentDate,
                desc = $"{(paymentType == "AR" ? "入金" : "支払")}: {documentNo}",
                paymentId,
                payAmt
            }, tx);

        if (paymentType == "AR")
        {
            // AR入金: 現金(1900)(DR) / 売掛金(1100)(CR)
            var cashAccountId = await GetAccountIdAsync(db, tx, "1900");
            await InsertJournalLineAsync(db, tx, journalId, 10, cashAccountId,
                debit: payAmt, credit: 0, desc: "入金計上");

            var arAccountId = await GetAccountIdAsync(db, tx, "1100");
            await InsertJournalLineAsync(db, tx, journalId, 20, arAccountId,
                debit: 0, credit: payAmt, desc: "売掛金回収");

            // bills.pay_amt を加算
            var billId = DictHelper.Get<int>(payment, "BillId", "bill_id", 0);
            if (billId > 0)
            {
                await db.ExecuteAsync(@"
                    UPDATE bills
                    SET PayAmt = PayAmt + @payAmt,
                        OutstandingAmt = MAX(0, GrandTotal - (PayAmt + @payAmt)),
                        DocStatus = CASE WHEN MAX(0, GrandTotal - (PayAmt + @payAmt)) = 0 THEN 'CL' ELSE DocStatus END
                    WHERE id = @billId",
                    new { payAmt, billId }, tx);
            }
        }
        else if (paymentType == "AP")
        {
            // AP支払: 買掛金(2100)(DR) / 現金(1900)(CR)
            var apAccountId = await GetAccountIdAsync(db, tx, "2100");
            await InsertJournalLineAsync(db, tx, journalId, 10, apAccountId,
                debit: payAmt, credit: 0, desc: "買掛金支払");

            var cashAccountId = await GetAccountIdAsync(db, tx, "1900");
            await InsertJournalLineAsync(db, tx, journalId, 20, cashAccountId,
                debit: 0, credit: payAmt, desc: "現金支払");

            // ap_invoices.pay_amt を加算
            var apInvoiceId = DictHelper.Get<int>(payment, "ApInvoiceId", "ap_invoice_id", 0);
            if (apInvoiceId > 0)
            {
                await db.ExecuteAsync(@"
                    UPDATE ap_invoices
                    SET PayAmt = PayAmt + @payAmt,
                        OutstandingAmt = MAX(0, GrandTotal - (PayAmt + @payAmt))
                    WHERE id = @apInvoiceId",
                    new { payAmt, apInvoiceId }, tx);
            }
        }
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    private async Task<string> GenerateJournalNoAsync(IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now;
        var prefix = $"JNL-{now:yyyyMM}-";

        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1
            FROM journals
            WHERE document_no LIKE @prefix";

        var nextSeq = await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
        return $"{prefix}{nextSeq:D4}";
    }

    private async Task<int> GetAccountIdAsync(IDbConnection db, IDbTransaction? tx, string code)
    {
        var id = await db.ExecuteScalarAsync<int?>(
            "SELECT id FROM accounts WHERE code = @code AND is_active = 1", new { code }, tx);

        if (!id.HasValue)
        {
            throw new InvalidOperationException($"勘定科目コード {code} が見つかりません");
        }

        return id.Value;
    }

    private async Task InsertJournalLineAsync(IDbConnection db, IDbTransaction? tx, long journalId,
        int lineNo, int accountId, double debit, double credit, string desc)
    {
        await db.ExecuteAsync(@"
            INSERT INTO journal_lines (journal_id, line_no, account_id, debit_amt, credit_amt, description)
            VALUES (@journalId, @lineNo, @accountId, @debit, @credit, @desc)",
            new { journalId, lineNo, accountId, debit, credit, desc }, tx);
    }
}
