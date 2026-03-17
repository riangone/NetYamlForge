// ファイル概要: 一覧クエリの共通オプション解決ロジックです。

namespace NetYamlForge.Services;

public static class ListQueryOptionResolver
{
    public static bool ResolveCountEnabled(string? count)
    {
        if (string.IsNullOrWhiteSpace(count))
        {
            return true;
        }

        return !count.Equals("false", StringComparison.OrdinalIgnoreCase)
            && !count.Equals("0", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ResolveClearRequested(string? clear)
    {
        if (string.IsNullOrWhiteSpace(clear))
        {
            return false;
        }

        return clear.Equals("1", StringComparison.OrdinalIgnoreCase)
            || clear.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
