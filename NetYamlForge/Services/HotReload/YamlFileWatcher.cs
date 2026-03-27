// ファイル概要：YAML ファイルの変更を監視する FileSystemWatcher ラッパー
using System.Collections.Concurrent;

namespace NetYamlForge.Services.HotReload;

public record YamlFileChangedEventArgs
{
    public string FilePath { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public WatcherChangeTypes ChangeType { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public interface IYamlFileWatcher : IDisposable
{
    event EventHandler<YamlFileChangedEventArgs>? FileChanged;
    void StartWatching(string projectDir);
    void StopWatching(string projectDir);
    void StopAll();
}

public class YamlFileWatcher : IYamlFileWatcher, IDisposable
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastChanged = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
    private readonly ILogger<YamlFileWatcher> _logger;
    private bool _disposed;

    public event EventHandler<YamlFileChangedEventArgs>? FileChanged;

    public YamlFileWatcher(ILogger<YamlFileWatcher> logger) => _logger = logger;

    public void StartWatching(string projectDir)
    {
        if (!Directory.Exists(projectDir)) return;

        var projectName = Path.GetFileName(projectDir);
        if (_watchers.TryGetValue(projectName, out var existing))
        {
            existing.Changed -= OnFileChanged;
            existing.Created -= OnFileChanged;
            existing.Deleted -= OnFileChanged;
            existing.Renamed -= OnFileRenamed;
            existing.Dispose();
        }

        var watcher = new FileSystemWatcher
        {
            Path = projectDir,
            Filter = "*.yml",
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size
        };

        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Deleted += OnFileChanged;
        watcher.Renamed += OnFileRenamed;
        watcher.Error += OnWatcherError;
        _watchers[projectName] = watcher;

        var yamlWatcher = new FileSystemWatcher
        {
            Path = projectDir,
            Filter = "*.yaml",
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size
        };

        yamlWatcher.Changed += OnFileChanged;
        yamlWatcher.Created += OnFileChanged;
        yamlWatcher.Deleted += OnFileChanged;
        yamlWatcher.Renamed += OnFileRenamed;
        yamlWatcher.Error += OnWatcherError;
        _watchers[$"{projectName}_yaml"] = yamlWatcher;

        _logger.LogInformation("YAML ファイル監視を開始：{Project} ({Dir})", projectName, projectDir);
    }

    public void StopWatching(string projectDir)
    {
        var projectName = Path.GetFileName(projectDir);
        if (_watchers.TryRemove(projectName, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        if (_watchers.TryRemove($"{projectName}_yaml", out var yamlWatcher))
        {
            yamlWatcher.EnableRaisingEvents = false;
            yamlWatcher.Dispose();
        }
    }

    public void StopAll()
    {
        foreach (var kvp in _watchers)
        {
            kvp.Value.EnableRaisingEvents = false;
            kvp.Value.Dispose();
        }
        _watchers.Clear();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!e.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) &&
            !e.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) return;

        var now = DateTime.UtcNow;
        var filePath = e.FullPath;

        if (_lastChanged.TryGetValue(filePath, out var lastTime) && (now - lastTime) < _debounceInterval) return;
        _lastChanged[filePath] = now;

        var projectName = ExtractProjectName(filePath);
        _logger.LogDebug("YAML ファイル変更を検知：{File} ({ChangeType})", filePath, e.ChangeType);
        FileChanged?.Invoke(this, new YamlFileChangedEventArgs
        {
            FilePath = filePath,
            ProjectName = projectName,
            ChangeType = e.ChangeType,
            Timestamp = now
        });
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!e.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) &&
            !e.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) return;

        var projectName = ExtractProjectName(e.FullPath);
        FileChanged?.Invoke(this, new YamlFileChangedEventArgs
        {
            FilePath = e.FullPath,
            ProjectName = projectName,
            ChangeType = WatcherChangeTypes.Renamed,
            Timestamp = DateTime.UtcNow
        });
    }

    private void OnWatcherError(object sender, ErrorEventArgs e) =>
        _logger.LogError(e.GetException(), "YAML ファイル監視エラー");

    private static string ExtractProjectName(string filePath)
    {
        var projectsDir = Path.Combine(Directory.GetCurrentDirectory(), "projects");
        if (filePath.StartsWith(projectsDir, StringComparison.OrdinalIgnoreCase))
        {
            var relative = filePath.Substring(projectsDir.Length).TrimStart(Path.DirectorySeparatorChar);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            return parts.Length > 0 ? parts[0] : string.Empty;
        }
        return string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopAll();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
