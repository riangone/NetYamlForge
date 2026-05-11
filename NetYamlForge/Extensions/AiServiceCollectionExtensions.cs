// ファイル概要：AI サービス登録の拡張メソッド。Program.cs の重複コードを解消します。

using Microsoft.Extensions.Options;
using NetYamlForge.AI.Client;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.AI.Providers;

namespace NetYamlForge.Extensions;

/// <summary>
/// AI サービス登録用拡張メソッド。Program.cs の責務を分割します。
/// </summary>
public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// 組み込み AI サービスを一括登録します（embedded モード用）。
    /// </summary>
    public static IServiceCollection AddEmbeddedAiServices(this IServiceCollection services, CliProcessPoolConfig processPoolConfig)
    {
        // 基础服务
        services.AddSingleton<ProcessExecutor>();
        services.AddSingleton<SkillLoader>();
        services.AddSingleton<CLIServiceFactory>();
        services.AddSingleton<AISettingsService>();
        services.AddSingleton<ProgressTracker>();
        services.AddSingleton<TaskQueueService>();

        // HTTP Client for Ollama and LM Studio
        services.AddHttpClient("OllamaClient");
        services.AddHttpClient("LmStudioClient");

        // AI 聊天服务
        services.AddScoped<AutoDealerChatService>();
        services.AddScoped<JpiereChatService>();

        // 自然语言查询服务
        services.AddScoped<ILlmProvider, HybridLlmProvider>();
        services.AddScoped<QueryParserService>();
        services.AddScoped<QueryExecutionService>();
        services.AddScoped<QueryResultFormatter>();

        // 意图分类器和槽位填充
        services.AddScoped<IIntentClassifier, HybridIntentClassifier>();
        services.AddSingleton<ISlotFillingManager, SlotFillingManager>();

        // 进程池管理器
        services.AddSingleton<AIProcessPoolManager>();
        services.AddSingleton<DaemonChatServiceFactory>(sp =>
        {
            return new DaemonChatServiceFactory(
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<ILoggerFactory>());
        });

        // CLI 服务（使用进程池包装）
        RegisterPooledCliService<ClaudeCLIService>(services, processPoolConfig);
        RegisterPooledCliService<QwenCodeCLIService>(services, processPoolConfig);
        RegisterPooledCliService<MockCLIService>(services, processPoolConfig);
        RegisterPooledCliService<CodexCLIService>(services, processPoolConfig);
        RegisterPooledCliService<GeminiCLIService>(services, processPoolConfig);
        RegisterPooledCliService<OllamaCLIService>(services, processPoolConfig);
        RegisterPooledCliService<LmStudioCLIService>(services, processPoolConfig);
        RegisterPooledCliService<CopilotCLIService>(services, processPoolConfig);

        // DashScope 直接 API 提供商
        services.AddSingleton<DashScopeApiProvider>();

        // SignalR
        services.AddSignalR();

        // AI 辩论服务
        services.AddSingleton<AIDebateService>();
        services.AddSingleton<AIDebateDbService>();
        services.AddSingleton<AIDebateOrchestrator>();

        // AI 窗口系统服务
        services.Configure<AiWindowConfig>(options => { });
        services.AddScoped<IConversationManager, ConversationManager>();
        services.AddScoped<IDirectAIProcessor, DirectAIProcessor>();
        services.AddScoped<IHandoverManager, HandoverManager>();
        services.AddScoped<ICustomerDataService, CustomerDataService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IOperatorChatService, OperatorChatService>();

        // AI Pipeline Service
        services.Configure<AiPipelineConfig>(options => { });
        services.AddHttpClient<AiPipelineService>(client =>
        {
            client.Timeout = TimeSpan.FromHours(1);
        });
        services.AddScoped<HarnessContextAdapter>();

        // AI 辅助服务
        services.AddScoped<AiRefactoringService>();
        services.AddScoped<AiTestGenerationService>();
        services.AddScoped<AiDocumentationService>();

        return services;
    }

    /// <summary>
    /// 独立进程モード用の AI サービスを登録します。
    /// </summary>
    public static IServiceCollection AddStandaloneAiServices(this IServiceCollection services)
    {
        services.AddHttpClient<AIServiceClient>(client =>
        {
            var baseUrl = "http://localhost:5200";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(30);
        });
        services.AddSingleton(sp => sp.GetRequiredService<AIServiceClient>());

        return services;
    }

    private static void RegisterPooledCliService<TService>(IServiceCollection services, CliProcessPoolConfig config)
        where TService : class, ICLIService
    {
        services.AddSingleton<ICLIService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PooledCLIService>>();
            var poolManager = sp.GetRequiredService<AIProcessPoolManager>();
            var executor = sp.GetRequiredService<ProcessExecutor>();
            var daemonFactory = sp.GetRequiredService<DaemonChatServiceFactory>();

            var inner = (TService)ActivatorUtilities.CreateInstance<TService>(sp,
                sp.GetRequiredService<ProcessExecutor>(),
                sp.GetRequiredService<IOptions<CliConfig>>(),
                sp.GetRequiredService<SkillLoader>(),
                sp.GetRequiredService<ILogger<TService>>());

            return new PooledCLIService(
                inner, poolManager, executor, config, daemonFactory, logger);
        });
    }
}
