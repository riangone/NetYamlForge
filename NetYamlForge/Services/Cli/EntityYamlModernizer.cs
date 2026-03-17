// ファイル概要: 既存 entities/*.yml を最新生成規約に合わせて自動補正します。

using System.Text;
using NetYamlForge.Models;
using NetYamlForge.Services.Cli;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public static class EntityYamlModernizer
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static int Run(string currentDir, string? projectFilter = null, CliScaffoldResult? result = null)
    {
        if (result != null && !string.IsNullOrWhiteSpace(projectFilter)) result.Project = projectFilter;
        var scaffoldExit = EntityYamlScaffolder.Run(currentDir, projectFilter, true, "entities.generated", false, result);
        if (scaffoldExit != 0)
        {
            return scaffoldExit;
        }

        var contentRoot = ResolveContentRoot(currentDir);
        var projectsRoot = Path.Combine(contentRoot, "projects");
        var projectDirs = Directory.GetDirectories(projectsRoot)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(projectFilter))
        {
            projectDirs = projectDirs
                .Where(d => string.Equals(Path.GetFileName(d), projectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        var touchedFiles = 0;
        foreach (var projectDir in projectDirs)
        {
            var generatedDir = Path.Combine(projectDir, "entities.generated");
            var customDir = Path.Combine(projectDir, "entities");
            if (!Directory.Exists(generatedDir) || !Directory.Exists(customDir))
            {
                continue;
            }

            var generatedMap = LoadEntityMap(deserializer, generatedDir);
            if (generatedMap.Count == 0)
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(customDir, "*.yml").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var yaml = File.ReadAllText(file);
                var root = deserializer.Deserialize<EntityConfigRoot>(yaml);
                if (root?.Entities == null || root.Entities.Count == 0)
                {
                    continue;
                }

                var changed = false;
                foreach (var entityEntry in root.Entities)
                {
                    if (!generatedMap.TryGetValue(entityEntry.Key, out var generated))
                    {
                        continue;
                    }

                    changed |= MergeEntity(entityEntry.Value, generated);
                }

                if (!changed)
                {
                    continue;
                }

                var normalized = serializer.Serialize(root);
                File.WriteAllText(file, normalized, Utf8NoBom);
                result?.GeneratedFiles.Add(file);
                touchedFiles++;
            }
        }

        Console.WriteLine($"done. modernized files: {touchedFiles}");
        return 0;
    }

    private static string ResolveContentRoot(string currentDir)
    {
        if (Directory.Exists(Path.Combine(currentDir, "projects")))
        {
            return currentDir;
        }

        var sub = Path.Combine(currentDir, "NetYamlForge");
        if (Directory.Exists(Path.Combine(sub, "projects")))
        {
            return sub;
        }

        throw new DirectoryNotFoundException($"content root を解決できません: {currentDir}");
    }

    private static Dictionary<string, EntityDefinition> LoadEntityMap(IDeserializer deserializer, string dir)
    {
        var result = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir, "*.yml").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var yaml = File.ReadAllText(file);
            var root = deserializer.Deserialize<EntityConfigRoot>(yaml);
            if (root?.Entities == null)
            {
                continue;
            }

            foreach (var kv in root.Entities)
            {
                result[kv.Key] = kv.Value;
            }
        }

        return result;
    }

    private static bool MergeEntity(EntityDefinition target, EntityDefinition generated)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(target.Table) && !string.IsNullOrWhiteSpace(generated.Table))
        {
            target.Table = generated.Table;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(target.Key) && !string.IsNullOrWhiteSpace(generated.Key))
        {
            target.Key = generated.Key;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(target.DisplayName) && !string.IsNullOrWhiteSpace(generated.DisplayName))
        {
            target.DisplayName = generated.DisplayName;
            changed = true;
        }

        foreach (var gcol in generated.Columns)
        {
            if (!target.Columns.TryGetValue(gcol.Key, out var tcol))
            {
                target.Columns[gcol.Key] = CloneColumn(gcol.Value);
                changed = true;
                continue;
            }

            if (gcol.Value.Required && !tcol.Required)
            {
                tcol.Required = true;
                changed = true;
            }

            if (gcol.Value.Identity && !tcol.Identity)
            {
                tcol.Identity = true;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(tcol.Type) && !string.IsNullOrWhiteSpace(gcol.Value.Type))
            {
                tcol.Type = gcol.Value.Type;
                changed = true;
            }
        }

        foreach (var gform in generated.Forms)
        {
            if (!target.Forms.TryGetValue(gform.Key, out var tform))
            {
                target.Forms[gform.Key] = CloneForm(gform.Value);
                changed = true;
                continue;
            }

            if (gform.Value.Required && !tform.Required)
            {
                tform.Required = true;
                changed = true;
            }

            if (gform.Value.Identity && !tform.Identity)
            {
                tform.Identity = true;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(tform.Type) && !string.IsNullOrWhiteSpace(gform.Value.Type))
            {
                tform.Type = gform.Value.Type;
                changed = true;
            }

            if (tform.ForeignKey == null && gform.Value.ForeignKey != null)
            {
                tform.ForeignKey = CloneForeignKey(gform.Value.ForeignKey);
                changed = true;
            }
        }

        if (target.Layout.Forms.Order.Count == 0 && generated.Layout.Forms.Order.Count > 0)
        {
            target.Layout.Forms.Order = generated.Layout.Forms.Order.ToList();
            changed = true;
        }

        if (target.Layout.Filters.Order.Count == 0 && generated.Layout.Filters.Order.Count > 0)
        {
            target.Layout.Filters.Order = generated.Layout.Filters.Order.ToList();
            changed = true;
        }

        return changed;
    }

    private static ColumnDefinition CloneColumn(ColumnDefinition src) => new()
    {
        Type = src.Type,
        Identity = src.Identity,
        Required = src.Required,
        Label = src.Label,
        LabelKey = src.LabelKey,
        Searchable = src.Searchable,
        Sortable = src.Sortable,
        Editable = src.Editable,
        Expression = src.Expression,
        ForeignKey = src.ForeignKey == null ? null : CloneForeignKey(src.ForeignKey),
        Options = src.Options?.ToList(),
        LabelI18n = src.LabelI18n == null ? null : new Dictionary<string, string>(src.LabelI18n, StringComparer.Ordinal),
        Hidden = src.Hidden
    };

    private static FormDefinition CloneForm(FormDefinition src) => new()
    {
        Type = src.Type,
        Identity = src.Identity,
        Required = src.Required,
        Label = src.Label,
        LabelKey = src.LabelKey,
        Searchable = src.Searchable,
        Sortable = src.Sortable,
        Editable = src.Editable,
        Expression = src.Expression,
        ForeignKey = src.ForeignKey == null ? null : CloneForeignKey(src.ForeignKey),
        Options = src.Options?.ToList(),
        LabelI18n = src.LabelI18n == null ? null : new Dictionary<string, string>(src.LabelI18n, StringComparer.Ordinal),
        Hidden = src.Hidden
    };

    private static ForeignKeyDefinition CloneForeignKey(ForeignKeyDefinition src) => new()
    {
        Entity = src.Entity,
        DisplayColumn = src.DisplayColumn,
        DisplayColumns = src.DisplayColumns?.ToList(),
        Query = src.Query,
        Picker = src.Picker,
        MultiPicker = src.MultiPicker
    };
}
