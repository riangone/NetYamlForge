// ファイル概要: DI登録をグループ別メソッドに分割した拡張クラス。
// Program.cs の肥大化を防ぎ、責務ごとに登録内容を把握しやすくします。
// 新しいサービスを追加する際は、対応するグループメソッドに追記してください。

using System;
using System.Data;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.Connection;
using NetYamlForge.Services.Dialect;
using NetYamlForge.Services.DynamicEntity;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.Page;
using NetYamlForge.Services.HotReload;
using NetYamlForge.Services.Tenant;
using NetYamlForge.Services.Workflow;
using NetYamlForge.Services.Webhook;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.AI.ToolValidation;
using NetYamlForge.Services.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;
using NetYamlForge.Projects.FormForge;
using NetYamlForge.Projects.KbForge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NetYamlForge.Extensions;

/// <summary>
/// アプリケーション DI 登録をグループ別に分割する拡張メソッド集。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 全サービスを一括登録するファサードメソッド。Program.cs から呼び出してください。
    /// </summary>
    public static IServiceCollection AddNetYamlForge(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMultiProjectInfrastructure();
        services.AddDatabaseServices();
        services.AddDynamicCrudCore(configuration);
        services.AddAuthServices();
        services.AddPdfServices();
        services.AddAiServices(configuration);
        services.AddBatchJobServices();
        services.AddExternalFeatureServices();
        services.AddProjectHooks();
        services.AddEntityHooks();
        services.AddYamlHotReload();
        services.AddEmailServices(configuration);
        
        // AI Prompt 配置配置化
        services.Configure<PromptHotReloadOptions>(configuration.GetSection(PromptHotReloadOptions.SectionName));
        return services;
    }

    /// <summary>
    /// メールサービスを登録します。
    /// </summary>
    public static IServiceCollection AddEmailServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NetYamlForge.Models.Config.EmailSettings>(configuration.GetSection("Email"));
        // グローバルデフォルト（プロジェクト設定なし時のフォールバック）
        services.AddSingleton<NetYamlForge.Services.Email.IEmailService, NetYamlForge.Services.Email.MailKitEmailService>();
        // プロジェクト別メールアカウントを解決するファクトリ
        services.AddSingleton<NetYamlForge.Services.Email.IEmailServiceFactory, NetYamlForge.Services.Email.MailKitEmailServiceFactory>();
        return services;
    }

    /// <summary>
    /// YAML ホットリロードサービスを登録します。
    /// </summary>
    public static IServiceCollection AddYamlHotReload(this IServiceCollection services)
    {
        services.Configure<HotReloadOptions>(options =>
        {
            options.Enabled = true;
            options.OnlyInDevelopment = true;
            options.DebounceMs = 500;
        });
        services.AddSingleton<IYamlFileWatcher, YamlFileWatcher>();
        services.AddSingleton<ProjectYamlCacheManager>();
        services.AddHostedService<YamlHotReloadService>();
        return services;
    }

    /// <summary>
    /// マルチプロジェクト基盤サービスを登録します。
    /// ProjectManager / ProjectScope / EntityMetadataProvider / DashboardConfigProvider
    /// </summary>
    public static IServiceCollection AddMultiProjectInfrastructure(this IServiceCollection services)
    {
        // ProjectManager は Singleton: 起動時に projects/ 配下を全スキャンします。
        services.AddSingleton<ProjectManager>();
        services.AddSingleton<IHomePageConfigProvider, HomePageConfigProvider>();

        // ProjectScope は Scoped: リクエストごとに ProjectMiddleware が初期化します。
        services.AddScoped<ProjectScope>();

        // IEntityMetadataProvider / IDashboardConfigProvider は Scoped プロキシ経由でプロジェクト別に切り替わります。
        services.AddScoped<IEntityMetadataProvider, ProjectAwareEntityMetadataProvider>();
        services.AddScoped<IDashboardConfigProvider, ProjectAwareDashboardConfigProvider>();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantQuotaValidator, TenantQuotaValidator>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddHostedService<WebhookOutboxPoller>();

        return services;
    }

    /// <summary>
    /// データベース接続・SQL方言サービスを登録します。
    /// IDbConnection (プロジェクト DatabaseType に応じたファクトリ) / ISqlDialect
    /// </summary>
    public static IServiceCollection AddDatabaseServices(this IServiceCollection services)
    {
        // 连接池配置（Phase 2 优化：已改为信赖底层原生驱动物理池，关闭自定义应用层连接池）
        services.Configure<ConnectionPoolOptions>(options =>
        {
            options.MaxPoolSize = 64;             // 从 32 提升到 64，支持更高并发
            options.IdleTimeoutMs = 120000;       // 从 1 分钟提升到 2 分钟，减少频繁创建
            options.MaxLifetimeMs = 600000;       // 从 5 分钟提升到 10 分钟，延长连接复用窗口
            options.Enabled = false;
        });

        // 注册连接管理器（Singleton，管理所有项目的连接池）
        services.AddSingleton<IConnectionManager, ConnectionManager>();
        
        // 注册 HttpContextAccessor（用于访问预加载的连接）
        services.AddHttpContextAccessor();

        // IDbConnection: プロジェクトの DatabaseType に応じた接続を生成します。
        // 优化：优先使用预加载的连接（避免同步阻塞异步）
        services.AddScoped<IDbConnection>(sp =>
        {
            var httpContext = sp.GetService<IHttpContextAccessor>()?.HttpContext;

            // 1. 尝试使用预加载的连接
            if (httpContext?.Items["PreloadedConnection"] is IDbConnection preloadedConn)
            {
                httpContext.Items["PreloadedConnectionUsed"] = true;
                return preloadedConn;
            }

            // 2. 项目未设置时，尝试从请求作用域获取 ProjectScope
            var scope = sp.GetService<ProjectScope>();
            if (scope == null || !scope.IsSet)
            {
                // プロジェクトスコープ外（ルートページ等）はフォールバック
#pragma warning disable DCS003
                return new SqliteConnection("Data Source=chinook.db");
#pragma warning restore DCS003
            }

            // 3. 惰性获取连接：返回 LazyDbConnection，避免同步阻塞并实现延迟打开
            var connectionManager = sp.GetRequiredService<IConnectionManager>();
            return new NetYamlForge.Services.Connection.LazyDbConnection(() =>
            {
                return connectionManager.GetConnection(scope.Current.Name);
            });
        });

        // ISqlDialect: プロジェクトの DatabaseType に応じた方言を提供します。
        services.AddScoped<ISqlDialect>(sp =>
        {
            var scope = sp.GetService<ProjectScope>();
            if (scope == null || !scope.IsSet) return new SqliteDialect();
            var dbType = scope.Current.DatabaseType.ToLowerInvariant();
            return dbType switch
            {
                "sqlserver" => new SqlServerDialect(),
                "postgresql" or "postgres" => new PostgreSqlDialect(),
                "mysql" or "mariadb" => new MySqlDialect(),
                _ => new SqliteDialect()
            };
        });

        return services;
    }

    /// <summary>
    /// コア CRUD / 監査サービスを登録します。
    /// </summary>
    public static IServiceCollection AddDynamicCrudCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValueConverter, ValueConverter>();
        services.AddSingleton<IProjectValidatorHookRegistry, ProjectValidatorHookRegistry>();
        services.AddSingleton<IFieldValidator, RegexFieldValidator>();
        services.AddSingleton<IFieldValidator, RangeFieldValidator>();
        services.AddSingleton<IFieldValidator, ConditionalFieldValidator>();
        services.AddScoped<FormValueValidationService>();
        services.AddScoped<DynamicCrudRowLevelSecurity>();
        services.AddScoped<IDynamicCrudRepository, DynamicCrudRepository>();
        services.AddScoped<IRowMutationRepository, RowMutationRepository>();
        services.AddScoped<HookExecutionService>();
        services.AddScoped<SectionRowValidationService>();
        services.AddScoped<SectionRowFormViewModelFactory>();
        services.AddScoped<PageRowMutationService>();
        services.AddSingleton<IProjectPageMutationValidatorRegistry, ProjectPageMutationValidatorRegistry>();
        services.AddScoped<PageDataQueryService>();
        services.AddScoped<PageViewPreferenceService>();
        services.AddScoped<EntityCrudExecutionService>();
        services.AddScoped<DynamicEntityCommandService>();
        services.AddScoped<DynamicEntityKeyResolverService>();
        services.AddScoped<DynamicEntityListResponseService>();
        services.AddScoped<DynamicEntityListQueryService>();
        services.AddScoped<DynamicEntityForeignKeyDataService>();
        services.AddScoped<DynamicEntityFormViewModelFactory>();
        services.AddScoped<DynamicEntityListHttpResponseService>();
        services.AddScoped<DynamicEntityNavigationService>();
        services.AddScoped<DynamicEntityConfigDiffService>();
        services.AddScoped<IBaseEntityMetadataProvider, BaseEntityMetadataProvider>();
        services.AddScoped<DynamicEntityConfigDiagnosticsService>();
        services.AddScoped<ISchemaDdlBuilder, SqliteSchemaDdlBuilder>();
        services.AddScoped<ISchemaDdlBuilder, PostgresSchemaDdlBuilder>();
        services.AddScoped<ISchemaDdlBuilder, MySqlSchemaDdlBuilder>();
        services.AddScoped<ISchemaDdlBuilder, SqlServerSchemaDdlBuilder>();
        services.AddScoped<DynamicEntitySchemaMigrationService>();
        services.AddScoped<DynamicEntityFormValidationService>();
        services.AddScoped<CommandErrorHttpMapper>();
        services.AddScoped<IHookExecutionTelemetry, HookExecutionTelemetryLogger>();
        services.AddSingleton<IEntityHooksService, EntityHooksService>();
        services.AddSingleton<IEntityMetadataParser, EntityMetadataParser>();
        services.AddSingleton<IEntityMetadataValidator, EntityMetadataValidator>();
        services.AddHostedService<NetYamlForge.Services.Validation.YamlConfigStartupValidator>();

        // 页面自定义动作分发器与处理器注册
        services.AddSingleton<IPageActionDispatcher, PageActionDispatcher>();
        services.AddSingleton<IPageActionHandler, NetYamlForge.Services.AI.PageActions.SwitchProviderAction>();
        services.AddSingleton<IPageActionHandler, NetYamlForge.Projects.PhotoVocab.Hooks.PageActions.AnnotatePhotoAction>();
        services.AddSingleton<IPageActionHandler, NetYamlForge.Projects.PhotoVocab.Hooks.PageActions.EmbedPhotoAction>();

        return services;
    }

    /// <summary>
    /// 認証・セキュリティ関連サービスを登録します。
    /// </summary>
    public static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        services.AddScoped<IUserAuthService, UserAuthService>();
        services.AddScoped<ITenantUserService, TenantUserService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IPagePermissionService, PagePermissionService>();
        return services;
    }

    /// <summary>
    /// PDF生成およびエクスポート関連サービスを登録します。
    /// </summary>
    public static IServiceCollection AddPdfServices(this IServiceCollection services)
    {
        services.AddScoped<IFileUploadService, FileUploadService>();
        services.AddSingleton<IPdfExportService, PdfExportService>();
        services.AddSingleton<IDocumentPdfService, DocumentPdfService>();
        return services;
    }

    /// <summary>
    /// AI 関連サービスを登録します。
    /// </summary>
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<NetYamlForge.Services.AI.IAntigravityCliService, NetYamlForge.Services.AI.AntigravityCliService>();
        services.AddScoped<NetYamlForge.Services.AI.ICliChainService, NetYamlForge.Services.AI.CliChainService>();
        
        var embedProvider = configuration.GetValue<string>("EmbeddingProvider")?.ToLowerInvariant()
            ?? Environment.GetEnvironmentVariable("EMBEDDING_PROVIDER")?.ToLowerInvariant()
            ?? "local";
        switch (embedProvider)
        {
            case "lmstudio":
                services.AddSingleton<NetYamlForge.Services.AI.IEmbeddingService, NetYamlForge.Services.AI.LmStudioEmbeddingService>();
                services.AddSingleton<NetYamlForge.Services.AI.IGeminiEmbeddingService>(
                    sp => (NetYamlForge.Services.AI.IGeminiEmbeddingService)sp.GetRequiredService<NetYamlForge.Services.AI.IEmbeddingService>());
                break;
            case "local":
                services.AddSingleton<NetYamlForge.Services.AI.IEmbeddingService, NetYamlForge.Services.AI.LocalEmbeddingService>();
                services.AddSingleton<NetYamlForge.Services.AI.IGeminiEmbeddingService>(
                    sp => (NetYamlForge.Services.AI.IGeminiEmbeddingService)sp.GetRequiredService<NetYamlForge.Services.AI.IEmbeddingService>());
                break;
            case "gemini":
                services.AddSingleton<NetYamlForge.Services.AI.IEmbeddingService, NetYamlForge.Services.AI.GeminiEmbeddingService>();
                services.AddSingleton<NetYamlForge.Services.AI.IGeminiEmbeddingService>(
                    sp => (NetYamlForge.Services.AI.IGeminiEmbeddingService)sp.GetRequiredService<NetYamlForge.Services.AI.IEmbeddingService>());
                break;
            default:
                services.AddSingleton<NetYamlForge.Services.AI.IEmbeddingService, NetYamlForge.Services.AI.LocalEmbeddingService>();
                services.AddSingleton<NetYamlForge.Services.AI.IGeminiEmbeddingService>(
                    sp => (NetYamlForge.Services.AI.IGeminiEmbeddingService)sp.GetRequiredService<NetYamlForge.Services.AI.IEmbeddingService>());
                break;
        }
        services.AddSingleton<ToolCallValidator>();
        services.AddSingleton<IToolRegistry, InMemoryToolRegistry>();
        services.AddScoped<IAiToolOrchestrator, AiToolOrchestrator>();
        services.AddSingleton<PromptVersionResolver>();
        services.AddSingleton<IAiScenarioYamlLoader, AiScenarioYamlLoader>();
        services.AddScoped<ISlotFillingManager, SlotFillingManager>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddHostedService<AiToolRegistryInitializer>();
        return services;
    }

    /// <summary>
    /// バッチジョブおよび関連バックグラウンドサービスを登録します。
    /// </summary>
    public static IServiceCollection AddBatchJobServices(this IServiceCollection services)
    {
        services.AddSingleton<IBatchJobLoader, BatchJobLoader>();
        services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<IOutboxJobService, OutboxJobService>();
        services.AddSingleton<BatchStepHandlerRegistry>();
        services.AddScoped<IRealBatchJobExecutor, RealBatchJobExecutor>();
        services.AddHostedService<OutboxJobBackgroundService>();

        // IBatchStepHandler 実装を登録
        services.AddBatchStepHandler<SqlToCsvHandler>("sql_to_csv");
        services.AddBatchStepHandler<SqlCommandHandler>("sql_command");
        services.AddBatchStepHandler<StoredProcedureHandler>("stored_procedure");
        services.AddBatchStepHandler<EmailFetchExecutor>("email_fetch");
        services.AddBatchStepHandler<AiCommunicationExecutor>("ai_communication_sender");
        services.AddBatchStepHandler<AiFolderProcessorExecutor>("ai_folder_processor");
        services.AddBatchStepHandler<DirectoryImportExecutor>("directory_import");
        services.AddBatchStepHandler<EmbeddingGeneratorExecutor>("photo_embedding_generator");
        services.AddBatchStepHandler<EmbeddingGeneratorExecutor>("embedding_generator");
        services.AddBatchStepHandler<AiEmbeddingGeneratorExecutor>("ai_embedding_generator");
        services.AddBatchStepHandler<AiAnnotatorExecutor>("ai_annotator");

        services.AddScoped<IBatchJobExecutor, BatchJobExecutor>();
        services.AddSingleton<IBatchJobHistoryStore, InMemoryBatchJobHistoryStore>();
        // BatchJobHostedService を Singleton として登録し、IBatchJobScheduler でも解決できるようにする
        services.AddSingleton<BatchJobHostedService>();
        services.AddSingleton<IBatchJobScheduler>(sp => sp.GetRequiredService<BatchJobHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<BatchJobHostedService>());
        return services;
    }

    /// <summary>
    /// 外部機能連携およびその他個別機能サービスを登録します。
    /// </summary>
    public static IServiceCollection AddExternalFeatureServices(this IServiceCollection services)
    {
        services.AddHttpClient<IChinaStockService, ChinaStockService>();
        services.AddScoped<WorkflowGuideService>();
        services.AddScoped<FormForgeRepository>();
        services.AddScoped<FormForgeResponseRepository>();
        services.AddScoped<KbForgeRepository>();
        return services;
    }

    /// <summary>
    /// プロジェクト固有フック・ビジネスロジックのレジストリを登録します。
    /// </summary>
    public static IServiceCollection AddProjectHooks(this IServiceCollection services)
    {
        // ===== プロジェクト固有フック =====
        services.AddSingleton<IProjectHookRegistry, ProjectHookRegistry>();
        services.AddSingleton<IProjectHookLoader, ProjectHookLoader>();

        // ===== プロジェクト固有ビジネスロジック =====
        services.AddSingleton<IProjectBusinessLogicRegistry, ProjectBusinessLogicRegistry>();

        // ===== プロジェクト固有カスタムアクション =====
        services.AddSingleton<IProjectActionRegistry, ProjectActionRegistry>();

        return services;
    }

    /// <summary>
    /// エンティティフック実装を全て登録します。
    /// </summary>
    public static IServiceCollection AddEntityHooks(this IServiceCollection services)
    {
        // サンプルフック（既存）
        services.AddSingleton<IEntityHook, CustomerEmailDomainHook>();
        services.AddSingleton<IEntityHook, CustomerNameNormalizeHook>();
        services.AddSingleton<IEntityHook, InvoiceMinimumTotalHook>();
        services.AddSingleton<IEntityHook, ConsoleLogAfterHook>();

        // 汎用フック（検証）
        services.AddSingleton<IEntityHook, ValidateEmailHook>();
        services.AddSingleton<IEntityHook, ValidatePhoneHook>();
        services.AddSingleton<IEntityHook, ValidateUrlHook>();
        services.AddSingleton<IEntityHook, ValidateRegexHook>();
        services.AddSingleton<IEntityHook, ValidateRangeHook>();
        services.AddSingleton<IEntityHook, ValidateUniqueHook>();
        services.AddSingleton<IEntityHook, ValidateRequiredHook>();

        // 汎用フック（データ変換）
        services.AddSingleton<IEntityHook, TrimHook>();
        services.AddSingleton<IEntityHook, UppercaseHook>();
        services.AddSingleton<IEntityHook, LowercaseHook>();
        services.AddSingleton<IEntityHook, TitleCaseHook>();
        services.AddSingleton<IEntityHook, DefaultHook>();
        services.AddSingleton<IEntityHook, NowHook>();
        services.AddSingleton<IEntityHook, CurrentUserHook>();

        // 汎用フック（監査・通知）
        services.AddSingleton<IEntityHook, AuditLogHook>();
        services.AddSingleton<IEntityHook, WebhookHook>();
        services.AddSingleton<IEntityHook, SendEmailHook>();

        // 汎用フック（関連データ操作）
        services.AddSingleton<IEntityHook, UpdateCountHook>();
        services.AddSingleton<IEntityHook, UpdateRelatedHook>();

        // 汎用フック（ソフト削除）
        services.AddSingleton<IEntityHook, SoftDeleteHook>();

        services.AddSingleton<IEntityHookRegistry, EntityHookRegistry>();

        return services;
    }

    public static IServiceCollection AddBatchStepHandler<THandler>(this IServiceCollection services, string stepType)
        where THandler : class, IBatchStepHandler
    {
        services.AddScoped<THandler>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<THandler>());
        services.AddSingleton(new BatchStepHandlerRegistration(stepType, typeof(THandler)));
        return services;
    }
}
