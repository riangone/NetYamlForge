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

        // システムデータベース（system.db）を初期化
        await SystemDatabaseInitializer.InitializeAsync(logger);
        logger.LogInformation("システムデータベース初期化完了");

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

        // 全プロジェクト初期化完了後、テストユーザーを system.db に同期
        await SyncTestUsersToSystemDbAsync(projects, logger);
    }

    /// <summary>
    /// 全プロジェクトのテストユーザーを system.db に同期します。
    /// </summary>
    private static async Task SyncTestUsersToSystemDbAsync(IReadOnlyCollection<ProjectInfo> projects, ILogger logger)
    {
        var systemDbPath = Path.Combine(Directory.GetCurrentDirectory(), "system.db");
        if (!File.Exists(systemDbPath))
        {
            logger.LogWarning("system.db が見つかりません。テストユーザーの同期をスキップします。");
            return;
        }

        var projectUserInfos = projects.Select(p =>
        {
            var sqliteBuilder = new SqliteConnectionStringBuilder(p.ConnectionString);
            return new ProjectUserInfo
            {
                Name = p.Name,
                DisplayName = p.DisplayName ?? p.Name,
                DbPath = sqliteBuilder.DataSource
            };
        }).ToList();

        var seeder = new SystemDbTestUserSeeder();
        await seeder.SyncTestUsersToSystemDbAsync(systemDbPath, projectUserInfos, logger);
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
            await new ProjectSpecificInitializer().EnsureProjectSpecificColumnsAsync(
                conn, project.Name, project.DatabaseType, project.EntityMetadata, logger);
            return;
        }

        if (dbType is "postgresql" or "postgres")
        {
            await using var conn = new NpgsqlConnection(project.ConnectionString);
            await conn.OpenAsync();
            await new PostgreSqlAuthSchemaInitializer().InitializeAsync(conn, logger);
            await new DefaultAdminSeeder().EnsureDefaultAdminAsync(conn, logger);
            await new ProjectSpecificInitializer().EnsureProjectSpecificColumnsAsync(
                conn, project.Name, project.DatabaseType, project.EntityMetadata, logger);
            return;
        }

        if (dbType is "mysql" or "mariadb")
        {
            await using var conn = new MySqlConnection(project.ConnectionString);
            await conn.OpenAsync();
            await new MySqlAuthSchemaInitializer().InitializeAsync(conn, logger);
            await new DefaultAdminSeeder().EnsureDefaultAdminAsync(conn, logger);
            await new ProjectSpecificInitializer().EnsureProjectSpecificColumnsAsync(
                conn, project.Name, project.DatabaseType, project.EntityMetadata, logger);
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
        
        // 创建全局通用测试用户
        var commonTestUserSeeder = new CommonTestUserSeeder();
        await commonTestUserSeeder.EnsureCommonTestUsersAsync(conn, logger);
        
        // 运行项目特定的初始化和测试用户创建
        await new ProjectSpecificInitializer().EnsureProjectSpecificColumnsAsync(
            conn, project.Name, project.DatabaseType, project.EntityMetadata, logger);
    }
}
