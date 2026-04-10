namespace NetYamlForge.Services.Hooks;

/// <summary>
/// Hook実装のための共通ヘルパーメソッド。
/// </summary>
internal static class HookHelpers
{
    /// <summary>
    /// 指定されたステータスへの変更かどうかをチェックし、Entity IDを出力します。
    /// </summary>
    /// <param name="ctx">Hookコンテキスト</param>
    /// <param name="targetStatus">対象ステータス</param>
    /// <param name="entityId">出力されるEntity ID</param>
    /// <returns>指定されたステータスへの変更であればtrue</returns>
    public static bool IsStatusChangeTo(this IDictionary<string, object?> ctx, string targetStatus, out int entityId)
    {
        entityId = 0;
        var newStatus = ctx.TryGetValue("DocStatus", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != targetStatus) return false;
        if (!ctx.TryGetValue("Id", out var idObj) || idObj == null) return false;
        entityId = Convert.ToInt32(idObj);
        return true;
    }

    /// <summary>
    /// コンテキストから指定された型の値を安全に取得します。
    /// </summary>
    public static T? GetValueOrDefault<T>(this IDictionary<string, object?> ctx, string key)
    {
        if (ctx.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    /// <summary>
    /// コンテキストから値を取得し、nullの場合はデフォルト値を返します。
    /// </summary>
    public static T GetOr<T>(this IDictionary<string, object?> ctx, string key, T defaultValue)
    {
        if (ctx.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return defaultValue;
    }
}
