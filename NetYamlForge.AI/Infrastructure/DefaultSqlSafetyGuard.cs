// AI 模块内部适配器 - SQL 安全检查默认实现

using System.Text.RegularExpressions;

namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// SQL 安全验证默认实现（从主框架复制的纯函数逻辑）
/// </summary>
public class DefaultSqlSafetyGuard : ISqlSafetyGuard
{
    // SQL 关键字列表（用于注入检测）
    private static readonly HashSet<string> DangerousKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE", "EXEC", "EXECUTE",
        "INSERT", "UPDATE", "REPLACE", "MERGE", "GRANT", "REVOKE",
        "SHUTDOWN", "KILL", "xp_", "sp_"
    };

    // 有效标识符正则（字母开头，只包含字母数字下划线）
    private static readonly Regex ValidIdentifierRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    public bool IsUnsafeToken(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var upper = input.ToUpperInvariant();
        return DangerousKeywords.Any(kw => upper.Contains(kw));
    }

    public bool IsValidIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        // 标识符长度检查
        if (identifier.Length > 128)
            return false;

        return ValidIdentifierRegex.IsMatch(identifier);
    }

    public string SanitizeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return "_invalid_";

        // 移除非字母数字下划线字符
        var sanitized = Regex.Replace(identifier, @"[^a-zA-Z0-9_]", "_");

        // 确保以字母或下划线开头
        if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            sanitized = "_" + sanitized;

        // 截断到最大长度
        if (sanitized.Length > 128)
            sanitized = sanitized.Substring(0, 128);

        return sanitized;
    }
}
