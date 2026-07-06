using System;
using System.Collections.Generic;
using System.Linq;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public interface IEntityHooksService
{
    List<string>? GetHookList(EntityHooksDefinition hooks, Func<EntityHooksDefinition, object?> selector);
    List<string>? GetExpandedHookList(EntityHooksDefinition hooks, Func<EntityHooksDefinition, object?> selector, Action<string>? onWarning = null);
}

public class EntityHooksService : IEntityHooksService
{
    public List<string>? GetHookList(EntityHooksDefinition hooks, Func<EntityHooksDefinition, object?> selector)
    {
        if (hooks == null) return null;
        var value = selector(hooks);
        if (value is string s) return new List<string> { s };
        if (value is List<object> objList) return objList.ConvertAll(x => x?.ToString() ?? "");
        if (value is List<string> strList) return strList;
        if (value is System.Collections.IEnumerable e) return e.Cast<object>().Select(x => x?.ToString() ?? "").ToList();
        return null;
    }

    public List<string>? GetExpandedHookList(
        EntityHooksDefinition hooks,
        Func<EntityHooksDefinition, object?> selector,
        Action<string>? onWarning = null)
    {
        if (hooks == null) return null;
        var hookList = GetHookList(hooks, selector);
        if (hookList == null || hookList.Count == 0)
            return hookList;

        return ExpandHookEntries(hooks, hookList, onWarning);
    }

    private List<string> ExpandHookEntries(EntityHooksDefinition hooks, IEnumerable<string>? entries, Action<string>? onWarning = null)
    {
        if (entries == null) return new List<string>();
        var result = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in entries)
            ExpandEntry(hooks, h, result, visiting, onWarning);
        return result;
    }

    private void ExpandEntry(
        EntityHooksDefinition hooks,
        string? name,
        List<string> output,
        HashSet<string> visiting,
        Action<string>? onWarning)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Trim();
        if (!trimmed.StartsWith('@')) { output.Add(trimmed); return; }

        var presetName = trimmed[1..].Trim();
        if (string.IsNullOrWhiteSpace(presetName))
        {
            onWarning?.Invoke("Hook preset 名が空です。'@' のみは無効です。");
            return;
        }

        var preset = GetPreset(hooks, presetName);
        if (preset == null)
        {
            onWarning?.Invoke($"Hook preset '{presetName}' が定義されていません。");
            return;
        }

        if (!visiting.Add(presetName))
        {
            onWarning?.Invoke($"Hook preset '{presetName}' に循環参照があります。");
            return;
        }

        foreach (var h in preset)
            ExpandEntry(hooks, h, output, visiting, onWarning);
        visiting.Remove(presetName);
    }

    private IReadOnlyList<string>? GetPreset(EntityHooksDefinition hooks, string presetName)
    {
        if (hooks.Presets == null || !hooks.Presets.TryGetValue(presetName, out var presetValue))
            return null;
        var list = ToHookList(presetValue);
        return list.Count == 0 ? null : list;
    }

    private static List<string> ToHookList(object? value)
    {
        if (value is null)
        {
            return new List<string>();
        }

        if (value is string s)
        {
            return new List<string> { s };
        }

        if (value is List<string> list)
        {
            return list;
        }

        if (value is List<object> objList)
        {
            return objList.ConvertAll(x => x?.ToString() ?? "");
        }

        if (value is System.Collections.IEnumerable e)
        {
            return e.Cast<object>().Select(x => x?.ToString() ?? "").ToList();
        }

        return new List<string>();
    }
}
