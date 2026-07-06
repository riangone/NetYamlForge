using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace NetYamlForge.Services;

/// <summary>
/// YAML 設定由来の SQL 式（WHERE 句・集計式）のホワイトリスト検証パーサー。
/// 下記文法に合致しない入力を例外で拒否します。文法にセミコロン・コメント・
/// サブクエリ・UNION が存在しないため、構造的に SQL インジェクションを排除します。
/// </summary>
public static class SqlExpressionParser
{
    private static readonly ConcurrentDictionary<string, bool> _validationCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCacheSize = 1000;

    internal static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "LENGTH", "LOWER", "UPPER", "TRIM", "SUBSTR", "REPLACE",
        "ABS", "ROUND", "COALESCE", "IFNULL", "NULLIF",
        "DATE", "DATETIME", "TIME", "STRFTIME", "JULIANDAY",
        "MIN", "MAX", "SUM", "COUNT", "AVG", "CAST"
    };

    internal static readonly HashSet<string> CastTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTEGER", "TEXT", "REAL", "NUMERIC", "BLOB"
    };

    /// <summary>式を検証。不正なら InvalidOperationException（context と失敗位置を含む）</summary>
    public static void Validate(string expression, string context)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new InvalidOperationException($"Invalid expression in '{context}': expression is empty.");

        if (_validationCache.ContainsKey(expression))
            return;

        var tokenizer = new SqlExpressionTokenizer(expression, context);
        var tokens = tokenizer.Tokenize();
        if (tokens.Count == 0)
            throw new InvalidOperationException($"Invalid expression in '{context}': no tokens found.");

        var parser = new SqlExpressionSyntaxParser(tokens, context);
        parser.ParseExpression();
        parser.ExpectEnd();

        if (_validationCache.Count >= MaxCacheSize)
        {
            _validationCache.Clear();
        }
        _validationCache.TryAdd(expression, true);
    }
}
