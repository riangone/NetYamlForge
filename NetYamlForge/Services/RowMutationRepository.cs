// ファイル概要: IRowMutationRepository の実装クラス。
// Entity 側（DynamicCrudRepository）と Section 側（PageRowMutationService）が持つ
// INSERT / UPDATE / DELETE SQL 生成ロジックの共通実装です。
// 識別子はすべて呼び出し前に検証済みと仮定します。
//
// DCS001 抑制理由: このファイルは動的SQL生成ユーティリティです。
// 引数として受け取るテーブル名・列名はすべて呼び出し元で SqlSafetyGuard.IdentifierRegex で検証済みです。
#pragma warning disable DCS001

using System.Data;
using Dapper;

namespace NetYamlForge.Services;

/// <summary>
/// IRowMutationRepository の標準実装。
/// 引数のテーブル名・列名はすべて呼び出し元で検証済みであること。
/// </summary>
public sealed class RowMutationRepository : IRowMutationRepository
{
    private readonly IDbConnection _db;
    private readonly ILogger<RowMutationRepository> _logger;

    public RowMutationRepository(IDbConnection db, ILogger<RowMutationRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> InsertAsync(
        string table,
        IReadOnlyList<KeyValuePair<string, object?>> fields,
        IDbTransaction? tx = null)
    {
        var cols = string.Join(", ", fields.Select(f => $"\"{f.Key}\""));
        var parms = string.Join(", ", fields.Select(f => $"@{f.Key}"));
        var sql = $"INSERT INTO \"{table}\" ({cols}) VALUES ({parms})";

        var param = new DynamicParameters();
        foreach (var f in fields)
            param.Add(f.Key, f.Value);

        _logger.LogInformation("RowMutationRepository.InsertAsync table={Table} sql={Sql}", table, sql);
        return await _db.ExecuteAsync(sql, param, tx);
    }

    /// <inheritdoc/>
    public async Task<int> UpdateAsync(
        string table,
        string primaryKeyColumn,
        object primaryKeyValue,
        IReadOnlyList<KeyValuePair<string, object?>> fields,
        IDbTransaction? tx = null)
    {
        var setSql = string.Join(", ", fields.Select(f => $"\"{f.Key}\" = @{f.Key}"));
        var sql = $"UPDATE \"{table}\" SET {setSql} WHERE \"{primaryKeyColumn}\" = @__pk";

        var param = new DynamicParameters();
        foreach (var f in fields)
            param.Add(f.Key, f.Value);
        param.Add("__pk", primaryKeyValue);

        _logger.LogInformation("RowMutationRepository.UpdateAsync table={Table} pk={Pk} sql={Sql}", table, primaryKeyValue, sql);
        return await _db.ExecuteAsync(sql, param, tx);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteAsync(
        string table,
        string primaryKeyColumn,
        object primaryKeyValue,
        IDbTransaction? tx = null)
    {
        var sql = $"DELETE FROM \"{table}\" WHERE \"{primaryKeyColumn}\" = @__pk";

        _logger.LogInformation("RowMutationRepository.DeleteAsync table={Table} pk={Pk}", table, primaryKeyValue);
        return await _db.ExecuteAsync(sql, new { __pk = primaryKeyValue }, tx);
    }
}
