// ファイル概要: MySQL 用 SQL 方言実装（LIMIT / OFFSET によるページング）。
using Dapper;

namespace NetYamlForge.Services.Dialect;

public class MySqlDialect : ISqlDialect
{
    public string ConcatOperator => "||";

    public string LastInsertIdExpression => "LAST_INSERT_ID()";

    public void AppendNumberedPagination(List<string> sqlParts, DynamicParameters param, int effectivePageSize, int offset, string defaultOrderByExpr)
    {
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
