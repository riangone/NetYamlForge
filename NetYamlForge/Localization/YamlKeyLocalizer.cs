// ファイル概要: YAML ファイル（config/i18n.yml / projects/<name>/config/i18n.yml）からキー翻訳を解決する静的ユーティリティです。
// 解決優先順: プロジェクト固有カタログ → グローバルカタログ → 言語サブタグ → en-US フォールバック。
// スレッドセーフなダブルチェックロック方式でアプリ起動後に一度だけロードします。

using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Localization;

public static class YamlKeyLocalizer
{
    private sealed class KeyTranslationRoot
    {
        public Dictionary<string, Dictionary<string, string>> Translations { get; set; } =
            new(StringComparer.Ordinal);
    }

    private static readonly object Sync = new();
    private static bool _loaded;
    private static Dictionary<string, Dictionary<string, string>> _globalTranslations =
        new(StringComparer.Ordinal);
    private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> _projectTranslations =
        new(StringComparer.OrdinalIgnoreCase);

    public static string? Resolve(string? key, CultureInfo culture, string? projectName = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        EnsureLoaded();

        if (!string.IsNullOrWhiteSpace(projectName)
            && _projectTranslations.TryGetValue(projectName, out var projectCatalog))
        {
            var projectText = ResolveFromCatalog(projectCatalog, key, culture);
            if (!string.IsNullOrWhiteSpace(projectText))
            {
                return projectText;
            }
        }

        return ResolveFromCatalog(_globalTranslations, key, culture);
    }

    private static string? ResolveFromCatalog(
        Dictionary<string, Dictionary<string, string>> catalog,
        string key,
        CultureInfo culture)
    {
        if (!catalog.TryGetValue(key, out var textMap) || textMap.Count == 0)
        {
            return null;
        }

        var exact = culture.Name;
        if (textMap.TryGetValue(exact, out var exactText) && !string.IsNullOrWhiteSpace(exactText))
        {
            return exactText;
        }

        var neutral = culture.TwoLetterISOLanguageName;
        var neutralPair = textMap.FirstOrDefault(x =>
            x.Key.StartsWith(neutral, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.Value));
        if (!string.IsNullOrWhiteSpace(neutralPair.Value))
        {
            return neutralPair.Value;
        }

        if (textMap.TryGetValue("en-US", out var enUs) && !string.IsNullOrWhiteSpace(enUs))
        {
            return enUs;
        }

        if (textMap.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        return textMap.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (Sync)
        {
            if (_loaded)
            {
                return;
            }

            var contentRoot = ResolveContentRoot();
            if (!string.IsNullOrWhiteSpace(contentRoot))
            {
                _globalTranslations = LoadCatalog(Path.Combine(contentRoot, "config", "i18n.yml"));
                _projectTranslations = LoadProjectCatalogs(Path.Combine(contentRoot, "projects"));
            }

            _loaded = true;
        }
    }

    private static string? ResolveContentRoot()
    {
        static IEnumerable<string> Ascend(string start)
        {
            var current = start;
            while (!string.IsNullOrWhiteSpace(current))
            {
                yield return current;
                var parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                {
                    break;
                }
                current = parent;
            }
        }

        var candidates = new List<string>();
        var cwd = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            candidates.AddRange(Ascend(cwd));
        }

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            candidates.AddRange(Ascend(baseDir));
        }

        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var configDir = Path.Combine(dir, "config");
            if (Directory.Exists(configDir))
            {
                return dir;
            }

            var nested = Path.Combine(dir, "NetYamlForge");
            if (Directory.Exists(Path.Combine(nested, "config")))
            {
                return nested;
            }
        }

        return null;
    }

    private static Dictionary<string, Dictionary<string, string>> LoadCatalog(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }

        try
        {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var root = deserializer.Deserialize<KeyTranslationRoot>(yaml) ?? new KeyTranslationRoot();
            return root.Translations ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> LoadProjectCatalogs(string projectsDir)
    {
        var result = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(projectsDir))
        {
            return result;
        }

        foreach (var projectDir in Directory.GetDirectories(projectsDir))
        {
            var projectName = Path.GetFileName(projectDir);
            if (string.IsNullOrWhiteSpace(projectName))
            {
                continue;
            }

            var catalogPath = Path.Combine(projectDir, "config", "i18n.yml");
            var catalog = LoadCatalog(catalogPath);
            if (catalog.Count == 0)
            {
                // backward compatibility
                catalog = LoadCatalog(Path.Combine(projectDir, "i18n.yml"));
            }
            if (catalog.Count > 0)
            {
                result[projectName] = catalog;
            }
        }

        return result;
    }
}
