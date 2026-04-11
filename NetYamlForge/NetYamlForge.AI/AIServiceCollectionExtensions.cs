// AI サービスの DI 登録拡張メソッド

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NetYamlForge.AI.Services;
using NetYamlForge.AI.Services.Providers;
using NetYamlForge.AI.Hubs;
using NetYamlForge.AI.Infrastructure;
using NetYamlForge.AI.Client;
using NetYamlForge.AI.Config;
using System.Data;

namespace NetYamlForge.AI;

/// <summary>
/// AI サービス DI 登録拡張
/// </summary>
public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// AI チャットサービスを登録（双モード対応）
    /// 嵌入式模式：直接在主进程内运行所有 AI 服务
    /// 独立进程模式：注册 HTTP 客户端代理，AI 服务在独立进程中运行
    /// </summary>
    public static IServiceCollection AddNetYamlForgeAI(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerControllers = true)
    {
        var modeConfig = configuration.GetSection(AIModeConfig.SectionName).Get<AIModeConfig>() ?? new AIModeConfig();
        var isStandalone = modeConfig.Mode.Equals("standalone", StringComparison.OrdinalIgnoreCase);

        if (isStandalone)
        {
            // 独立进程模式：只注册 HTTP 客户端
            return RegisterStandaloneMode(services, configuration, modeConfig);
        }

        // 嵌入式模式：注册完整 AI 服务
        return RegisterEmbeddedMode(services, configuration, registerControllers);
    }

    /// <summary>
    /// 嵌入式模式注册（AI 服务在主进程内运行）
    /// </summary>
    private static IServiceCollection RegisterEmbeddedMode(
        IServiceCollection services,
        IConfiguration configuration,
        bool registerControllers)
    {
        // ===== 設定登録 =====
        services.Configure<CliConfig>(configuration.GetSection(CliConfig.SectionName));
        services.Configure<AiWindowConfig>(configuration.GetSection("AiWindow"));

        // ===== インフラストラクチャ =====
        services.AddSingleton<ISqlSafetyGuard, DefaultSqlSafetyGuard>();
        services.AddSingleton<IAIDbConnectionFactory, DefaultAIDbConnectionFactory>();
        services.AddSingleton<IAIPathResolver, DefaultAIPathResolver>();
        services.AddSingleton<IAIProjectContext, StandaloneAIProjectContext>();
        
        // ===== AI 查询执行器 =====
        services.AddScoped<IAIQueryExecutor, DefaultAIQueryExecutor>();
        
        // ===== 数据库连接（Scoped，供 ChatService 使用）=====
        services.AddScoped<IDbConnection>(sp =>
        {
            var factory = sp.GetRequiredService<IAIDbConnectionFactory>();
            var projectContext = sp.GetRequiredService<IAIProjectContext>();
            return factory.CreateConnection(projectContext.ProjectName);
        });

        // ===== 基本サービス =====
        services.AddSingleton<ProcessExecutor>();
        services.AddSingleton<SkillLoader>();
        services.AddSingleton<CLIServiceFactory>();
        services.AddSingleton<AISettingsService>();
        services.AddSingleton<ProgressTracker>();
        services.AddSingleton<TaskQueueService>();

        // ===== HTTP クライアント（本地模型用）=====
        services.AddHttpClient("OllamaClient");
        services.AddHttpClient("LmStudioClient");

        // ===== LLM プロバイダー =====
        services.AddScoped<ILlmProvider, HybridLlmProvider>();

        // ===== AI CLI サービス =====
        services.AddSingleton<CliProcessPoolConfig>(sp =>
        {
            var config = new CliProcessPoolConfig();
            configuration.GetSection("AICli:ProcessPool").Bind(config);
            return config;
        });
        services.AddSingleton<AIProcessPoolManager>();

        // 常驻进程聊天服务工厂
        services.AddSingleton<DaemonChatServiceFactory>(sp =>
        {
            return new DaemonChatServiceFactory(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
        });

        // 注册装饰器包装的 CLI 服务
        services.AddSingleton<ICLIService>(sp =>
        {
            var inner = new ClaudeCLIService(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClaudeCLIService>>());

            var poolManager = sp.GetRequiredService<AIProcessPoolManager>();
            var executor = sp.GetRequiredService<ProcessExecutor>();
            var daemonFactory = sp.GetRequiredService<DaemonChatServiceFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledCLIService>>();
            var poolConfig = sp.GetRequiredService<CliProcessPoolConfig>();

            return new PooledCLIService(inner, poolManager, executor, poolConfig, daemonFactory, logger);
        });

        services.AddSingleton<ICLIService>(sp =>
        {
            var inner = new QwenCodeCLIService(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QwenCodeCLIService>>());

            var poolManager = sp.GetRequiredService<AIProcessPoolManager>();
            var executor = sp.GetRequiredService<ProcessExecutor>();
            var daemonFactory = sp.GetRequiredService<DaemonChatServiceFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledCLIService>>();
            var poolConfig = sp.GetRequiredService<CliProcessPoolConfig>();

            return new PooledCLIService(inner, poolManager, executor, poolConfig, daemonFactory, logger);
        });

        services.AddSingleton<ICLIService>(sp =>
        {
            var inner = new MockCLIService(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MockCLIService>>());

            var poolManager = sp.GetRequiredService<AIProcessPoolManager>();
            var executor = sp.GetRequiredService<ProcessExecutor>();
            var daemonFactory = sp.GetRequiredService<DaemonChatServiceFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledCLIService>>();
            var poolConfig = sp.GetRequiredService<CliProcessPoolConfig>();

            return new PooledCLIService(inner, poolManager, executor, poolConfig, daemonFactory, logger);
        });

        services.AddSingleton<ICLIService>(sp =>
        {
            var inner = new CodexCLIService(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CodexCLIService>>());

            var poolManager = sp.GetRequiredService<AIProcessPoolManager>();
            var executor = sp.GetRequiredService<ProcessExecutor>();
            var daemonFactory = sp.GetRequiredService<DaemonChatServiceFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledCLIService>>();
            var poolConfig = sp.GetRequiredService<CliProcessPoolConfig>();

            return new PooledCLIService(inner, poolManager, executor, poolConfig, daemonFactory, logger);
        });

        services.AddSingleton<ICLIService>(sp =>
        {
            var inner = new GeminiCLIService(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GeminiCLIService>>());

            var poolManager = sp.GetRequiredService<AIProcessPoolManager>();
            var executor = sp.GetRequiredService<ProcessExecutor>();
            var daemonFactory = sp.GetRequiredService<DaemonChatServiceFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledCLIService>>();
            var poolConfig = sp.GetRequiredService<CliProcessPoolConfig>();

            return new PooledCLIService(inner, poolManager, executor, poolConfig, daemonFactory, logger);
        });

        services.AddSingleton<ICLIService>(sp =>
        {
            var inner = new OllamaCLIService(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OllamaCLIService>>());

            var poolManager = sp.GetRequiredService<AIProcessPoolManager>();
            var executor = sp.GetRequiredService<ProcessExecutor>();
            var daemonFactory = sp.GetRequiredService<DaemonChatServiceFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledCLIService>>();
            var poolConfig = sp.GetRequiredService<CliProcessPoolConfig>();

            return new PooledCLIService(inner, poolManager, executor, poolConfig, daemonFactory, logger);
        });

        services.AddSingleton<ICLIService>(sp =>
        {
            var inner = new LmStudioCLIService(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LmStudioCLIService>>());

            var poolManager = sp.GetRequiredService<AIProcessPoolManager>();
            var executor = sp.GetRequiredService<ProcessExecutor>();
            var daemonFactory = sp.GetRequiredService<DaemonChatServiceFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledCLIService>>();
            var poolConfig = sp.GetRequiredService<CliProcessPoolConfig>();

            return new PooledCLIService(inner, poolManager, executor, poolConfig, daemonFactory, logger);
        });

        services.AddSingleton<ICLIService>(sp =>
        {
            var inner = new CopilotCLIService(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CopilotCLIService>>());

            var poolManager = sp.GetRequiredService<AIProcessPoolManager>();
            var executor = sp.GetRequiredService<ProcessExecutor>();
            var daemonFactory = sp.GetRequiredService<DaemonChatServiceFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledCLIService>>();
            var poolConfig = sp.GetRequiredService<CliProcessPoolConfig>();

            return new PooledCLIService(inner, poolManager, executor, poolConfig, daemonFactory, logger);
        });

        // DashScope 直接 API プロバイダー
        services.AddSingleton<DashScopeApiProvider>();

        // ===== SignalR =====
        services.AddSignalR();

        // ===== AI ディベート =====
        services.AddSingleton<AIDebateService>();
        services.AddSingleton<AIDebateDbService>();
        services.AddSingleton<AIDebateOrchestrator>();

        // ===== AI 窓口システム =====
        services.AddScoped<IConversationManager, ConversationManager>();
        services.AddScoped<IDirectAIProcessor, DirectAIProcessor>();
        services.AddScoped<IHandoverManager, HandoverManager>();
        services.AddScoped<ICustomerDataService, CustomerDataService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IOperatorChatService, OperatorChatService>();
        
        // ===== AI レポート =====
        services.AddSingleton<IAIReportPdfService, AIReportPdfService>();

        // ===== 自然言語クエリ =====
        services.AddScoped<QueryParserService>();
        services.AddScoped<QueryExecutionService>();
        services.AddScoped<QueryResultFormatter>();

        // ===== 意図分類 & スロットフィリング =====
        services.AddScoped<IIntentClassifier, HybridIntentClassifier>();
        services.AddSingleton<ISlotFillingManager, SlotFillingManager>();

        // ===== プロジェクト固有チャットサービス =====
        services.AddScoped<AutoDealerChatService>();
        services.AddScoped<JpiereChatService>();

        // ===== 会話履歴 =====
        services.AddSingleton<ChatHistoryService>();

        // ===== コントローラー登録 =====
        if (registerControllers)
        {
            services.AddControllers()
                .AddApplicationPart(typeof(AI.Controllers.AIController).Assembly);
        }

        return services;
    }

    /// <summary>
    /// 独立进程模式注册（通过 HTTP API 调用 AI 服务）
    /// </summary>
    private static IServiceCollection RegisterStandaloneMode(
        IServiceCollection services,
        IConfiguration configuration,
        AIModeConfig modeConfig)
    {
        // 注册 HTTP 客户端
        services.AddAIServiceClient(configuration);

        // 不注册嵌入式服务、控制器、Hubs
        // AI 服务在独立进程中运行，通过 AIServiceClient 代理调用

        return services;
    }

    /// <summary>
    /// AI チャットコントローラーを登録（MVC アプリケーションパートとして追加）
    /// </summary>
    public static IMvcBuilder AddAIControllers(this IMvcBuilder builder)
    {
        return builder.AddApplicationPart(typeof(AI.Controllers.AIController).Assembly);
    }
}
