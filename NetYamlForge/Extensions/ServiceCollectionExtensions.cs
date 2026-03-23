// ファイル概要: DI登録をグループ別メソッドに分割した拡張クラス。
// Program.cs の肥大化を防ぎ、責務ごとに登録内容を把握しやすくします。
// 新しいサービスを追加する際は、対応するグループメソッドに追記してください。

using System.Data;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.Dialect;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.Page;
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
    public static IServiceCollection AddNetYamlForge(this IServiceCollection services)
    {
        services.AddMultiProjectInfrastructure();
        services.AddDatabaseServices();
        services.AddDynamicCrudCore();
        services.AddProjectHooks();
        services.AddEntityHooks();
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
        // IDbConnection: プロジェクトの DatabaseType に応じた接続を生成します。
        // DCS003 抑制理由: ここはDIファクトリ登録であり、接続を直接生成する唯一の正当な場所です。
        // 他の全サービスはDIから IDbConnection を受け取るべきです。
#pragma warning disable DCS003
        services.AddScoped<IDbConnection>(sp =>
        {
            var scope = sp.GetRequiredService<ProjectScope>();
            if (!scope.IsSet)
            {
                // プロジェクトスコープ外（ルートページ等）はフォールバック
                return new SqliteConnection("Data Source=chinook.db");
            }
            var dbType = scope.Current.DatabaseType.ToLowerInvariant();
            return dbType switch
            {
                "sqlserver" => new SqlConnection(scope.Current.ConnectionString),
                "postgresql" or "postgres" => new NpgsqlConnection(scope.Current.ConnectionString),
                "mysql" or "mariadb" => new MySqlConnection(scope.Current.ConnectionString),
                _ => new SqliteConnection(scope.Current.ConnectionString)
            };
        });
#pragma warning restore DCS003

        // ISqlDialect: プロジェクトの DatabaseType に応じた方言を提供します。
        services.AddScoped<ISqlDialect>(sp =>
        {
            var scope = sp.GetRequiredService<ProjectScope>();
            if (!scope.IsSet) return new SqliteDialect();
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
    public static IServiceCollection AddDynamicCrudCore(this IServiceCollection services)
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
        services.AddScoped<DynamicEntityFormValidationService>();
        services.AddScoped<CommandErrorHttpMapper>();
        services.AddScoped<IUserAuthService, UserAuthService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IHookExecutionTelemetry, HookExecutionTelemetryLogger>();
        services.AddScoped<IPagePermissionService, PagePermissionService>();
        // ファイルアップロードサービス
        services.AddScoped<IFileUploadService, FileUploadService>();
        // PDF エクスポートサービス
        services.AddSingleton<IPdfExportService, PdfExportService>();
        services.AddHostedService<CrmAutomationHostedService>();
        services.AddHostedService<NetYamlForge.Services.Validation.YamlConfigStartupValidator>();

        // バッチジョブサービス
        services.AddSingleton<IBatchJobLoader, BatchJobLoader>();
        services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<IBatchJobExecutor, BatchJobExecutor>();
        services.AddSingleton<IBatchJobHistoryStore, InMemoryBatchJobHistoryStore>();
        // BatchJobHostedService を Singleton として登録し、IBatchJobScheduler でも解決できるようにする
        services.AddSingleton<BatchJobHostedService>();
        services.AddSingleton<IBatchJobScheduler>(sp => sp.GetRequiredService<BatchJobHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<BatchJobHostedService>());

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

        // 汎用フック（関連データ操作）
        services.AddSingleton<IEntityHook, UpdateCountHook>();
        services.AddSingleton<IEntityHook, UpdateRelatedHook>();

        // 汎用フック（ソフト削除）
        services.AddSingleton<IEntityHook, SoftDeleteHook>();

        services.AddSingleton<IEntityHookRegistry, EntityHookRegistry>();

        return services;
    }
}
