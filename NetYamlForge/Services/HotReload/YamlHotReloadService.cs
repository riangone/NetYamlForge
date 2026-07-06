using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.HotReload;

public class HotReloadOptions
{
    public const string SectionName = "HotReload";
    public bool Enabled { get; set; } = true;
    public bool OnlyInDevelopment { get; set; } = false;
    public bool EnableCsHotReload { get; set; } = false;
    public int DebounceMs { get; set; } = 500;
}

public class YamlHotReloadService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IYamlFileWatcher _fileWatcher;
    private readonly ProjectYamlCacheManager _cacheManager;
    private readonly HotReloadOptions _options;
    private readonly ILogger<YamlHotReloadService> _logger;
    private bool _disposed;

    public YamlHotReloadService(
        IServiceProvider serviceProvider,
        IYamlFileWatcher fileWatcher,
        ProjectYamlCacheManager cacheManager,
        IOptions<HotReloadOptions> options,
        ILogger<YamlHotReloadService> logger)
    {
        _serviceProvider = serviceProvider;
        _fileWatcher = fileWatcher;
        _cacheManager = cacheManager;
        _options = options.Value;
        _logger = logger;
        _fileWatcher.FileChanged += OnFileChanged;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("YAML ホットリロードは無効化されています");
            return Task.CompletedTask;
        }

        if (_options.OnlyInDevelopment && !IsDevelopment())
        {
            _logger.LogInformation("YAML ホットリロードは開発環境でのみ有効です");
            return Task.CompletedTask;
        }

        _logger.LogInformation("YAML ホットリロードサービスを開始 (CsReload={CsEnabled})", _options.EnableCsHotReload);
        var projectsDir = Path.Combine(Directory.GetCurrentDirectory(), "projects");
        if (Directory.Exists(projectsDir))
        {
            foreach (var projectDir in Directory.GetDirectories(projectsDir))
                _fileWatcher.StartWatching(projectDir, _options.EnableCsHotReload);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("YAML ホットリロードサービスを停止");
        _fileWatcher.StopAll();
        return Task.CompletedTask;
    }

    private async void OnFileChanged(object? sender, YamlFileChangedEventArgs e)
    {
        try
        {
            _logger.LogInformation("YAML ファイル変更を検知：{File}", e.FilePath);
            await ReloadAffectedCacheAsync(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YAML ホットリロード処理中にエラーが発生");
        }
    }

    private async Task ReloadAffectedCacheAsync(YamlFileChangedEventArgs e)
    {
        var filePath = e.FilePath;
        var projectName = e.ProjectName;

        if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("C# コードファイルの変更を検知：{File}。リロードを開始します...", filePath);
            using var scope = _serviceProvider.CreateScope();
            var projectManager = scope.ServiceProvider.GetRequiredService<ProjectManager>();
            var hookLoader = scope.ServiceProvider.GetRequiredService<IProjectHookLoader>();
            var hookRegistry = scope.ServiceProvider.GetRequiredService<IProjectHookRegistry>();
            var bizRegistry = scope.ServiceProvider.GetRequiredService<IProjectBusinessLogicRegistry>();
            var actionRegistry = scope.ServiceProvider.GetRequiredService<IProjectActionRegistry>();

            if (projectManager.TryGet(projectName, out var projectInfo) && projectInfo != null)
            {
                var projectDir = projectInfo.ProjectDir;
                
                // 1. 古いアセンブリと登録を解除
                await hookLoader.UnloadProjectAssemblyAsync(projectName);

                // 2. 再コンパイルしてロード
                await hookLoader.LoadProjectHooksAsync(projectName, projectDir, hookRegistry);
                await hookLoader.LoadProjectBusinessLogicAsync(projectName, projectDir, bizRegistry);
                await hookLoader.LoadProjectActionHandlersAsync(projectName, projectDir, actionRegistry);

                _logger.LogInformation("プロジェクト '{Project}' の C# コードリロードが完了しました", projectName);
            }
            else
            {
                _logger.LogWarning("プロジェクト '{Project}' が見つからないため、C# コードリロードをスキップします", projectName);
            }
            return;
        }

        if (filePath.EndsWith("scenarios.yaml", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith("scenarios.yml", StringComparison.OrdinalIgnoreCase))
        {
            using var scope = _serviceProvider.CreateScope();
            var scenarioLoader = scope.ServiceProvider.GetRequiredService<IAiScenarioYamlLoader>();
            scenarioLoader.Reload(projectName);
            _logger.LogInformation("プロジェクト '{Project}' の AI シナリオ設定がホットリロードされました：{File}", projectName, filePath);
            return;
        }

        if (filePath.Contains("/entities/", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("\\entities\\", StringComparison.OrdinalIgnoreCase))
        {
            await _cacheManager.ReloadAsync($"{projectName}_entities", filePath);
        }
        else if (filePath.EndsWith("dashboard.yml", StringComparison.OrdinalIgnoreCase) ||
                 filePath.EndsWith("dashboard.yaml", StringComparison.OrdinalIgnoreCase))
        {
            await _cacheManager.ReloadAsync($"{projectName}_dashboard", filePath);
        }
        else if (filePath.Contains("/pages/", StringComparison.OrdinalIgnoreCase) ||
                 filePath.Contains("\\pages\\", StringComparison.OrdinalIgnoreCase))
        {
            await _cacheManager.ReloadAsync($"{projectName}_pages", filePath);
        }
        else if (filePath.Contains("/config/", StringComparison.OrdinalIgnoreCase) ||
                 filePath.EndsWith("project.yaml", StringComparison.OrdinalIgnoreCase) ||
                 filePath.EndsWith("project.yml", StringComparison.OrdinalIgnoreCase))
        {
            await _cacheManager.ReloadProjectAsync(projectName);
        }
    }

    private bool IsDevelopment()
    {
        var env = _serviceProvider.GetService<IHostEnvironment>();
        return env?.IsDevelopment() ?? false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _fileWatcher.FileChanged -= OnFileChanged;
        _fileWatcher.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
