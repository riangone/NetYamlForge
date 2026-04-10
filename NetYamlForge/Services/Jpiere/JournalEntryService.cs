using System.Data;
using Dapper;

namespace NetYamlForge.Services.Jpiere;

/// <summary>
/// 仕訳エントリー関連の共通操作を提供するサービス。
/// 各Hookから重複コードを排除するために使用されます。
/// </summary>
public class JournalEntryService
{
    /// <summary>
    /// 仕訳番号を生成します (JNL-YYYYMM-XXXX 形式)。
    /// </summary>
    public async Task<string> GenerateJournalNoAsync(IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now;
        var prefix = $"JNL-{now:yyyyMM}-";
        const string sql = "SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1 FROM journals WHERE document_no LIKE @prefix";
        var nextSeq = await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
        return $"{prefix}{nextSeq:D4}";
    }

    /// <summary>
    /// 勘定科目コードに対応する勘定科目IDを取得します。
    /// </summary>
    public async Task<int> GetAccountIdAsync(IDbConnection db, IDbTransaction? tx, string accountCode)
    {
        const string sql = "SELECT id FROM accounts WHERE code = @code";
        return await db.ExecuteScalarAsync<int>(sql, new { code = accountCode }, tx);
    }

    /// <summary>
    /// 仕訳明細を挿入します。
    /// </summary>
    public async Task InsertJournalLineAsync(IDbConnection db, IDbTransaction? tx,
        long journalId, int lineNo, int accountId, double debitAmt, double creditAmt, string description)
    {
        const string sql = @"INSERT INTO journal_lines
            (journal_id, line_no, account_id, debit_amt, credit_amt, description, created_at)
            VALUES (@JournalId, @LineNo, @AccountId, @DebitAmt, @CreditAmt, @Description, @CreatedAt)";

        await db.ExecuteAsync(sql, new
        {
            JournalId = journalId,
            LineNo = lineNo,
            AccountId = accountId,
            DebitAmt = debitAmt,
            CreditAmt = creditAmt,
            Description = description,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        }, tx);
    }

    /// <summary>
    /// 仕訳ヘッダーを作成し、生成されたIDを返します。
    /// </summary>
    public async Task<long> CreateJournalHeaderAsync(IDbConnection db, IDbTransaction? tx,
        string documentNo, string docStatus, string journalType, string dateAcct,
        string description, string sourceTable, int sourceId, double totalAmount)
    {
        const string sql = @"INSERT INTO journals
            (document_no, doc_status, journal_type, date_acct, description,
             source_table, source_id, total_amount, created_at)
            VALUES (@DocumentNo, @DocStatus, @JournalType, @DateAcct, @Description,
                    @SourceTable, @SourceId, @TotalAmount, @CreatedAt);
            SELECT last_insert_rowid();";

        return await db.ExecuteScalarAsync<long>(sql, new
        {
            DocumentNo = documentNo,
            DocStatus = docStatus,
            JournalType = journalType,
            DateAcct = dateAcct,
            Description = description,
            SourceTable = sourceTable,
            SourceId = sourceId,
            TotalAmount = totalAmount,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        }, tx);
    }
}
