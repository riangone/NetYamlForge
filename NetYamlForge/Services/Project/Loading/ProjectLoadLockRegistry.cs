using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NetYamlForge.Services.Project.Loading;

public class ProjectLoadLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _projectLocks = new(StringComparer.OrdinalIgnoreCase);

    public SemaphoreSlim For(string projectName)
    {
        return _projectLocks.GetOrAdd(projectName, _ => new SemaphoreSlim(1, 1));
    }
}
