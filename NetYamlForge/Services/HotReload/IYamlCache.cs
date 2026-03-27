// ファイル概要：YAML ホットリロード機能のキャッシュインターフェース
using System.Collections.Concurrent;

namespace NetYamlForge.Services.HotReload;

public interface IYamlCache
{
    string CacheKey { get; }
    DateTime LastModified { get; }
    Task ReloadAsync(string filePath);
    void Clear();
    bool IsValid();
}

public abstract class YamlCacheBase<T> : IYamlCache where T : class
{
    protected readonly SemaphoreSlim _semaphore = new(1, 1);
    protected T? _cachedValue;
    protected string _filePath = string.Empty;
    protected DateTime _lastModified;
    protected bool _isLoaded;

    public string CacheKey { get; protected set; } = string.Empty;
    public DateTime LastModified => _lastModified;

    public virtual void Clear()
    {
        _cachedValue = null;
        _isLoaded = false;
        _lastModified = DateTime.MinValue;
    }

    public abstract Task ReloadAsync(string filePath);
    public T? GetCachedValue() => _cachedValue;
    public bool IsValid() => _isLoaded && _cachedValue != null;
}

public class ProjectYamlCacheManager
{
    private readonly ConcurrentDictionary<string, IYamlCache> _caches = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ProjectYamlCacheManager> _logger;

    public ProjectYamlCacheManager(ILogger<ProjectYamlCacheManager> logger) => _logger = logger;

    public void Register(string key, IYamlCache cache)
    {
        _caches[key] = cache;
        _logger.LogDebug("YAML キャッシュを登録：{Key}", key);
    }

    public bool TryGet(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IYamlCache? cache) =>
        _caches.TryGetValue(key, out cache);

    public async Task ReloadAsync(string key, string filePath)
    {
        if (_caches.TryGetValue(key, out var cache))
        {
            await cache.ReloadAsync(filePath);
            _logger.LogInformation("YAML キャッシュをリロード：{Key} ({File})", key, filePath);
        }
    }

    public async Task ReloadProjectAsync(string projectName)
    {
        var projectCaches = _caches.Keys.Where(k => 
            k.StartsWith(projectName, StringComparison.OrdinalIgnoreCase)).ToList();
        
        foreach (var key in projectCaches)
        {
            if (_caches.TryGetValue(key, out var cache))
            {
                var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName);
                await cache.ReloadAsync(projectDir);
            }
        }
    }

    public void ClearAll()
    {
        foreach (var cache in _caches.Values) cache.Clear();
        _logger.LogInformation("全 YAML キャッシュをクリア");
    }

    public Dictionary<string, CacheStatus> GetStatusSnapshot() =>
        _caches.ToDictionary(kvp => kvp.Key, kvp => new CacheStatus
        {
            Key = kvp.Key,
            LastModified = kvp.Value.LastModified,
            IsValid = kvp.Value.IsValid()
        });
}

public record CacheStatus
{
    public string Key { get; init; } = string.Empty;
    public DateTime LastModified { get; init; }
    public bool IsValid { get; init; }
}
