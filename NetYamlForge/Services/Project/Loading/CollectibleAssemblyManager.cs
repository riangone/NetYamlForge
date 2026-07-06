using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Project.Loading;

public class CollectibleAssemblyManager
{
    private readonly ILogger<CollectibleAssemblyManager> _logger;
    private readonly ConcurrentDictionary<string, CollectibleAssemblyLoadContext> _assemblyContexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Assembly> _loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);

    public CollectibleAssemblyManager(ILogger<CollectibleAssemblyManager> logger)
    {
        _logger = logger;
    }

    public AssemblyLoadContext GetOrCreate(string projectName)
    {
        var contextName = $"ProjectContext_{projectName}_{Guid.NewGuid():N}";
        var alc = new CollectibleAssemblyLoadContext(contextName);
        _assemblyContexts[projectName] = alc;
        return alc;
    }

    public void Register(string projectName, Assembly assembly)
    {
        _loadedAssemblies[projectName] = assembly;
    }

    public bool TryGetLoadedAssembly(string projectName, out Assembly? assembly)
    {
        return _loadedAssemblies.TryGetValue(projectName, out assembly);
    }

    public void RemoveLoadedAssembly(string projectName)
    {
        _loadedAssemblies.TryRemove(projectName, out _);
    }

    public void UnloadAlcInternal(string projectName)
    {
        if (_assemblyContexts.TryRemove(projectName, out var oldAlc))
        {
            try
            {
                oldAlc.Unload();
                TrackUnload(projectName, oldAlc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "古い AssemblyLoadContext のアンロードに失敗：{Project}", projectName);
            }
        }
    }

    private void TrackUnload(string projectName, AssemblyLoadContext alc)
    {
        var weakRef = new WeakReference(alc);
        Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(1000);
            }

            if (weakRef.IsAlive)
            {
                _logger.LogWarning("警告: プロジェクト '{Project}' の AssemblyLoadContext (Collectible) がアンロード後に GC によって回収されませんでした。メモリリークの可能性があります。", projectName);
            }
            else
            {
                _logger.LogInformation("プロジェクト '{Project}' の AssemblyLoadContext が正常に GC によって回収されました。", projectName);
            }
        });
    }

    private class CollectibleAssemblyLoadContext : AssemblyLoadContext
    {
        public CollectibleAssemblyLoadContext(string name) : base(name, isCollectible: true)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            return null;
        }
    }
}
