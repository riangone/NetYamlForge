using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Services.Page;

public interface IPageActionDispatcher
{
    void Register(string projectName, IPageActionHandler handler);
    void Clear(string projectName);
    Task<IActionResult?> DispatchAsync(string project, string pageName, string actionName, PageActionContext ctx);
}

public class PageActionDispatcher : IPageActionDispatcher
{
    private readonly ConcurrentDictionary<(string Project, string ActionName), IPageActionHandler> _handlers = 
        new(new TupleComparer());

    public PageActionDispatcher(IEnumerable<IPageActionHandler> staticHandlers)
    {
        foreach (var handler in staticHandlers)
        {
            Register("", handler);
        }
    }

    public void Register(string projectName, IPageActionHandler handler)
    {
        // If the handler restricts itself to a specific project, use that. Otherwise use the passed projectName.
        var projKey = handler.Project ?? projectName ?? "";
        _handlers[(projKey, handler.ActionName)] = handler;
    }

    public void Clear(string projectName)
    {
        var keysToRemove = new List<(string Project, string ActionName)>();
        foreach (var key in _handlers.Keys)
        {
            if (string.Equals(key.Project, projectName, StringComparison.OrdinalIgnoreCase))
            {
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove)
        {
            _handlers.TryRemove(key, out _);
        }
    }

    public async Task<IActionResult?> DispatchAsync(string project, string pageName, string actionName, PageActionContext ctx)
    {
        // 1. Project-specific priority
        if (_handlers.TryGetValue((project, actionName), out var handler))
        {
            return await handler.HandleAsync(ctx);
        }
        // 2. Global fallback
        if (_handlers.TryGetValue(("", actionName), out handler))
        {
            return await handler.HandleAsync(ctx);
        }

        return null;
    }

    private class TupleComparer : IEqualityComparer<(string Project, string ActionName)>
    {
        public bool Equals((string Project, string ActionName) x, (string Project, string ActionName) y)
        {
            return string.Equals(x.Project, y.Project, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.ActionName, y.ActionName, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Project, string ActionName) obj)
        {
            return HashCode.Combine(
                obj.Project != null ? obj.Project.ToLowerInvariant() : "", 
                obj.ActionName != null ? obj.ActionName.ToLowerInvariant() : "");
        }
    }
}
