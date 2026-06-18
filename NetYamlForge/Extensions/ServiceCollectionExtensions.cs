// ファイル概要: DI登録をグループ別メソッドに分割した拡張クラス。
// Program.cs の肥大化を防ぎ、責務ごとに登録内容を把握しやすくします。
// 新しいサービスを追加する際は、対応するグループメソッドに追記してください。

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
using NetYamlForge.Services.Cli;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.AI.ToolValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;

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
                return connectionManager.GetConnectionAsync(scope.Current.Name).GetAwaiter().GetResult();
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
    /// コア CRUD / 認証 / 監査サービスを登録します。
    /// </summary>
    public static IServiceCollection AddDynamicCrudCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValueConverter, ValueConverter>();
        services.AddScoped<FormValueValidationService>();
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
        services.AddScoped<DynamicEntitySchemaMigrationService>();
        services.AddScoped<DynamicEntityFormValidationService>();
        services.AddScoped<CommandErrorHttpMapper>();
        services.AddScoped<IUserAuthService, UserAuthService>();
        services.AddScoped<ITenantUserService, TenantUserService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IHookExecutionTelemetry, HookExecutionTelemetryLogger>();
        services.AddScoped<IPagePermissionService, PagePermissionService>();
        
        // AI サービス
        services.AddScoped<NetYamlForge.Services.AI.IAntigravityCliService, NetYamlForge.Services.AI.AntigravityCliService>();
        // Embedding サービス（EmbeddingProvider で切替: local / lmstudio / gemini）
        var embedProvider = configuration.GetValue<string>("EmbeddingProvider")?.ToLowerInvariant()
            ?? Environment.GetEnvironmentVariable("EMBEDDING_PROVIDER")?.ToLowerInvariant()
            ?? "local";
        switch (embedProvider)
        {
            case "lmstudio":
                services.AddSingleton<NetYamlForge.Services.AI.IGeminiEmbeddingService, NetYamlForge.Services.AI.LmStudioEmbeddingService>();
                break;
            case "local":
                services.AddSingleton<NetYamlForge.Services.AI.IGeminiEmbeddingService, NetYamlForge.Services.AI.LocalEmbeddingService>();
                break;
            case "gemini":
                services.AddSingleton<NetYamlForge.Services.AI.IGeminiEmbeddingService, NetYamlForge.Services.AI.GeminiEmbeddingService>();
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

        // ファイルアップロードサービス
        services.AddScoped<IFileUploadService, FileUploadService>();
        // PDF エクスポートサービス (PDFsharp - MIT ライセンス)
        services.AddSingleton<IPdfExportService, PdfExportService>();
        // IDocumentPdfService の既定実装は PDFsharp (MIT ライセンス)
        services.AddSingleton<IDocumentPdfService, DocumentPdfService>();
        services.AddHostedService<NetYamlForge.Services.Validation.YamlConfigStartupValidator>();

        // バッチジョブサービス
        services.AddSingleton<IBatchJobLoader, BatchJobLoader>();
        services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<IOutboxJobService, OutboxJobService>();
        services.AddScoped<IRealBatchJobExecutor, RealBatchJobExecutor>();
        services.AddHostedService<OutboxJobBackgroundService>();

        // IBatchStepHandler 実装を登録 (具体型とインターフェースの両方を登録して遅延解決に対応)
        services.AddScoped<SqlToCsvHandler>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<SqlToCsvHandler>());
        services.AddScoped<SqlCommandHandler>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<SqlCommandHandler>());
        services.AddScoped<StoredProcedureHandler>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<StoredProcedureHandler>());
        services.AddScoped<ChinaStockBriefingExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<ChinaStockBriefingExecutor>());
        services.AddScoped<EmailFetchExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<EmailFetchExecutor>());
        services.AddScoped<InvoiceEmailProcessorExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<InvoiceEmailProcessorExecutor>());
        services.AddScoped<AutomatedBlogGeneratorExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<AutomatedBlogGeneratorExecutor>());
        services.AddScoped<AiDealerEngineExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<AiDealerEngineExecutor>());
        services.AddScoped<AiCommunicationExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<AiCommunicationExecutor>());
        services.AddScoped<AiFolderProcessorExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<AiFolderProcessorExecutor>());
        services.AddScoped<DirectoryImportExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<DirectoryImportExecutor>());
        services.AddScoped<PhotoAnnotatorExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<PhotoAnnotatorExecutor>());
        services.AddScoped<EmbeddingGeneratorExecutor>();
        services.AddScoped<IBatchStepHandler>(sp => sp.GetRequiredService<EmbeddingGeneratorExecutor>());

        services.AddScoped<IBatchJobExecutor, BatchJobExecutor>();
        services.AddSingleton<IBatchJobHistoryStore, InMemoryBatchJobHistoryStore>();
        // BatchJobHostedService を Singleton として登録し、IBatchJobScheduler でも解決できるようにする
        services.AddSingleton<BatchJobHostedService>();
        services.AddSingleton<IBatchJobScheduler>(sp => sp.GetRequiredService<BatchJobHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<BatchJobHostedService>());

        // 中国股市行情数据服务
        services.AddHttpClient<IChinaStockService, ChinaStockService>();

        // ワークフローガイドサービス
        services.AddScoped<WorkflowGuideService>();

        return services;
    }

    /// <summary>
    /// プロジェクト固有フック・ビジネスロジックのレジストリを登録します。
    /// 新しいプロジェクトのフックは projects/&lt;name&gt;/Hooks/ に配置すると自動検出されます。
    /// </summary>
    public static IServiceCollection AddProjectHooks(this IServiceCollection services)
    {
        // ===== プロジェクト固有フック =====
        // 各プロジェクトの Hooks/ ディレクトリから動的にフックを読み込みます。
        services.AddSingleton<IProjectHookRegistry, ProjectHookRegistry>();
        services.AddSingleton<IProjectHookLoader, ProjectHookLoader>();

        // ===== プロジェクト固有ビジネスロジック =====
        // 各プロジェクトの Hooks/ ディレクトリからビジネスロジック・バリデーション・データ変換を読み込みます。
        services.AddSingleton<IProjectBusinessLogicRegistry, ProjectBusinessLogicRegistry>();

        // ===== プロジェクト固有カスタムアクション =====
        services.AddSingleton<IProjectActionRegistry, ProjectActionRegistry>();

        return services;
    }

    /// <summary>
    /// エンティティフック実装を全て登録します。
    /// 新しいフックを追加する場合はここに IEntityHook の実装を追記してください。
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
}
