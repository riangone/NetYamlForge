// ファイル概要：プロジェクト固有のエンティティフックを管理するレジストリ。
// 各プロジェクトごとに独立したフック登録を可能にします。

using System.Collections.Concurrent;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

/// <summary>
/// プロジェクト固有の IEntityHook 実装を管理するレジストリ。
/// フレームワーク共通フックとは分離され、プロジェクトごとに独立して登録・管理されます。
/// </summary>
public interface IProjectHookRegistry
{
    /// <summary>
    /// 指定プロジェクトのフックを取得します。
    /// </summary>
    IEntityHook? Find(string projectName, string hookName, EntityHookContext? context = null);

    /// <summary>
    /// 指定プロジェクトに登録されている全フック名を取得します。
    /// </summary>
    IEnumerable<string> GetHookNames(string projectName);

    /// <summary>
    /// 指定プロジェクトに登録されている全フックを取得します。
    /// </summary>
    IEnumerable<IEntityHook> GetAllHooks(string projectName);

    /// <summary>
    /// プロジェクトにフックを登録します。
    /// </summary>
    void Register(string projectName, IEntityHook hook);
}

/// <summary>
/// IProjectHookRegistry のデフォルト実装。
/// プロジェクト名をキーとしてフックを分類管理します。
/// </summary>
public class ProjectHookRegistry : IProjectHookRegistry
{
    // プロジェクト名 → (フック名 → フック実装)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IEntityHook>> _projectHooks;
    private readonly ILogger<ProjectHookRegistry> _logger;

    public ProjectHookRegistry(ILogger<ProjectHookRegistry> logger)
    {
        _logger = logger;
        _projectHooks = new ConcurrentDictionary<string, ConcurrentDictionary<string, IEntityHook>>(
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 指定プロジェクトからフックを検索します。
    /// フック名は "hookName:param1,param2" の形式をサポートします。
    /// </summary>
    public IEntityHook? Find(string projectName, string hookNameWithConfig, EntityHookContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            _logger.LogWarning("プロジェクト名が指定されていません");
            return null;
        }

        if (string.IsNullOrWhiteSpace(hookNameWithConfig))
        {
            return null;
        }

        // フック名から設定部分を抽出
        var parts = hookNameWithConfig.Split(':', 2);
        var hookName = parts[0].Trim();
        var hookConfig = parts.Length > 1 ? parts[1].Trim() : null;

        if (!_projectHooks.TryGetValue(projectName, out var projectHooks))
        {
            _logger.LogDebug("プロジェクト '{Project}' にはフックが登録されていません", projectName);
            return null;
        }

        if (!projectHooks.TryGetValue(hookName, out var hook))
        {
            _logger.LogDebug("プロジェクト '{Project}' にフック '{Hook}' は登録されていません", projectName, hookName);
            return null;
        }

        // 設定がある場合はコンテキストに渡す
        if (!string.IsNullOrEmpty(hookConfig) && context != null)
        {
            context.Data["__hookConfig"] = hookConfig;
            _logger.LogDebug("プロジェクト '{Project}' のフック '{Hook}' を設定付きで実行：{Config}", 
                projectName, hookName, hookConfig);
        }

        return hook;
    }

    /// <summary>
    /// 指定プロジェクトに登録されている全フック名を取得します。
    /// </summary>
    public IEnumerable<string> GetHookNames(string projectName)
    {
        if (!_projectHooks.TryGetValue(projectName, out var projectHooks))
        {
            return Enumerable.Empty<string>();
        }

        return projectHooks.Keys;
    }

    /// <summary>
    /// 指定プロジェクトに登録されている全フックを取得します。
    /// </summary>
    public IEnumerable<IEntityHook> GetAllHooks(string projectName)
    {
        if (!_projectHooks.TryGetValue(projectName, out var projectHooks))
        {
            return Enumerable.Empty<IEntityHook>();
        }

        return projectHooks.Values;
    }

    /// <summary>
    /// プロジェクトにフックを登録します。
    /// 既存のフック名がある場合は上書きされます（ログ警告）。
    /// </summary>
    public void Register(string projectName, IEntityHook hook)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("プロジェクト名を指定してください。", nameof(projectName));
        }

        if (hook == null)
        {
            throw new ArgumentNullException(nameof(hook));
        }

        var projectHooks = _projectHooks.GetOrAdd(projectName, 
            _ => new ConcurrentDictionary<string, IEntityHook>(StringComparer.OrdinalIgnoreCase));

        if (projectHooks.TryGetValue(hook.Name, out var existing))
        {
            _logger.LogWarning(
                "プロジェクト '{Project}' にフック '{Hook}' が既に登録されています（{ExistingType} → {NewType}）",
                projectName, hook.Name, existing.GetType().Name, hook.GetType().Name);
        }

        projectHooks[hook.Name] = hook;

        _logger.LogDebug(
            "プロジェクト '{Project}' にフック '{Hook}' ({Type}) を登録しました",
            projectName, hook.Name, hook.GetType().Name);
    }

    /// <summary>
    /// 指定プロジェクトの全フックをクリアします（テスト用）。
    /// </summary>
    public void Clear(string projectName)
    {
        _projectHooks.TryRemove(projectName, out _);
    }
}
