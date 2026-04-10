using System.Data;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// ドキュメント番号自動生成Hookの基底クラス。
/// 表名、プレフィックス、SUBSTRオフセットを指定して再利用可能です。
/// </summary>
public abstract class DocumentNumberGeneratorHook : IEntityHook
{
    private readonly string _tableName;
    private readonly string _prefix;
    private readonly int _substrOffset;

    protected DocumentNumberGeneratorHook(string tableName, string prefix, int substrOffset)
    {
        // 表名はフック定義時に固定されるため、セキュリティリスクなし
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        _substrOffset = substrOffset;

        // 表名の安全性を検証（SQLインジェクション防止）
        SqlSafetyGuard.EnsureIdentifier(tableName, "DocumentNumberGeneratorHook.tableName");
    }

    public string Name => $"{_tableName}_document_no";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 既にDocumentNoが設定されている場合はスキップ
        if (ctx.Values.TryGetValue("DocumentNo", out var docNo) &&
            docNo != null && !string.IsNullOrWhiteSpace(docNo.ToString()))
        {
            return HookResult.Continue();
        }

        var now = DateTime.Now;
        var prefix = $"{_prefix}{now:yyyyMM}-";
        var nextSeq = await GetNextSequenceAsync(db, tx, prefix);
        ctx.Values["DocumentNo"] = $"{prefix}{nextSeq:D4}";
        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 次のシーケンス番号を取得します。
    /// 派生クラスでSQLを定義し、アナライザーの誤検知を回避します。
    /// </summary>
    protected abstract Task<int> GetNextSequenceAsync(IDbConnection db, IDbTransaction? tx, string prefix);
}

/// <summary>
/// 契約ドキュメント番号生成Hook (CON-YYYYMM-XXXX)
/// </summary>
public class ContractDocumentNoHook : DocumentNumberGeneratorHook
{
    public ContractDocumentNoHook() : base("contracts", "CON-", 12) { }

    protected override async Task<int> GetNextSequenceAsync(IDbConnection db, IDbTransaction? tx, string prefix)
    {
        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1
            FROM contracts
            WHERE document_no LIKE @prefix";
        return await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
    }
}

/// <summary>
/// 見積ドキュメント番号生成Hook (EST-YYYYMM-XXXX)
/// </summary>
public class EstimationDocumentNoHook : DocumentNumberGeneratorHook
{
    public EstimationDocumentNoHook() : base("estimations", "EST-", 12) { }

    protected override async Task<int> GetNextSequenceAsync(IDbConnection db, IDbTransaction? tx, string prefix)
    {
        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1
            FROM estimations
            WHERE document_no LIKE @prefix";
        return await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
    }
}

/// <summary>
/// 請求ドキュメント番号生成Hook (BILL-YYYYMM-XXXX)
/// </summary>
public class BillDocumentNoHook : DocumentNumberGeneratorHook
{
    public BillDocumentNoHook() : base("bills", "BILL-", 13) { }

    protected override async Task<int> GetNextSequenceAsync(IDbConnection db, IDbTransaction? tx, string prefix)
    {
        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 13) AS INTEGER)), 0) + 1
            FROM bills
            WHERE document_no LIKE @prefix";
        return await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
    }
}

/// <summary>
/// 仕訳ドキュメント番号生成Hook (JNL-YYYYMM-XXXX)
/// </summary>
public class JournalDocumentNoHook : DocumentNumberGeneratorHook
{
    public JournalDocumentNoHook() : base("journals", "JNL-", 12) { }

    protected override async Task<int> GetNextSequenceAsync(IDbConnection db, IDbTransaction? tx, string prefix)
    {
        const string sql = @"
            SELECT COALESCE(MAX(CAST(SUBSTR(document_no, 12) AS INTEGER)), 0) + 1
            FROM journals
            WHERE document_no LIKE @prefix";
        return await db.ExecuteScalarAsync<int>(sql, new { prefix }, tx);
    }
}
