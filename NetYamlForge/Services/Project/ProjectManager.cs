// ファイル概要：projects/ ディレクトリをスキャンし、全プロジェクトの ProjectInfo をキャッシュします。
// Singleton として登録し、起動時に一度だけ初期化されます。

using NetYamlForge.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

public class ProjectManager
{
    private readonly Dictionary<string, ProjectInfo> _projects;
    private readonly ILogger<ProjectManager> _logger;
    private readonly IProjectHookRegistry _projectHookRegistry;
    private readonly IProjectHookLoader _projectHookLoader;
    private readonly IProjectBusinessLogicRegistry _projectBusinessLogicRegistry;

    public ProjectManager(
        IWebHostEnvironment env,
        ILogger<ProjectManager> logger,
        IProjectHookRegistry projectHookRegistry,
        IProjectHookLoader projectHookLoader,
        IProjectBusinessLogicRegistry projectBusinessLogicRegistry)
    {
        _logger = logger;
        _projectHookRegistry = projectHookRegistry;
        _projectHookLoader = projectHookLoader;
        _projectBusinessLogicRegistry = projectBusinessLogicRegistry;
        _projects = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);

        var projectsRoot = Path.Combine(env.ContentRootPath, "projects");
        if (!Directory.Exists(projectsRoot))
        {
            _logger.LogWarning("projects/ ディレクトリが見つかりません：{Path}", projectsRoot);
            return;
        }

        var loadErrors = new List<string>();
        foreach (var projectDir in Directory.GetDirectories(projectsRoot))
        {
            var yamlPath = Path.Combine(projectDir, "project.yaml");
            if (!File.Exists(yamlPath))
            {
                _logger.LogWarning("project.yaml が見つかりません（スキップ）：{Dir}", projectDir);
                continue;
            }

            try
            {
                LoadProjectAsync(projectDir, yamlPath).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "プロジェクト読み込みエラー：{Dir}", projectDir);
                var projectName = Path.GetFileName(projectDir);
                loadErrors.Add($"- {projectName}: {ex.Message}");
            }
        }

        if (loadErrors.Count > 0)
        {
            var details = string.Join(Environment.NewLine, loadErrors);
            throw new InvalidOperationException(
                $"プロジェクト設定の読み込みに失敗しました。以下を修正してください。{Environment.NewLine}{details}");
        }

        _logger.LogInformation("プロジェクト読み込み完了：{Count} 件 ({Names})",
            _projects.Count, string.Join(", ", _projects.Keys));
    }

    private async Task LoadProjectAsync(string projectDir, string yamlPath)
    {
        var yamlContent = File.ReadAllText(yamlPath);

        // JSON Schema で検証
        YamlSchemaValidator.ValidateProjectYaml(yamlContent, yamlPath);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var strictDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var config = deserializer.Deserialize<ProjectConfig>(yamlContent);

        // layout.yml から追加設定を読み込み（存在する場合）
        // 優先: config/layout.yml、互換: layout.yml
        var layoutYamlPath = Path.Combine(projectDir, "config", "layout.yml");
        if (!File.Exists(layoutYamlPath))
        {
            layoutYamlPath = Path.Combine(projectDir, "layout.yml");
        }
        if (File.Exists(layoutYamlPath))
        {
            var layoutYamlContent = File.ReadAllText(layoutYamlPath);
            // layout.yml は strict デシリアライズで未知キーを検出します。
            var layoutConfig = strictDeserializer.Deserialize<ProjectLayoutConfig>(layoutYamlContent);

            // layout.yml の設定が優先される
            if (layoutConfig != null)
            {
                config.Layout = layoutConfig;
                _logger.LogInformation("プロジェクト '{Name}' に layout.yml を適用", config.Name);
            }
        }

        var dbType = (config.Database.Type ?? "sqlite").ToLowerInvariant();
        var connectionString = BuildConnectionString(config, projectDir, dbType);

        var entityMetadata = new EntityMetadataProvider(projectDir, dbType);
        var dashboardConfig = new DashboardConfigProvider(projectDir);
        var pageMetadata = new PageMetadataProvider(projectDir);
        EntityDbSchemaConsistencyValidator.ValidateOrThrow(config.Name, dbType, connectionString, entityMetadata);

        var info = new ProjectInfo
        {
            Name = config.Name,
            DisplayName = config.DisplayName ?? config.Name,
            ProjectDir = projectDir,
            DatabaseType = dbType,
            ConnectionString = connectionString,
            EntityMetadata = entityMetadata,
            DashboardConfig = dashboardConfig,
            PageMetadata = pageMetadata,
            Layout = config.Layout,
            Calendar = config.Calendar
        };

        _projects[config.Name] = info;
        _logger.LogInformation("プロジェクト登録：{Name} ({DisplayName}), DB={DbType}",
            info.Name, info.DisplayName, info.DatabaseType);

        // プロジェクト固有フックを読み込み
        await _projectHookLoader.LoadProjectHooksAsync(config.Name, projectDir, _projectHookRegistry);

        // プロジェクト固有ビジネスロジックを読み込み
        await _projectHookLoader.LoadProjectBusinessLogicAsync(config.Name, projectDir, _projectBusinessLogicRegistry);
    }

    private static string BuildConnectionString(ProjectConfig config, string projectDir, string dbType)
    {
        if (dbType == "sqlserver"
            || dbType == "postgresql"
            || dbType == "postgres"
            || dbType == "mysql"
            || dbType == "mariadb")
        {
            return config.Database.ConnectionString
                ?? throw new InvalidOperationException(
                    $"プロジェクト '{config.Name}' の database.connectionString が未設定です。");
        }

        // SQLite: path が相対パスの場合は projectDir 基準で解決
        if (!string.IsNullOrWhiteSpace(config.Database.Path))
        {
            var dbPath = Path.IsPathRooted(config.Database.Path)
                ? config.Database.Path
                : Path.GetFullPath(Path.Combine(projectDir, config.Database.Path));
            return $"Data Source={dbPath}";
        }

        // フォールバック：database/ サブディレクトリ内の {name}.db
        var defaultPath = Path.Combine(projectDir, "database", $"{config.Name}.db");
        return $"Data Source={defaultPath}";
    }

    public bool TryGet(string name, out ProjectInfo? info) =>
        _projects.TryGetValue(name, out info);

    public IReadOnlyCollection<ProjectInfo> GetAll() => _projects.Values;
}
