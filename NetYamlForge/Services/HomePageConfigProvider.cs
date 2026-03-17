// ファイル概要: ホームページ表示設定（HomePageConfig）を YAML ファイルからロードするプロバイダーです。
// config/home-page.yml（グローバル）と projects/<name>/config/home-page.yml（プロジェクト上書き）をマージして返します。
// HomeController の Index アクションから IHomePageConfigProvider 経由で使用されます。

using NetYamlForge.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public interface IHomePageConfigProvider
{
    HomePageConfig GetConfig(string? projectName = null);
}

public class HomePageConfigProvider : IHomePageConfigProvider
{
    private readonly string _rootConfigPath;
    private readonly Dictionary<string, string> _projectConfigPaths;
    private readonly ILogger<HomePageConfigProvider> _logger;

    public HomePageConfigProvider(IWebHostEnvironment env, ILogger<HomePageConfigProvider> logger)
    {
        _logger = logger;
        _rootConfigPath = Path.Combine(env.ContentRootPath, "config", "home-page.yml");
        _projectConfigPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var projectsRoot = Path.Combine(env.ContentRootPath, "projects");
        if (Directory.Exists(projectsRoot))
        {
            foreach (var projectDir in Directory.GetDirectories(projectsRoot))
            {
                var projectName = Path.GetFileName(projectDir);
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    continue;
                }

                var projectConfigPath = Path.Combine(projectDir, "config", "home-page.yml");
                if (!File.Exists(projectConfigPath))
                {
                    // backward compatibility
                    projectConfigPath = Path.Combine(projectDir, "home-page.yml");
                }
                if (!File.Exists(projectConfigPath))
                {
                    continue;
                }

                _projectConfigPaths[projectName] = projectConfigPath;
            }
        }
    }

    private static HomePageConfig Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new HomePageConfig();
        }

        try
        {
            var yaml = File.ReadAllText(filePath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            return deserializer.Deserialize<HomePageConfig>(yaml) ?? new HomePageConfig();
        }
        catch
        {
            return new HomePageConfig();
        }
    }

    public HomePageConfig GetConfig(string? projectName = null)
    {
        if (!string.IsNullOrWhiteSpace(projectName)
            && _projectConfigPaths.TryGetValue(projectName, out var projectConfigPath))
        {
            var projectConfig = Load(projectConfigPath);
            _logger.LogDebug("home-page.yml loaded for project: {ProjectName} ({Path})", projectName, projectConfigPath);
            return projectConfig;
        }

        var rootConfig = Load(_rootConfigPath);
        _logger.LogDebug("home-page.yml loaded for root: {Path}", _rootConfigPath);
        return rootConfig;
    }
}
