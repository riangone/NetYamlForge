// ファイル概要: SQL方言抽象インターフェース。ページング句と文字列連結演算子をDBごとに切り替えます。
namespace NetYamlForge.Services.Dialect;

public interface ISqlDialect
{
    /// <summary>numbered ページング句を sqlParts に追加します。ORDER BY が未指定の場合は defaultOrderByExpr で補完します。</summary>
    void AppendNumberedPagination(List<string> sqlParts, Dapper.DynamicParameters param, int effectivePageSize, int offset, string defaultOrderByExpr);

    /// <summary>keyset ページング句を sqlParts に追加します（ORDER BY は呼び出し側が追加済み）。</summary>
    void AppendKeysetPagination(List<string> sqlParts, Dapper.DynamicParameters param, int effectivePageSize);

    /// <summary>文字列連結演算子（SQLite: ||, SQL Server: +）</summary>
    string ConcatOperator { get; }
}
