// ファイル概要: DB初期化の協調器。各プロジェクトの DB を種別別に初期化します。
// 実装は以下のファイルに委譲: Schemas/、Seeders/、ProjectSpecificInitializer.cs。
// マルチプロジェクト対応: ProjectManager.GetAll() で全プロジェクトの DB を初期化します。
//
// DCS003 抑制理由: DB初期化はDIセットアップ前に実行されるため接続を直接生成します。
#pragma warning disable DCS003

using System.Data;
using Dapper;
using NetYamlForge.Data.Schemas;
using NetYamlForge.Data.Seeders;
using NetYamlForge.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;

namespace NetYamlForge.Data;

/// <summary>
/// DB初期化の協調器。各プロジェクトの初期化をスキーマ・シーダーに委譲します。
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
        var projectManager = scope.ServiceProvider.GetRequiredService<ProjectManager>();

        var projects = projectManager.GetAll();
        if (projects.Count == 0)
        {
            logger.LogWarning("初期化対象のプロジェクトが見つかりません。projects/ ディレクトリを確認してください。");
            return;
        }

        foreach (var project in projects)
        {
            logger.LogInformation("プロジェクト '{Name}' の DB を初期化中...", project.Name);
            try
            {
                await InitializeProjectAsync(project, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "プロジェクト '{Name}' の DB 初期化に失敗しました", project.Name);
                throw;
            }
        }
    }

    private static async Task InitializeProjectAsync(ProjectInfo project, ILogger logger)
    {
        var dbType = project.DatabaseType.ToLowerInvariant();

        if (dbType == "sqlserver")
        {
            await using var conn = new SqlConnection(project.ConnectionString);
            await conn.OpenAsync();
            await new SqlServerAuthSchemaInitializer().InitializeAsync(conn, logger);
            await new DefaultAdminSeeder().EnsureDefaultAdminAsync(conn, logger);
            return;
        }

        if (dbType is "postgresql" or "postgres")
        {
            await using var conn = new NpgsqlConnection(project.ConnectionString);
            await conn.OpenAsync();
            await new PostgreSqlAuthSchemaInitializer().InitializeAsync(conn, logger);
            await new DefaultAdminSeeder().EnsureDefaultAdminAsync(conn, logger);
            return;
        }

        if (dbType is "mysql" or "mariadb")
        {
            await using var conn = new MySqlConnection(project.ConnectionString);
            await conn.OpenAsync();
            await new MySqlAuthSchemaInitializer().InitializeAsync(conn, logger);
            await new DefaultAdminSeeder().EnsureDefaultAdminAsync(conn, logger);
            return;
        }

        // SQLite (デフォルト)
        await InitializeSqliteProjectAsync(project, logger);
    }

    private static async Task InitializeSqliteProjectAsync(ProjectInfo project, ILogger logger)
    {
        var sqliteBuilder = new SqliteConnectionStringBuilder(project.ConnectionString);
        var dbPath = sqliteBuilder.DataSource;

        await new ChinookDownloader().EnsureChinookDatabaseAsync(dbPath, project.ProjectDir, logger);

        await using var conn = new SqliteConnection(project.ConnectionString);
        await conn.OpenAsync();

        await new SqliteAuthSchemaInitializer().InitializeAsync(conn, logger);
        await new DefaultAdminSeeder().EnsureDefaultAdminAsync(conn, logger);
        await new RbacSeeder().EnsureRbacRolesAsync(conn, logger);
        await new ProjectSpecificInitializer().EnsureProjectSpecificColumnsAsync(conn, project.Name, logger);

        // salesforce-crm 専用の処理（他プロジェクトでは実行しない）
        if (string.Equals(project.Name, "salesforce-crm", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureCrmSchemaAsync((IDbConnection)conn, logger);
            await new RbacSeeder().EnsureSalesforcePermissionsAsync(conn, project.Name, logger);
            await new CrmSeeder().EnsureCrmPoliciesAndRulesAsync(conn, project.Name, logger);
        }
    }

    private static async Task EnsureCrmSchemaAsync(IDbConnection conn, ILogger logger)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS CrmLead (
    LeadId INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL UNIQUE,
    LeadStage TEXT NOT NULL,
    OwnerUserName TEXT,
    Score INTEGER NOT NULL DEFAULT 0,
    LastTouchAt TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CrmCase (
    CaseId INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderId INTEGER NOT NULL UNIQUE,
    Severity TEXT NOT NULL,
    CaseStatus TEXT NOT NULL,
    OwnerUserName TEXT,
    SlaDueAt TEXT,
    RootCause TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CrmQuote (
    QuoteId INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderId INTEGER NOT NULL UNIQUE,
    QuoteStatus TEXT NOT NULL,
    DiscountRate REAL NOT NULL DEFAULT 0,
    TotalAmount REAL NOT NULL DEFAULT 0,
    VersionNo INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CrmContract (
    ContractId INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderId INTEGER NOT NULL UNIQUE,
    ContractState TEXT NOT NULL,
    StartDate TEXT,
    EndDate TEXT,
    ValueAmount REAL NOT NULL DEFAULT 0,
    RenewalRisk TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CrmTaskActivity (
    ActivityId INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityType TEXT NOT NULL,
    EntityId INTEGER NOT NULL,
    ActivityType TEXT NOT NULL,
    Status TEXT NOT NULL,
    DueAt TEXT,
    OwnerUserName TEXT,
    PayloadJson TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CrmApprovalRequest (
    ApprovalId INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityType TEXT NOT NULL,
    EntityId INTEGER NOT NULL,
    RequestType TEXT NOT NULL,
    ApprovalStatus TEXT NOT NULL,
    RequestedBy TEXT,
    ApproverUserName TEXT,
    Reason TEXT,
    RequestedAt TEXT NOT NULL,
    DecidedAt TEXT
);

CREATE TABLE IF NOT EXISTS CrmSlaPolicy (
    PolicyId INTEGER PRIMARY KEY AUTOINCREMENT,
    PolicyName TEXT NOT NULL,
    TargetEntity TEXT NOT NULL,
    TargetStatus TEXT NOT NULL,
    DueHours INTEGER NOT NULL,
    Priority TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CrmAutomationRule (
    RuleId INTEGER PRIMARY KEY AUTOINCREMENT,
    RuleName TEXT NOT NULL UNIQUE,
    TriggerCondition TEXT NOT NULL,
    Action TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    LastRunAt TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);";
        await conn.ExecuteAsync(sql);
        logger.LogInformation("CRM スキーマ確認済み (salesforce-crm 専用)");
    }
}
