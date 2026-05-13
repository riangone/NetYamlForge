// ファイル概要: SQL 安全性チェックの共用ユーティリティ
// IdentifierRegex, ExpressionRegex などを一元管理し、重複定義を防ぎます（改善5.1）

using System.Text.RegularExpressions;

namespace NetYamlForge.Services;

/// <summary>
/// SQL 安全性チェック用の共用正規表現とヘルパーメソッド（改善5.1：重複定義排除）
/// </summary>
public static class SqlSafetyGuard
{
    /// <summary>識別子（テーブル名、列名など）の正規表現。単純識別子またはSQLite括弧引用形式 [Name With Spaces] をサポート</summary>
    public static readonly Regex IdentifierRegex =
        new(@"^([A-Za-z_][A-Za-z0-9_]*|\[[A-Za-z_][A-Za-z0-9_ ]+\])$", RegexOptions.Compiled);

    /// <summary>SQL 式（WHERE 句など）の正規表現</summary>
    public static readonly Regex ExpressionRegex =
        new(@"^[A-Za-z0-9_.\s,()+*/%<>=!'|-]+$", RegexOptions.Compiled);

    /// <summary>識別子として有効かどうかを判定</summary>
    public static bool IsValidIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && IdentifierRegex.IsMatch(value);

    /// <summary>SQL インジェクション危険トークンを含むかどうかを判定</summary>
    public static bool IsUnsafeToken(string? value) =>
        !string.IsNullOrEmpty(value) &&
        (value.Contains(';',    StringComparison.Ordinal) ||
         value.Contains("--",   StringComparison.Ordinal) ||
         value.Contains("/*",   StringComparison.Ordinal) ||
         value.Contains("*/",   StringComparison.Ordinal));

    /// <summary>識別子の安全性を確保する（不正な場合は例外を投げる）</summary>
    public static void EnsureIdentifier(string? value, string context)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Invalid identifier in '{context}': value is empty.");
        if (!IsValidIdentifier(value))
            throw new InvalidOperationException(
                $"Unsafe identifier in '{context}': '{value}'. Must match [A-Za-z_][A-Za-z0-9_]* or [Name With Spaces]");
        
        // 危険なSQLキーワードをチェック（識別子としてSQLコマンドを拒否）
        // 注意: "CREATE" は許可される（一般的な識別子として使用されるため）
        var dangerousKeywords = new[] { "DROP", "DELETE", "INSERT", "UPDATE", "ALTER", "EXEC", "EXECUTE", "TRUNCATE", "REPLACE" };
        foreach (var kw in dangerousKeywords)
            if (value.Equals(kw, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Unsafe identifier in '{context}': '{value}'. SQL keywords are not allowed.");
    }

    /// <summary>式の安全性を確保する（危険な場合は例外を投げる）</summary>
    public static void EnsureExpression(string? value, string context)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (IsUnsafeToken(value))
            throw new InvalidOperationException(
                $"Unsafe expression in '{context}': contains SQL injection markers");

        // スペース付きキーワード: 列名への誤マッチを防ぐ（例: "created_at" が "CREATE" にマッチしない）
        var dangerous = new[]
        {
            "DROP ", "ALTER ", "TRUNCATE ", "DELETE ", "INSERT ", "UPDATE ", "EXEC ", "EXECUTE ",
            "CREATE ", "REPLACE ", "UNION ", "DECLARE ", "CAST("
        };
        foreach (var kw in dangerous)
            if (value.Contains(kw, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Unsafe expression in '{context}': contains forbidden keyword '{kw}'");

        if (!ExpressionRegex.IsMatch(value))
            throw new InvalidOperationException(
                $"Invalid expression in '{context}': '{value}'. Contains disallowed characters.");
    }
}
