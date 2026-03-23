// ファイル概要: プロジェクト固有のカスタムアクションハンドラーを管理するレジストリ。

using System.Collections.Concurrent;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

/// <summary>
/// プロジェクト固有の ICustomActionHandler を管理するレジストリ。
/// </summary>
public interface IProjectActionRegistry
{
    ICustomActionHandler? Find(string projectName, string handlerName);
    void Register(string projectName, ICustomActionHandler handler);
}

/// <summary>
/// IProjectActionRegistry のデフォルト実装。
/// </summary>
public class ProjectActionRegistry : IProjectActionRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ICustomActionHandler>> _registry
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<ProjectActionRegistry> _logger;

    public ProjectActionRegistry(ILogger<ProjectActionRegistry> logger)
    {
        _logger = logger;
    }

    public ICustomActionHandler? Find(string projectName, string handlerName)
    {
        if (!_registry.TryGetValue(projectName, out var handlers))
            return null;
        handlers.TryGetValue(handlerName, out var handler);
        return handler;
    }

    public void Register(string projectName, ICustomActionHandler handler)
    {
        var handlers = _registry.GetOrAdd(projectName,
            _ => new ConcurrentDictionary<string, ICustomActionHandler>(StringComparer.OrdinalIgnoreCase));
        handlers[handler.Name] = handler;
        _logger.LogDebug(
            "プロジェクト '{Project}' にカスタムアクションハンドラー '{Name}' ({Type}) を登録しました",
            projectName, handler.Name, handler.GetType().Name);
    }
}
