// ファイル概要: SQL Server 用 SQL 方言実装（OFFSET / FETCH NEXT によるページング）。
// SQL Server は OFFSET/FETCH に ORDER BY が必須のため、未指定の場合は defaultOrderByExpr で補完します。
using Dapper;

namespace NetYamlForge.Services.Dialect;

public class SqlServerDialect : ISqlDialect
{
    public string ConcatOperator => "+";

    public void AppendNumberedPagination(List<string> sqlParts, DynamicParameters param, int effectivePageSize, int offset, string defaultOrderByExpr)
    {
        // SQL Server は OFFSET/FETCH の前に ORDER BY が必須
        bool hasOrderBy = sqlParts.Any(s => s.TrimStart().StartsWith("ORDER BY", StringComparison.OrdinalIgnoreCase));
        if (!hasOrderBy)
        {
            sqlParts.Add($" ORDER BY {defaultOrderByExpr}");
        }

        param.Add("Offset", offset);
        param.Add("PageSize", effectivePageSize);
        sqlParts.Add(" OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
    }

    public void AppendKeysetPagination(List<string> sqlParts, DynamicParameters param, int effectivePageSize)
    {
        // keyset モードは呼び出し側で ORDER BY を追加済み
        param.Add("PageSize", effectivePageSize);
        sqlParts.Add(" OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY");
    }
}
