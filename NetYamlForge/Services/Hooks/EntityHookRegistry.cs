// ファイル概要：DI に登録された IEntityHook 実装を名前で検索するレジストリ実装です。

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// DI コンテナから IEnumerable&lt;IEntityHook&gt; を受け取り、
/// 名前をキーとした辞書に格納するレジストリ実装。
/// フック名の重複がある場合は後勝ちになります。
/// </summary>
public class EntityHookRegistry : IEntityHookRegistry
{
    private readonly Dictionary<string, IEntityHook> _hooks;
    private readonly ILogger<EntityHookRegistry> _logger;

    public EntityHookRegistry(IEnumerable<IEntityHook> hooks, ILogger<EntityHookRegistry> logger)
    {
        _logger = logger;
        _hooks = new Dictionary<string, IEntityHook>(StringComparer.OrdinalIgnoreCase);
        foreach (var hook in hooks)
        {
            if (_hooks.ContainsKey(hook.Name))
            {
                _logger.LogWarning("Duplicate hook name '{Name}' — overwriting with {Type}", hook.Name, hook.GetType().Name);
            }
            _hooks[hook.Name] = hook;
        }

        _logger.LogInformation("EntityHookRegistry initialized with {Count} hook(s): {Names}",
            _hooks.Count, string.Join(", ", _hooks.Keys));
    }

    public IEntityHook? Find(string hookNameWithConfig, EntityHookContext? context = null)
    {
        // フック名から設定部分を抽出（例："validate_email:Email,Phone" → "validate_email" + "Email,Phone"）
        var parts = hookNameWithConfig.Split(':', 2);
        var hookName = parts[0].Trim();
        var hookConfig = parts.Length > 1 ? parts[1].Trim() : null;

        if (!_hooks.TryGetValue(hookName, out var hook))
        {
            _logger.LogWarning("Hook '{Name}' not found in registry. Check entities.yml and DI registration.", hookName);
            return null;
        }

        // 設定がある場合はコンテキストに渡す
        if (!string.IsNullOrEmpty(hookConfig) && context != null)
        {
            context.Data["__hookConfig"] = hookConfig;
            _logger.LogDebug("Hook '{Name}' called with config: {Config}", hookName, hookConfig);
        }

        return hook;
    }

    /// <summary>
    /// 登録されているすべてのフックを取得します。
    /// </summary>
    public IEnumerable<IEntityHook> GetAllHooks() => _hooks.Values;
}
