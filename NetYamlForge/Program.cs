// ファイル概要：アプリケーションのエントリポイント。DI、認証、ローカライズ、ログ、ルーティングを初期化します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using NetYamlForge.Data;
using NetYamlForge.Data.Schemas;
using NetYamlForge.Extensions;
using NetYamlForge.Hubs;
using NetYamlForge.Middleware;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Cli;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.AI.Providers;
using NetYamlForge.AI.Client;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;

var jsonMode = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));

// Windows サービスとして実行するかどうか（--run-as-service フラグまたは環境変数）
var useWindowsService = args.Any(a => a.Equals("--run-as-service", StringComparison.OrdinalIgnoreCase))
    || Environment.GetEnvironmentVariable("DOTNET_RUNNING_AS_WINDOWS_SERVICE") == "true";

if (useWindowsService)
{
    args = args.Where(a => a != "--run-as-service").ToArray();
}

if (args.Any(a => a.Equals("--scaffold-entities", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var overwrite = !args.Any(a => a.Equals("--no-overwrite", StringComparison.OrdinalIgnoreCase));
    var outputDirArg = args.FirstOrDefault(a => a.StartsWith("--output-dir=", StringComparison.OrdinalIgnoreCase));
    var outputDirName = outputDirArg?.Split('=', 2).ElementAtOrDefault(1);
    var withLabelKeys = args.Any(a => a.Equals("--with-label-keys", StringComparison.OrdinalIgnoreCase));
    var scaffoldResult = new CliScaffoldResult { Command = "scaffold-entities" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = EntityYamlScaffolder.Run(
        Directory.GetCurrentDirectory(),
        projectName,
        overwrite,
        string.IsNullOrWhiteSpace(outputDirName) ? "entities.generated" : outputDirName,
        withLabelKeys,
        scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

if (args.Any(a => a.Equals("--scaffold-hook", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var nameArg = args.FirstOrDefault(a => a.StartsWith("--name=", StringComparison.OrdinalIgnoreCase));
    var hookName = nameArg?.Split('=', 2).ElementAtOrDefault(1);
    var withTests = args.Any(a => a.Equals("--with-tests", StringComparison.OrdinalIgnoreCase));
    var scaffoldResult = new CliScaffoldResult { Command = "scaffold-hook" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = HookScaffolder.Run(
        Directory.GetCurrentDirectory(),
        projectName,
        hookName,
        withTests,
        scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

if (args.Any(a => a.Equals("--upgrade-entity-yaml", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var scaffoldResult = new CliScaffoldResult { Command = "upgrade-entity-yaml" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = EntityYamlModernizer.Run(Directory.GetCurrentDirectory(), projectName, scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

if (args.Any(a => a.Equals("--init-project", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var displayNameArg = args.FirstOrDefault(a => a.StartsWith("--display-name=", StringComparison.OrdinalIgnoreCase));
    var displayName = displayNameArg?.Split('=', 2).ElementAtOrDefault(1);
    var dbTypeArg = args.FirstOrDefault(a => a.StartsWith("--db-type=", StringComparison.OrdinalIgnoreCase));
    var dbType = dbTypeArg?.Split('=', 2).ElementAtOrDefault(1);
    var dbPathArg = args.FirstOrDefault(a => a.StartsWith("--db-path=", StringComparison.OrdinalIgnoreCase));
    var dbPath = dbPathArg?.Split('=', 2).ElementAtOrDefault(1);
    var dbConnectionArg = args.FirstOrDefault(a => a.StartsWith("--db-connection=", StringComparison.OrdinalIgnoreCase));
    var dbConnection = dbConnectionArg?.Split('=', 2).ElementAtOrDefault(1);
    var i18nFallbackModeArg = args.FirstOrDefault(a => a.StartsWith("--i18n-fallback-mode=", StringComparison.OrdinalIgnoreCase));
    var i18nFallbackMode = i18nFallbackModeArg?.Split('=', 2).ElementAtOrDefault(1);
    var autoScaffold = !args.Any(a => a.Equals("--no-auto-scaffold", StringComparison.OrdinalIgnoreCase));
    var force = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));
    var scaffoldResult = new CliScaffoldResult { Command = "init-project" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = ProjectTemplateScaffolder.Run(
        Directory.GetCurrentDirectory(),
        projectName,
        displayName,
        force,
        dbType,
        dbPath,
        dbConnection,
        autoScaffold,
        i18nFallbackMode,
        scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

if (args.Any(a => a.Equals("--ai-generate", StringComparison.OrdinalIgnoreCase)))
{
    var promptArg = args.FirstOrDefault(a => a.StartsWith("--prompt=", StringComparison.OrdinalIgnoreCase));
    var prompt = promptArg?.Split('=', 2).ElementAtOrDefault(1) 
        ?? throw new ArgumentException("--prompt 参数是必需的");
    
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    
    var targetDirArg = args.FirstOrDefault(a => a.StartsWith("--target-dir=", StringComparison.OrdinalIgnoreCase));
    var targetDir = targetDirArg?.Split('=', 2).ElementAtOrDefault(1);
    
    var timeoutArg = args.FirstOrDefault(a => a.StartsWith("--timeout=", StringComparison.OrdinalIgnoreCase));
    int? timeout = null;
    if (timeoutArg != null)
    {
        var timeoutStr = timeoutArg.Split('=', 2).ElementAtOrDefault(1);
        if (int.TryParse(timeoutStr, out var parsedTimeout))
        {
            timeout = parsedTimeout;
        }
    }
    
    var pipelineMode = args.Any(a => a.Equals("--pipeline-mode=single", StringComparison.OrdinalIgnoreCase)) 
        ? "single" : "full";
    
    Console.WriteLine($"[AI-Pipeline] 开始生成 - Prompt: {prompt}");
    if (!string.IsNullOrEmpty(projectName))
        Console.WriteLine($"[AI-Pipeline] 项目: {projectName}");
    if (!string.IsNullOrEmpty(targetDir))
        Console.WriteLine($"[AI-Pipeline] 目标目录: {targetDir}");
    
    // 创建临时服务提供者
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information))
        .AddOptions()
        .Configure<AiPipelineConfig>(b => 
        {
            b.HarnessDirectory = "/home/ubuntu/ws/harness-new";
            b.HarnessWorkDirectory = "/tmp/nyf-harness";
            b.PythonExecutable = "python3";
            b.DefaultTimeoutSeconds = timeout ?? 3600;
        })
        .AddHttpClient<AiPipelineService>(c => c.Timeout = TimeSpan.FromHours(1));
        
    var services = serviceCollection.BuildServiceProvider();
    
    var pipelineService = services.GetRequiredService<AiPipelineService>();
    var cts = new CancellationTokenSource();
    
    var result = await pipelineService.ExecutePipelineAsync(
        prompt: prompt,
        projectName: projectName,
        targetProjectDir: targetDir,
        timeout: timeout,
        ct: cts.Token);
    
    if (result.Success)
    {
        Console.WriteLine($"\n[AI-Pipeline] ✅ 生成成功!");
        Console.WriteLine($"[AI-Pipeline] 工作目录: {result.WorkDirectory}");
        if (result.GeneratedFiles.Count > 0)
        {
            Console.WriteLine($"[AI-Pipeline] 生成的文件:");
            foreach (var file in result.GeneratedFiles)
                Console.WriteLine($"  - {file}");
        }
        Environment.Exit(0);
    }
    else
    {
        Console.WriteLine($"\n[AI-Pipeline] ❌ 生成失败: {result.ErrorMessage}");
        if (result.Logs.Count > 0)
        {
            Console.WriteLine("\n[AI-Pipeline] 日志:");
            foreach (var log in result.Logs.TakeLast(20))
                Console.WriteLine($"  {log}");
        }
        Environment.Exit(1);
    }
    return;
}

if (args.Any(a => a.Equals("--scaffold-batch-job", StringComparison.OrdinalIgnoreCase)))
{
    var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
    var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
    var nameArg = args.FirstOrDefault(a => a.StartsWith("--name=", StringComparison.OrdinalIgnoreCase));
    var jobName = nameArg?.Split('=', 2).ElementAtOrDefault(1);
    var scaffoldResult = new CliScaffoldResult { Command = "scaffold-batch-job" };
    if (jsonMode) Console.SetOut(TextWriter.Null);
    var exitCode = BatchJobScaffolder.Run(
        Directory.GetCurrentDirectory(),
        projectName,
        jobName ?? "sample_job",
        scaffoldResult);
    if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
    Environment.Exit(exitCode);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// ユーザーが管理画面から変更した AI 設定を読み込む（appsettings.json を上書き）
builder.Configuration.AddJsonFile("ai-user-settings.json", optional: true, reloadOnChange: true);

// Windows サービスとして実行する場合
if (useWindowsService)
{
    builder.Host.UseWindowsService();
}

builder.Host.UseSerilog((context, cfg) =>
{
    cfg.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
});

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddControllersWithViews()
    .AddViewLocalization();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.Razor.RazorViewEngineOptions>(options =>
{
    options.ViewLocationExpanders.Add(new NetYamlForge.Services.ProjectViewLocationExpander());
});

// ===== 多租户认证服务 =====
builder.Services.AddScoped<NetYamlForge.Services.Tenant.ITenantUserService, NetYamlForge.Services.Tenant.TenantUserService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/TenantAccount/Login";
        options.LogoutPath = "/TenantAccount/Logout";
        options.AccessDeniedPath = "/TenantAccount/AccessDenied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ===== サービス登録（Extensions/ServiceCollectionExtensions.cs に委譲）=====
// グループ別の詳細は AddNetYamlForge の各メソッドを参照してください。
builder.Services.AddNetYamlForge();

// ===== AI Assistant Services =====
// 双モード対応：
// - embedded (デフォルト): AI サービスを主プロセス内で実行
// - standalone: 独立プロセスの AI サービスを HTTP API + SignalR で呼び出し
var aiMode = builder.Configuration["AI:Mode"] ?? "embedded";

if (aiMode.Equals("standalone", StringComparison.OrdinalIgnoreCase))
{
    // 独立进程模式：使用 HTTP API クライアント
    builder.Services.AddHttpClient<AIServiceClient>(client =>
    {
        var baseUrl = builder.Configuration["AI:ServiceBaseUrl"] ?? "http://localhost:5200";
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromMinutes(30);
    });
    builder.Services.AddSingleton(sp => sp.GetRequiredService<AIServiceClient>());
    Log.Information("AI服务模式：独立进程 (HTTP API @ {BaseUrl})", builder.Configuration["AI:ServiceBaseUrl"]);
}
else
{
    // 嵌入式模式：直接在主进程内运行 AI 服务（现有逻辑保持不变）
builder.Services.Configure<CliConfig>(builder.Configuration.GetSection(CliConfig.SectionName));
builder.Services.AddSingleton<ProcessExecutor>();
builder.Services.AddSingleton<SkillLoader>();
builder.Services.AddSingleton<CLIServiceFactory>();
builder.Services.AddSingleton<AISettingsService>();
builder.Services.AddSingleton<ProgressTracker>();
builder.Services.AddSingleton<TaskQueueService>();

// HTTP Client for Ollama and LM Studio (本地模型)
builder.Services.AddHttpClient("OllamaClient");
builder.Services.AddHttpClient("LmStudioClient");

// AI 窓口チャットサービス (auto-dealer-demo)
// グローバル AI と共通のサービス（CLIServiceFactory, SkillLoader, TaskQueueService, ProgressTracker）を再利用
builder.Services.AddScoped<NetYamlForge.Services.AI.AutoDealerChatService>();

// JPiere 契約サービス AI チャットサービス
builder.Services.AddScoped<NetYamlForge.Services.AI.JpiereChatService>();

// AI 自然言語クエリサービス（QueryParserService が依存する ILlmProvider を含む）
// HybridLlmProvider: API ファースト + CLI フォールバック（高速応答）
builder.Services.AddScoped<NetYamlForge.Services.AI.Providers.ILlmProvider,
                           NetYamlForge.Services.AI.Providers.HybridLlmProvider>();
builder.Services.AddScoped<NetYamlForge.Services.AI.QueryParserService>();
builder.Services.AddScoped<NetYamlForge.Services.AI.QueryExecutionService>();
builder.Services.AddScoped<NetYamlForge.Services.AI.QueryResultFormatter, NetYamlForge.Services.AI.QueryResultFormatter>();

// 意図分類器（試乗予約Slot-filling用）
builder.Services.AddScoped<NetYamlForge.Services.AI.IIntentClassifier,
                           NetYamlForge.Services.AI.HybridIntentClassifier>();

// スロットフィリングマネージャー（試乗予約情報収集用）
builder.Services.AddSingleton<NetYamlForge.Services.AI.ISlotFillingManager,
                              NetYamlForge.Services.AI.SlotFillingManager>();

// AI CLI Services
// 进程池管理器（需要在 CLI 服务之前注册）
var processPoolConfig = new NetYamlForge.Services.AI.CliProcessPoolConfig();
builder.Configuration.GetSection("AICli:ProcessPool").Bind(processPoolConfig);
builder.Services.AddSingleton(processPoolConfig);
builder.Services.AddSingleton<NetYamlForge.Services.AI.AIProcessPoolManager>();

// 常驻进程聊天服务工厂
builder.Services.AddSingleton<NetYamlForge.Services.AI.DaemonChatServiceFactory>(sp =>
{
    return new NetYamlForge.Services.AI.DaemonChatServiceFactory(
        sp.GetRequiredService<ProcessExecutor>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetYamlForge.Services.AI.CliConfig>>(),
        sp.GetRequiredService<SkillLoader>(),
        sp.GetRequiredService<ILoggerFactory>());
});

// 注册装饰器包装的 CLI 服务（进程池支持）
builder.Services.AddSingleton<ICLIService>(sp =>
{
    var inner = new ClaudeCLIService(
        sp.GetRequiredService<ProcessExecutor>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetYamlForge.Services.AI.CliConfig>>(),
        sp.GetRequiredService<SkillLoader>(),
        sp.GetRequiredService<ILogger<ClaudeCLIService>>());

    var poolManager = sp.GetRequiredService<NetYamlForge.Services.AI.AIProcessPoolManager>();
    var executor = sp.GetRequiredService<ProcessExecutor>();
    var daemonFactory = sp.GetRequiredService<NetYamlForge.Services.AI.DaemonChatServiceFactory>();
    var logger = sp.GetRequiredService<ILogger<NetYamlForge.Services.AI.PooledCLIService>>();

    return new NetYamlForge.Services.AI.PooledCLIService(
        inner, poolManager, executor, processPoolConfig, daemonFactory, logger);
});

builder.Services.AddSingleton<ICLIService>(sp =>
{
    var inner = new QwenCodeCLIService(
        sp.GetRequiredService<ProcessExecutor>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetYamlForge.Services.AI.CliConfig>>(),
        sp.GetRequiredService<SkillLoader>(),
        sp.GetRequiredService<ILogger<QwenCodeCLIService>>());

    var poolManager = sp.GetRequiredService<NetYamlForge.Services.AI.AIProcessPoolManager>();
    var executor = sp.GetRequiredService<ProcessExecutor>();
    var daemonFactory = sp.GetRequiredService<NetYamlForge.Services.AI.DaemonChatServiceFactory>();
    var logger = sp.GetRequiredService<ILogger<NetYamlForge.Services.AI.PooledCLIService>>();

    return new NetYamlForge.Services.AI.PooledCLIService(
        inner, poolManager, executor, processPoolConfig, daemonFactory, logger);
});

builder.Services.AddSingleton<ICLIService>(sp =>
{
    var inner = new MockCLIService(
        sp.GetRequiredService<ProcessExecutor>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetYamlForge.Services.AI.CliConfig>>(),
        sp.GetRequiredService<SkillLoader>(),
        sp.GetRequiredService<ILogger<MockCLIService>>());

    var poolManager = sp.GetRequiredService<NetYamlForge.Services.AI.AIProcessPoolManager>();
    var executor = sp.GetRequiredService<ProcessExecutor>();
    var daemonFactory = sp.GetRequiredService<NetYamlForge.Services.AI.DaemonChatServiceFactory>();
    var logger = sp.GetRequiredService<ILogger<NetYamlForge.Services.AI.PooledCLIService>>();

    return new NetYamlForge.Services.AI.PooledCLIService(
        inner, poolManager, executor, processPoolConfig, daemonFactory, logger);
});

builder.Services.AddSingleton<ICLIService>(sp =>
{
    var inner = new CodexCLIService(
        sp.GetRequiredService<ProcessExecutor>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetYamlForge.Services.AI.CliConfig>>(),
        sp.GetRequiredService<SkillLoader>(),
        sp.GetRequiredService<ILogger<CodexCLIService>>());

    var poolManager = sp.GetRequiredService<NetYamlForge.Services.AI.AIProcessPoolManager>();
    var executor = sp.GetRequiredService<ProcessExecutor>();
    var daemonFactory = sp.GetRequiredService<NetYamlForge.Services.AI.DaemonChatServiceFactory>();
    var logger = sp.GetRequiredService<ILogger<NetYamlForge.Services.AI.PooledCLIService>>();

    return new NetYamlForge.Services.AI.PooledCLIService(
        inner, poolManager, executor, processPoolConfig, daemonFactory, logger);
});

builder.Services.AddSingleton<ICLIService>(sp =>
{
    var inner = new GeminiCLIService(
        sp.GetRequiredService<ProcessExecutor>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetYamlForge.Services.AI.CliConfig>>(),
        sp.GetRequiredService<SkillLoader>(),
        sp.GetRequiredService<ILogger<GeminiCLIService>>());

    var poolManager = sp.GetRequiredService<NetYamlForge.Services.AI.AIProcessPoolManager>();
    var executor = sp.GetRequiredService<ProcessExecutor>();
    var daemonFactory = sp.GetRequiredService<NetYamlForge.Services.AI.DaemonChatServiceFactory>();
    var logger = sp.GetRequiredService<ILogger<NetYamlForge.Services.AI.PooledCLIService>>();

    return new NetYamlForge.Services.AI.PooledCLIService(
        inner, poolManager, executor, processPoolConfig, daemonFactory, logger);
});

builder.Services.AddSingleton<ICLIService>(sp =>
{
    var inner = new OllamaCLIService(
        sp.GetRequiredService<ProcessExecutor>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetYamlForge.Services.AI.CliConfig>>(),
        sp.GetRequiredService<SkillLoader>(),
        sp.GetRequiredService<ILogger<OllamaCLIService>>());

    var poolManager = sp.GetRequiredService<NetYamlForge.Services.AI.AIProcessPoolManager>();
    var executor = sp.GetRequiredService<ProcessExecutor>();
    var daemonFactory = sp.GetRequiredService<NetYamlForge.Services.AI.DaemonChatServiceFactory>();
    var logger = sp.GetRequiredService<ILogger<NetYamlForge.Services.AI.PooledCLIService>>();

    return new NetYamlForge.Services.AI.PooledCLIService(
        inner, poolManager, executor, processPoolConfig, daemonFactory, logger);
});

builder.Services.AddSingleton<ICLIService>(sp =>
{
    var inner = new LmStudioCLIService(
        sp.GetRequiredService<ProcessExecutor>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetYamlForge.Services.AI.CliConfig>>(),
        sp.GetRequiredService<SkillLoader>(),
        sp.GetRequiredService<ILogger<LmStudioCLIService>>());

    var poolManager = sp.GetRequiredService<NetYamlForge.Services.AI.AIProcessPoolManager>();
    var executor = sp.GetRequiredService<ProcessExecutor>();
    var daemonFactory = sp.GetRequiredService<NetYamlForge.Services.AI.DaemonChatServiceFactory>();
    var logger = sp.GetRequiredService<ILogger<NetYamlForge.Services.AI.PooledCLIService>>();

    return new NetYamlForge.Services.AI.PooledCLIService(
        inner, poolManager, executor, processPoolConfig, daemonFactory, logger);
});

builder.Services.AddSingleton<ICLIService>(sp =>
{
    var inner = new CopilotCLIService(
        sp.GetRequiredService<ProcessExecutor>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetYamlForge.Services.AI.CliConfig>>(),
        sp.GetRequiredService<SkillLoader>(),
        sp.GetRequiredService<ILogger<CopilotCLIService>>());

    var poolManager = sp.GetRequiredService<NetYamlForge.Services.AI.AIProcessPoolManager>();
    var executor = sp.GetRequiredService<ProcessExecutor>();
    var daemonFactory = sp.GetRequiredService<NetYamlForge.Services.AI.DaemonChatServiceFactory>();
    var logger = sp.GetRequiredService<ILogger<NetYamlForge.Services.AI.PooledCLIService>>();

    return new NetYamlForge.Services.AI.PooledCLIService(
        inner, poolManager, executor, processPoolConfig, daemonFactory, logger);
});

// DashScope 直接 API プロバイダー（高速応答用）
builder.Services.AddSingleton<NetYamlForge.Services.AI.DashScopeApiProvider>();

builder.Services.AddSignalR();

// AI ディベートサービス
builder.Services.AddSingleton<NetYamlForge.Services.AI.AIDebateService>();
builder.Services.AddSingleton<NetYamlForge.Services.AI.AIDebateDbService>();
builder.Services.AddSingleton<NetYamlForge.Services.AI.AIDebateOrchestrator>();

// AI 窓口システムサービス (AIWindowController が依存)
builder.Services.Configure<NetYamlForge.Services.AI.AiWindowConfig>(
    builder.Configuration.GetSection("AiWindow"));
builder.Services.AddScoped<NetYamlForge.Services.AI.IConversationManager,
                           NetYamlForge.Services.AI.ConversationManager>();
builder.Services.AddScoped<NetYamlForge.Services.AI.IDirectAIProcessor,
                           NetYamlForge.Services.AI.DirectAIProcessor>();
builder.Services.AddScoped<NetYamlForge.Services.AI.IHandoverManager,
                           NetYamlForge.Services.AI.HandoverManager>();
builder.Services.AddScoped<NetYamlForge.Services.AI.ICustomerDataService,
                           NetYamlForge.Services.AI.CustomerDataService>();
builder.Services.AddScoped<NetYamlForge.Services.AI.IAppointmentService,
                           NetYamlForge.Services.AI.AppointmentService>();
builder.Services.AddScoped<NetYamlForge.Services.AI.IOperatorChatService,
                           NetYamlForge.Services.AI.OperatorChatService>();

// AI Pipeline Service (Harness 統合)
builder.Services.Configure<NetYamlForge.Services.AI.AiPipelineConfig>(
    builder.Configuration.GetSection("AiPipeline"));
builder.Services.AddHttpClient<NetYamlForge.Services.AI.AiPipelineService>(client =>
{
    client.Timeout = TimeSpan.FromHours(1);
});
builder.Services.AddScoped<NetYamlForge.Services.AI.HarnessContextAdapter>();

// AI 辅助服务（阶段 3）
builder.Services.AddScoped<NetYamlForge.Services.AI.AiRefactoringService>();
builder.Services.AddScoped<NetYamlForge.Services.AI.AiTestGenerationService>();
builder.Services.AddScoped<NetYamlForge.Services.AI.AiDocumentationService>();
} // end else (embedded mode)

var app = builder.Build();

app.UsePathBase("/nyf");

// 处理代理服务器转发的协议头（Caddy 设置的 X-Forwarded-Proto 等）
app.Use(async (context, next) =>
{
    var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
    if (!string.IsNullOrEmpty(forwardedProto))
    {
        context.Request.Scheme = forwardedProto;
    }
    
    // Caddy の handle_path で /nyf が剥がされると UsePathBase だけでは PathBase が空のままになる。
    // その場合でも Razor/Url.Content が正しく /nyf を含む URL を生成できるように補正する。
    if (!context.Request.PathBase.HasValue)
    {
        var forwardedPrefix = context.Request.Headers["X-Forwarded-Prefix"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedPrefix))
        {
            context.Request.PathBase = new PathString(forwardedPrefix);
        }
        else
        {
            context.Request.PathBase = new PathString("/nyf");
        }
    }

    await next();
});

// Start task queue processing
var taskQueue = app.Services.GetRequiredService<TaskQueueService>();
taskQueue.StartProcessing();

// システムデータベース初期化（マルチテナント認証用）
var sysLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SystemDatabase");
await SystemDatabaseInitializer.InitializeAsync(sysLogger);

// プロジェクトデータベース初期化
await DbInitializer.InitializeAsync(app.Services, app.Configuration);

// プロジェクトメタデータをシステムデータベースに同期
var projectManager = app.Services.GetRequiredService<ProjectManager>();
await SystemDatabaseInitializer.SyncProjectsAsync(projectManager.GetAll(), sysLogger);

var supportedCultures = new[] { "en-US", "zh-CN", "ja-JP", "ko-KR" }
    .Select(x => new CultureInfo(x))
    .ToList();

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en-US"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseMiddleware<RequestTraceMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        var project = httpContext.GetRouteValue("project")?.ToString();
        if (!string.IsNullOrWhiteSpace(project))
        {
            diagnosticContext.Set("Project", project);
        }
    };
});
app.UseRequestLocalization(localizationOptions);
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<ProjectMiddleware>(); // UseRouting 後・UseAuthentication 前に配置
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<NetYamlForge.Services.Tenant.ProjectScopeMiddleware>(); // 项目范围验证中间件
app.UseMiddleware<NetYamlForge.Services.Connection.ConnectionPreloadingMiddleware>(); // 连接预加载（Phase 2）- 必须在 ProjectScope 之后

// SignalR Hubs for AI（嵌入式模式下才需要映射）
if (aiMode.Equals("embedded", StringComparison.OrdinalIgnoreCase))
{
    app.MapHub<AIProgressHub>("/aiProgressHub");
    app.MapHub<NaturalLanguageQueryHub>("/nlQueryHub");
    app.MapHub<AIDebateHub>("/aiDebateHub");
}

// [ApiController] 属性ルーティングを明示的に登録（MapControllerRoute だけでは登録されない）
app.MapControllers();

// 用户个人主页（必须在 project-home 之前）
app.MapControllerRoute(
    name: "user-home",
    pattern: "MyHome",
    defaults: new { controller = "UserHome", action = "Index" });

// プロジェクトホーム：/{project}
app.MapControllerRoute(
    name: "project-home",
    pattern: "{project}",
    defaults: new { controller = "Home", action = "Project" });

// プロジェクトルート：/{project}/{controller}/{action}
app.MapControllerRoute(
    name: "project",
    pattern: "{project}/{controller=Dashboard}/{action=Index}/{id?}");

// ルートルート：/{controller}/{action}（デフォルトは Home）
app.MapControllerRoute(
    name: "root",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
