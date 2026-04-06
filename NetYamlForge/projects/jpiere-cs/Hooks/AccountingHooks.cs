using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.JpiereCs.Hooks;

/// <summary>
/// 仕訳番号の自動採番（JNL-YYYYMM-XXXX）
/// </summary>
public class JournalDocumentNoHook : IEntityHook
{
    public string Name => "journal_document_no";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("DocumentNo", out var docNo) &&
            docNo != null && !string.IsNullOrWhiteSpace(docNo.ToString()))
        {
            return HookResult.Continue();
        }

        var now = DateTime.Now;
        var prefix = $"JNL-{now:yyyyMM}-";

        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1
            FROM journals
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
/// 仕訳の借貸均衡チェック（借方合計 = 貸方合計）
/// </summary>
public class JournalBalanceValidationHook : IEntityHook
{
    public string Name => "journal_balance_validation";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("TotalDebit", out var debitObj) ||
            !ctx.Values.TryGetValue("TotalCredit", out var creditObj))
        {
            return Task.FromResult(HookResult.Continue());
        }

        var debit = debitObj != null ? Convert.ToDouble(debitObj) : 0.0;
        var credit = creditObj != null ? Convert.ToDouble(creditObj) : 0.0;

        var tolerance = 1.0;
        var diff = Math.Abs(debit - credit);

        if (diff > tolerance)
        {
            return Task.FromResult(HookResult.Abort(
                $"仕訳が均衡していません: 借方 {debit:N0} ≠ 貸方 {credit:N0} (差額: {diff:N0})"));
        }

        ctx.Values["IsBalanced"] = diff < 0.01 ? 1 : 0;
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// Helper to safely get value from dictionary trying both PascalCase and snake_case
/// </summary>
internal static class DictHelper
{
    public static T? Get<T>(Dictionary<string, object?> dict, string pascalName, string snakeName, T? defaultValue = default)
    {
        if (dict.TryGetValue(pascalName, out var val) && val != null && val != DBNull.Value)
            return (T)Convert.ChangeType(val, typeof(T));
        if (dict.TryGetValue(snakeName, out var val2) && val2 != null && val2 != DBNull.Value)
            return (T)Convert.ChangeType(val2, typeof(T));
        return defaultValue;
    }
    
    public static string? GetStr(Dictionary<string, object?> dict, string pascalName, string snakeName)
    {
        if (dict.TryGetValue(pascalName, out var val) && val != null && val != DBNull.Value)
            return val.ToString();
        if (dict.TryGetValue(snakeName, out var val2) && val2 != null && val2 != DBNull.Value)
            return val2.ToString();
        return null;
    }
}

/// <summary>
/// 請求確定時の仕訳自動起票（Bill.doc_status: DR → CO）
/// </summary>
public class BillCompleteHook : IEntityHook
{
    public string Name => "bill_complete";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("DocStatus", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != "CO") return;
        if (!ctx.Values.TryGetValue("Id", out var idObj) || idObj == null) return;

        var billId = Convert.ToInt32(idObj);

        var bill = await db.QuerySingleAsync<Dictionary<string, object?>>(
            "SELECT * FROM bills WHERE id = @id", new { id = billId }, tx);

        var existingJournal = await db.ExecuteScalarAsync<int?>(
            "SELECT id FROM journals WHERE source_table = 'bills' AND source_id = @billId AND doc_status = 'CO'",
            new { billId }, tx);
        if (existingJournal.HasValue) return;

        var journalNo = await GenerateJournalNoAsync(db, tx);
        var grandTotal = DictHelper.Get<double>(bill, "GrandTotal", "grand_total", 0.0);
        var taxBaseAmt = DictHelper.Get<double>(bill, "TaxBaseAmt", "tax_base_amt", 0.0);
        var taxAmt = DictHelper.Get<double>(bill, "TaxAmt", "tax_amt", 0.0);
        var dateBilled = DictHelper.GetStr(bill, "DateBilled", "date_billed") ?? DateTime.Now.ToString("yyyy-MM-dd");
        var documentNo = DictHelper.GetStr(bill, "DocumentNo", "document_no") ?? string.Empty;

        var journalId = await db.ExecuteScalarAsync<long>(@"
            INSERT INTO journals (document_no, doc_status, journal_type, date_acct, description, source_table, source_id, total_debit, total_credit, is_balanced)
            VALUES (@no, 'CO', 'AR', @dateAcct, @desc, 'bills', @billId, @grandTotal, @grandTotal, 1);
            SELECT last_insert_rowid()",
            new { no = journalNo, dateAcct = dateBilled, desc = $"請求確定: {documentNo}", billId, grandTotal }, tx);

        var arAccountId = await GetAccountIdAsync(db, tx, "1100");
        await InsertJournalLineAsync(db, tx, journalId, 10, arAccountId, grandTotal, 0, "売掛金計上");

        var salesAccountId = await GetAccountIdAsync(db, tx, "4100");
        await InsertJournalLineAsync(db, tx, journalId, 20, salesAccountId, 0, taxBaseAmt, "売上計上");

        var taxAccountId = await GetAccountIdAsync(db, tx, "2400");
        await InsertJournalLineAsync(db, tx, journalId, 30, taxAccountId, 0, taxAmt, "消費税計上");
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    private async Task<string> GenerateJournalNoAsync(IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now;
        var prefix = $"JNL-{now:yyyyMM}-";
        const string sql = "SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1 FROM journals WHERE document_no LIKE @prefix";
        var nextSeq = await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
        return $"{prefix}{nextSeq:D4}";
    }

    private async Task<int> GetAccountIdAsync(IDbConnection db, IDbTransaction? tx, string code)
    {
        var id = await db.ExecuteScalarAsync<int?>("SELECT id FROM accounts WHERE code = @code AND is_active = 1", new { code }, tx);
        if (!id.HasValue) throw new InvalidOperationException($"勘定科目コード {code} が見つかりません");
        return id.Value;
    }

    private async Task InsertJournalLineAsync(IDbConnection db, IDbTransaction? tx, long journalId, int lineNo, int accountId, double debit, double credit, string desc)
    {
        await db.ExecuteAsync("INSERT INTO journal_lines (journal_id, line_no, account_id, debit_amt, credit_amt, description) VALUES (@journalId, @lineNo, @accountId, @debit, @credit, @desc)",
            new { journalId, lineNo, accountId, debit, credit, desc }, tx);
    }
}

/// <summary>
/// 請求取消時の逆仕訳起票
/// </summary>
public class BillReverseHook : IEntityHook
{
    public string Name => "bill_reverse";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("DocStatus", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != "RE") return;
        if (!ctx.Values.TryGetValue("Id", out var idObj) || idObj == null) return;

        var billId = Convert.ToInt32(idObj);
        var originalJournal = await db.QueryFirstOrDefaultAsync<Dictionary<string, object?>>(
            "SELECT * FROM journals WHERE source_table = 'bills' AND source_id = @billId AND doc_status = 'CO'", new { billId }, tx);
        if (originalJournal == null) return;

        var originalJournalId = Convert.ToInt32(originalJournal["id"]);
        var existingReverse = await db.ExecuteScalarAsync<int?>(
            "SELECT id FROM journals WHERE source_table = 'bills' AND source_id = @billId AND doc_status = 'RE'", new { billId }, tx);
        if (existingReverse.HasValue) return;

        var journalNo = await GenerateJournalNoAsync(db, tx);
        var grandTotal = DictHelper.Get<double>(originalJournal, "TotalDebit", "total_debit", 0.0);
        var dateAcct = DictHelper.GetStr(originalJournal, "DateAcct", "date_acct") ?? DateTime.Now.ToString("yyyy-MM-dd");
        var originalDocNo = DictHelper.GetStr(originalJournal, "DocumentNo", "document_no") ?? string.Empty;

        var journalId = await db.ExecuteScalarAsync<long>(@"
            INSERT INTO journals (document_no, doc_status, journal_type, date_acct, description, source_table, source_id, total_debit, total_credit, is_balanced)
            VALUES (@no, 'RE', 'AR', @dateAcct, @desc, 'bills', @billId, @grandTotal, @grandTotal, 1);
            SELECT last_insert_rowid()",
            new { no = journalNo, dateAcct, desc = $"請求取消: {originalDocNo}", billId, grandTotal }, tx);

        var originalLines = await db.QueryAsync<Dictionary<string, object?>>(
            "SELECT * FROM journal_lines WHERE journal_id = @journalId ORDER BY line_no", new { journalId = originalJournalId }, tx);

        var lineNo = 10;
        foreach (var line in originalLines)
        {
            var accountId = Convert.ToInt32(line["account_id"]);
            var debitAmt = Convert.ToDouble(line["debit_amt"]);
            var creditAmt = Convert.ToDouble(line["credit_amt"]);
            var desc = line["description"]?.ToString() ?? string.Empty;
            await db.ExecuteAsync("INSERT INTO journal_lines (journal_id, line_no, account_id, debit_amt, credit_amt, description) VALUES (@journalId, @lineNo, @accountId, @debit, @credit, @desc)",
                new { journalId, lineNo, accountId, debit = creditAmt, credit = debitAmt, desc = $"取消: {desc}" }, tx);
            lineNo += 10;
        }
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    private async Task<string> GenerateJournalNoAsync(IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now;
        var prefix = $"JNL-{now:yyyyMM}-";
        const string sql = "SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1 FROM journals WHERE document_no LIKE @prefix";
        var nextSeq = await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
        return $"{prefix}{nextSeq:D4}";
    }
}

/// <summary>
/// 売上認識確定時の仕訳自動起票
/// </summary>
public class RecognitionCompleteHook : IEntityHook
{
    public string Name => "recognition_complete";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("DocStatus", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != "CO") return;
        if (!ctx.Values.TryGetValue("Id", out var idObj) || idObj == null) return;

        var recognitionId = Convert.ToInt32(idObj);
        var recognition = await db.QuerySingleAsync<Dictionary<string, object?>>(
            "SELECT * FROM recognitions WHERE id = @id", new { id = recognitionId }, tx);

        var existingJournal = await db.ExecuteScalarAsync<int?>(
            "SELECT id FROM journals WHERE source_table = 'recognitions' AND source_id = @recognitionId AND doc_status = 'CO'", new { recognitionId }, tx);
        if (existingJournal.HasValue) return;

        var journalNo = await GenerateJournalNoAsync(db, tx);
        var grandTotal = DictHelper.Get<double>(recognition, "GrandTotal", "grand_total", 0.0);
        var dateAcct = DictHelper.GetStr(recognition, "DateAcct", "date_acct") ?? DateTime.Now.ToString("yyyy-MM-dd");
        var documentNo = DictHelper.GetStr(recognition, "DocumentNo", "document_no") ?? string.Empty;

        var journalId = await db.ExecuteScalarAsync<long>(@"
            INSERT INTO journals (document_no, doc_status, journal_type, date_acct, description, source_table, source_id, total_debit, total_credit, is_balanced)
            VALUES (@no, 'CO', 'AR', @dateAcct, @desc, 'recognitions', @recognitionId, @grandTotal, @grandTotal, 1);
            SELECT last_insert_rowid()",
            new { no = journalNo, dateAcct, desc = $"売上認識確定: {documentNo}", recognitionId, grandTotal }, tx);

        var arAccountId = await GetAccountIdAsync(db, tx, "1100");
        await InsertJournalLineAsync(db, tx, journalId, 10, arAccountId, grandTotal, 0, "売掛金計上");

        var salesAccountId = await GetAccountIdAsync(db, tx, "4100");
        await InsertJournalLineAsync(db, tx, journalId, 20, salesAccountId, 0, grandTotal, "サービス売上計上");
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    private async Task<string> GenerateJournalNoAsync(IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now;
        var prefix = $"JNL-{now:yyyyMM}-";
        const string sql = "SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1 FROM journals WHERE document_no LIKE @prefix";
        var nextSeq = await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
        return $"{prefix}{nextSeq:D4}";
    }

    private async Task<int> GetAccountIdAsync(IDbConnection db, IDbTransaction? tx, string code)
    {
        var id = await db.ExecuteScalarAsync<int?>("SELECT id FROM accounts WHERE code = @code AND is_active = 1", new { code }, tx);
        if (!id.HasValue) throw new InvalidOperationException($"勘定科目コード {code} が見つかりません");
        return id.Value;
    }

    private async Task InsertJournalLineAsync(IDbConnection db, IDbTransaction? tx, long journalId, int lineNo, int accountId, double debit, double credit, string desc)
    {
        await db.ExecuteAsync("INSERT INTO journal_lines (journal_id, line_no, account_id, debit_amt, credit_amt, description) VALUES (@journalId, @lineNo, @accountId, @debit, @credit, @desc)",
            new { journalId, lineNo, accountId, debit, credit, desc }, tx);
    }
}
