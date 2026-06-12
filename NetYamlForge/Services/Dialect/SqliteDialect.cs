// ファイル概要: SQLite 用 SQL 方言実装（LIMIT / OFFSET によるページング）。
using Dapper;

namespace NetYamlForge.Services.Dialect;

public class SqliteDialect : ISqlDialect
{
    public string ConcatOperator => "||";

    public string LastInsertIdExpression => "last_insert_rowid()";

    public void AppendNumberedPagination(List<string> sqlParts, DynamicParameters param, int effectivePageSize, int offset, string defaultOrderByExpr)
    {
        // SQLite は ORDER BY なしでも LIMIT / OFFSET が使えます
        sqlParts.Add(" LIMIT @PageSize");
        param.Add("PageSize", effectivePageSize);
        sqlParts.Add(" OFFSET @Offset");
        param.Add("Offset", offset);
    }

    public void AppendKeysetPagination(List<string> sqlParts, DynamicParameters param, int effectivePageSize)
    {
        sqlParts.Add(" LIMIT @PageSize");
        param.Add("PageSize", effectivePageSize);
    }
}
