// ファイル概要: 2つの EntityDefinition（グローバル設定 vs プロジェクト適用後の有効設定）を
// JSON にシリアライズし、行単位の差分リスト（+ Added / - Removed / Changed）を生成するサービスです。
// DynamicEntityConfigDiagnosticsService から呼ばれ、Admin 向け設定診断画面に利用されます。

using System.Text.Json;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public sealed class DynamicEntityConfigDiffService
{
    public (IReadOnlyList<string> Lines, int ChangedCount) BuildJsonDiffLines(
        EntityDefinition? baseMeta,
        EntityDefinition? effectiveMeta,
        bool includeUnchanged)
    {
        if (baseMeta == null && effectiveMeta == null)
        {
            return (new List<string> { "No metadata available." }, 0);
        }

        if (baseMeta == null)
        {
            return (new List<string> { "+ Entity exists only in effective/project metadata." }, 1);
        }

        if (effectiveMeta == null)
        {
            return (new List<string> { "- Entity exists only in base metadata." }, 1);
        }

        using var baseDoc = JsonDocument.Parse(JsonSerializer.Serialize(baseMeta));
        using var effectiveDoc = JsonDocument.Parse(JsonSerializer.Serialize(effectiveMeta));
        var diffs = new List<string>();
        var changedCount = 0;
        CollectJsonDiff(baseDoc.RootElement, effectiveDoc.RootElement, "$", diffs, includeUnchanged, ref changedCount);
        if (diffs.Count == 0)
        {
            return (new List<string> { "No differences." }, 0);
        }

        return (diffs, changedCount);
    }

    private static void CollectJsonDiff(
        JsonElement baseEl,
        JsonElement effectiveEl,
        string path,
        List<string> diffs,
        bool includeUnchanged,
        ref int changedCount)
    {
        if (baseEl.ValueKind != effectiveEl.ValueKind)
        {
            diffs.Add($"~ {path}: kind {baseEl.ValueKind} . {effectiveEl.ValueKind}");
            changedCount++;
            return;
        }

        if (baseEl.ValueKind == JsonValueKind.Object)
        {
            var baseProps = baseEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            var effectiveProps = effectiveEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            var keys = baseProps.Keys.Union(effectiveProps.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal);

            foreach (var key in keys)
            {
                var nextPath = $"{path}.{key}";
                var hasBase = baseProps.TryGetValue(key, out var baseValue);
                var hasEffective = effectiveProps.TryGetValue(key, out var effectiveValue);

                if (!hasBase && hasEffective)
                {
                    diffs.Add($"+ {nextPath}: {effectiveValue.GetRawText()}");
                    changedCount++;
                    continue;
                }

                if (hasBase && !hasEffective)
                {
                    diffs.Add($"- {nextPath}: {baseValue.GetRawText()}");
                    changedCount++;
                    continue;
                }

                CollectJsonDiff(baseValue, effectiveValue, nextPath, diffs, includeUnchanged, ref changedCount);
            }

            return;
        }

        if (baseEl.ValueKind == JsonValueKind.Array)
        {
            var baseRaw = baseEl.GetRawText();
            var effectiveRaw = effectiveEl.GetRawText();
            if (!string.Equals(baseRaw, effectiveRaw, StringComparison.Ordinal))
            {
                diffs.Add($"~ {path}: {baseRaw} . {effectiveRaw}");
                changedCount++;
            }
            else if (includeUnchanged)
            {
                diffs.Add($"= {path}: {baseRaw}");
            }

            return;
        }

        var baseText = baseEl.GetRawText();
        var effectiveText = effectiveEl.GetRawText();
        if (!string.Equals(baseText, effectiveText, StringComparison.Ordinal))
        {
            diffs.Add($"~ {path}: {baseText} . {effectiveText}");
            changedCount++;
        }
        else if (includeUnchanged)
        {
            diffs.Add($"= {path}: {baseText}");
        }
    }
}
