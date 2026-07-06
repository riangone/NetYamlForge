#pragma warning disable DCS001

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public partial class DynamicCrudRepository
{
    public async Task<int> InsertAsync(string entity, IDictionary<string, object?> values, IDbTransaction? tx = null)
    {
        // identity列を除外してINSERT文を动的生成します。
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        await EnsurePermissionAsync(meta, "write");
        await VerifyFieldWritePermissionsAsync(meta, values);

        var hasAutoIncrementIdentity = meta.Columns.Any(c => c.Value.Identity);

        var valuesKeysStr = string.Join(",", values.Keys.OrderBy(k => k));
        var cacheKey = $"Insert_{meta.GetHashCode()}_{valuesKeysStr}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var cols = meta.Columns
                .Where(c => !c.Value.Identity && string.IsNullOrWhiteSpace(c.Value.Expression) && values.ContainsKey(c.Key))
                .Select(c => c.Key)
                .ToArray();

            var colList = string.Join(", ", cols);
            var paramList = string.Join(", ", cols.Select(c => "@" + c));
            sql = $"INSERT INTO {meta.Table} ({colList}) VALUES ({paramList});";
            if (hasAutoIncrementIdentity)
                sql += $" SELECT {_dialect.LastInsertIdExpression};";
            SqlCache.TryAdd(cacheKey, sql);
        }

        _logger.LogInformation("InsertAsync entity={Entity} sql={Sql}", entity, sql);
        if (hasAutoIncrementIdentity)
            return await TimedAsync("InsertAsync", entity, sql, () => _db.ExecuteScalarAsync<int>(sql, values, tx));
        return await TimedAsync("InsertAsync", entity, sql, () => _db.ExecuteAsync(sql, values, tx));
    }

    public async Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, IDbTransaction? tx = null)
    {
        // editable=falseのフォーム列は更新対象から除外します。
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        await EnsurePermissionAsync(meta, "write");
        await VerifyFieldWritePermissionsAsync(meta, values);
        var pkColumns = meta.GetPrimaryKeyColumns();

        var valuesKeysStr = string.Join(",", values.Keys.OrderBy(k => k));
        var cacheKey = $"Update_{meta.GetHashCode()}_{valuesKeysStr}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var fields = meta.Forms
                .Where(f => !f.Value.Identity && values.ContainsKey(f.Key) && f.Value.Editable)
                .Select(f => f.Key)
                .ToArray();

            var setClause = string.Join(", ", fields.Select(f => $"{f} = @{f}"));
            var whereParts = pkColumns.Select((col, i) => $"{col} = @Pk{i}");
            sql = $"UPDATE {meta.Table} SET {setClause} WHERE {string.Join(" AND ", whereParts)}";
            SqlCache.TryAdd(cacheKey, sql);
        }

        var param = new DynamicParameters(values);
        // 複合主鍵の場合は values から主鍵値を取得、単一主鍵の場合は id を使用
        for (var i = 0; i < pkColumns.Count; i++)
        {
            var col = pkColumns[i];
            if (values.TryGetValue(col, out var pkVal) && pkVal != null)
            {
                param.Add($"Pk{i}", pkVal);
            }
            else
            {
                // Fallback: use id parameter for single PK or first PK column
                param.Add($"Pk{i}", i == 0 ? id : null);
            }
        }

        _logger.LogInformation("UpdateAsync entity={Entity} id={Id} sql={Sql}", entity, id, sql);
        return await TimedAsync("UpdateAsync", entity, sql, () => _db.ExecuteAsync(sql, param, tx));
    }

    /// <summary>
    /// 複合主鍵対応の更新メソッド。主鍵値をディクショナリ形式で受け取る。
    /// </summary>
    public async Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, IDbTransaction? tx = null)
    {
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        await EnsurePermissionAsync(meta, "write");
        await VerifyFieldWritePermissionsAsync(meta, values);
        var pkColumns = meta.GetPrimaryKeyColumns();

        var valuesKeysStr = string.Join(",", values.Keys.OrderBy(k => k));
        var cacheKey = $"UpdateComposite_{meta.GetHashCode()}_{valuesKeysStr}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var fields = meta.Forms
                .Where(f => !f.Value.Identity && values.ContainsKey(f.Key) && f.Value.Editable)
                .Select(f => f.Key)
                .ToArray();

            var setClause = string.Join(", ", fields.Select(f => $"{f} = @{f}"));
            var tempParam = new DynamicParameters();
            var whereClause = BuildCompositeKeyWhere(pkColumns, keyValues, tempParam);
            sql = $"UPDATE {meta.Table} SET {setClause} WHERE {whereClause}";
            SqlCache.TryAdd(cacheKey, sql);
        }

        var param = new DynamicParameters(values);
        BuildCompositeKeyWhere(pkColumns, keyValues, param);

        _logger.LogInformation("UpdateAsync entity={Entity} keys={Keys} sql={Sql}", entity, string.Join(",", pkColumns), sql);
        return await TimedAsync("UpdateAsync.Composite", entity, sql, () => _db.ExecuteAsync(sql, param, tx));
    }

    public async Task<int> DeleteAsync(string entity, object id, IDbTransaction? tx = null)
    {
        // softDelete=trueなら論理削除、falseなら物理削除を実行します。
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        await EnsurePermissionAsync(meta, "delete");
        var pkColumns = meta.GetPrimaryKeyColumns();

        var cacheKey = $"Delete_{meta.GetHashCode()}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            // 単一主鍵: @Id で直接バインド。複合主鍵: @Id0, @Id1... で展開。
            var whereClause = pkColumns.Count == 1
                ? $"{pkColumns[0]} = @Id"
                : string.Join(" AND ", pkColumns.Select((col, i) => $"{col} = @Id{i}"));

            if (meta.SoftDelete)
            {
                sql = $"UPDATE {meta.Table} SET IsDeleted = 1 WHERE {whereClause}";
            }
            else
            {
                sql = $"DELETE FROM {meta.Table} WHERE {whereClause}";
            }
            SqlCache.TryAdd(cacheKey, sql);
        }

        var args = new { Id = id };
        if (meta.SoftDelete)
        {
            _logger.LogInformation("SoftDelete entity={Entity} id={Id}", entity, id);
            return await TimedAsync("SoftDelete", entity, sql, () => _db.ExecuteAsync(sql, args, tx));
        }

        _logger.LogInformation("Delete entity={Entity} id={Id}", entity, id);
        return await TimedAsync("Delete", entity, sql, () => _db.ExecuteAsync(sql, args, tx));
    }

    /// <summary>
    /// 複合主鍵対応の削除メソッド。主鍵値をディクショナリ形式で受け取る。
    /// </summary>
    public async Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, IDbTransaction? tx = null)
    {
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        await EnsurePermissionAsync(meta, "delete");
        var pkColumns = meta.GetPrimaryKeyColumns();

        var cacheKey = $"DeleteComposite_{meta.GetHashCode()}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var tempParam = new DynamicParameters();
            var whereClause = BuildCompositeKeyWhere(pkColumns, keyValues, tempParam);
            if (meta.SoftDelete)
            {
                sql = $"UPDATE {meta.Table} SET IsDeleted = 1 WHERE {whereClause}";
            }
            else
            {
                sql = $"DELETE FROM {meta.Table} WHERE {whereClause}";
            }
            SqlCache.TryAdd(cacheKey, sql);
        }

        var param = new DynamicParameters();
        BuildCompositeKeyWhere(pkColumns, keyValues, param);

        if (meta.SoftDelete)
        {
            _logger.LogInformation("SoftDelete entity={Entity} keys={Keys}", entity, string.Join(",", pkColumns));
            return await TimedAsync("SoftDelete.Composite", entity, sql, () => _db.ExecuteAsync(sql, param, tx));
        }

        _logger.LogInformation("Delete entity={Entity} keys={Keys}", entity, string.Join(",", pkColumns));
        return await TimedAsync("Delete.Composite", entity, sql, () => _db.ExecuteAsync(sql, param, tx));
    }
}
