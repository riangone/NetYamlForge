// ファイル概要：システムデータベース (system.db) の初期化クラス
// マルチテナント認証に必要な app_user, app_user_project_role, projects テーブルを作成

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services;

namespace NetYamlForge.Data.Schemas;

/// <summary>
/// システムデータベースのスキーマ初期化。
/// 
public static class SystemDatabaseInitializer
{
    // プロジェクトルートにある system.db を使用（bin/ ではなく）
    private static readonly string DbPath = Path.Combine(Directory.GetCurrentDirectory(), "system.db");

    /// <summary>
    /// システムデータベースを初期化します
    /// </summary>
    public static async Task InitializeAsync(ILogger logger)
    {
        // DCS003 抑制理由：システム DB 初期化は DI 開始前に実行されるため直接接続します
#pragma warning disable DCS003
        var connectionString = new SqliteConnectionStringBuilder { DataSource = DbPath }.ConnectionString;

        try
        {
            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync();

            // WAL + busy_timeout を有効にして並行性を向上（SqliteConnectionHardening に一元化）
            await NetYamlForge.Services.Connection.SqliteConnectionHardening.ApplyAsync(conn);
#pragma warning restore DCS003

            Console.WriteLine($"[SystemDatabase] Initializing: {DbPath}");
            logger.LogInformation("システムデータベース初期化中：{DbPath}", DbPath);

            // app_user テーブル
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS app_user (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_name TEXT NOT NULL UNIQUE,
                    password_hash TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    email TEXT,
                    phone TEXT,
                    user_type TEXT NOT NULL DEFAULT 'employee',
                    default_project_name TEXT,
                    is_admin INTEGER NOT NULL DEFAULT 0,
                    preferred_language TEXT NOT NULL DEFAULT 'ja-JP',
                    is_active INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                )
            ");

            // app_user_project_role テーブル
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS app_user_project_role (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    project_name TEXT NOT NULL,
                    role_name TEXT NOT NULL,
                    permission_scope TEXT,
                    assigned_by INTEGER,
                    created_at TEXT NOT NULL,
                    UNIQUE(user_id, project_name)
                )
            ");

            // app_user_role テーブル (全局角色)
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS app_user_role (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_name TEXT NOT NULL,
                    role_name TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    UNIQUE(user_name, role_name)
                )
            ");

            // projects テーブル（プロジェクトメタデータ）
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS projects (
                    name TEXT PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    description TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                )
            ");

            // インデックス作成
            await conn.ExecuteAsync(@"
                CREATE INDEX IF NOT EXISTS IX_app_user_user_name ON app_user(user_name);
                CREATE INDEX IF NOT EXISTS IX_app_user_email ON app_user(email);
                CREATE INDEX IF NOT EXISTS IX_app_user_user_type ON app_user(user_type);
                CREATE INDEX IF NOT EXISTS IX_app_user_project_role_user_id ON app_user_project_role(user_id);
                CREATE INDEX IF NOT EXISTS IX_app_user_project_role_project_name ON app_user_project_role(project_name);
            ");

            // 既存の app_user テーブルに新しいカラムを追加 (マイグレーション)
            try
            {
                await conn.ExecuteAsync("ALTER TABLE app_user ADD COLUMN is_admin INTEGER NOT NULL DEFAULT 0;");
                logger.LogInformation("app_user テーブルに is_admin カラムを追加しました");
            }
            catch { /* カラムが既に存在する場合の例外は無視 */ }

            try
            {
                await conn.ExecuteAsync("ALTER TABLE app_user ADD COLUMN preferred_language TEXT NOT NULL DEFAULT 'ja-JP';");
                logger.LogInformation("app_user テーブルに preferred_language カラムを追加しました");
            }
            catch { /* カラムが既に存在する場合の例外は無視 */ }

            try
            {
                await conn.ExecuteAsync("ALTER TABLE app_user ADD COLUMN last_login_at TEXT;");
                logger.LogInformation("app_user テーブルに last_login_at カラムを追加しました");
            }
            catch { /* カラムが既に存在する場合の例外は無視 */ }

            try
            {
                // SystemDbTestUserSeeder が参照する所属プロジェクト列。
                // 新規作成の system.db には存在しないため、ここで追加します。
                await conn.ExecuteAsync("ALTER TABLE app_user ADD COLUMN owning_project TEXT;");
                logger.LogInformation("app_user テーブルに owning_project カラムを追加しました");
            }
            catch { /* カラムが既に存在する場合の例外は無視 */ }

            // デフォルト管理者ユーザーを作成（存在しない場合）
            await EnsureDefaultAdminAsync(conn, logger);

            logger.LogInformation("システムデータベース初期化完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FATAL ERROR] SystemDatabase initialization failed: {ex.Message}");
            logger.LogError(ex, "システムデータベースの初期化に失敗しました：{DbPath}", DbPath);
            throw;
        }

    }

    /// <summary>
    /// デフォルト管理者ユーザーを作成します
    /// </summary>
    private static async Task EnsureDefaultAdminAsync(SqliteConnection conn, ILogger logger)
    {
        var existing = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM app_user WHERE user_name = @UserName",
            new { UserName = "admin" }
        );

        if (existing == 0)
        {
            var passwordHash = HashPassword("Admin@123");
            await conn.ExecuteAsync(@"
                INSERT INTO app_user (user_name, password_hash, display_name, email, user_type, is_admin, preferred_language, is_active, created_at, updated_at)
                VALUES (@UserName, @PasswordHash, @DisplayName, @Email, @UserType, 1, 'ja-JP', 1, @CreatedAt, @UpdatedAt)
            ", new
            {
                UserName = "admin",
                PasswordHash = passwordHash,
                DisplayName = "系统管理者",
                Email = "admin@example.com",
                UserType = "employee",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            logger.LogInformation("デフォルト管理者ユーザーを作成しました：admin / Admin@123");
        }
    }

    /// <summary>
    /// プロジェクトメタデータを同期します
    /// </summary>
    public static async Task SyncProjectsAsync(IEnumerable<ProjectInfo> projects, ILogger logger)
    {
        // DCS003 抑制理由：システム DB 初期化は DI 開始前に実行されるため直接接続します
#pragma warning disable DCS003
        var connectionString = new SqliteConnectionStringBuilder { DataSource = DbPath }.ConnectionString;

        try
        {
            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync();
#pragma warning restore DCS003
            await NetYamlForge.Services.Connection.SqliteConnectionHardening.ApplyAsync(conn);

            foreach (var project in projects)
            {
                await conn.ExecuteAsync(@"
                    INSERT OR REPLACE INTO projects (name, display_name, description, created_at, updated_at)
                    VALUES (@Name, @DisplayName, @Description, @CreatedAt, @UpdatedAt)
                ", new
                {
                    Name = project.Name,
                    DisplayName = project.DisplayName,
                    Description = "",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            logger.LogInformation("プロジェクトメタデータを同期しました：{Count} 件", projects.Count());

            // admin ユーザーに全プロジェクトの Admin ロールを付与
            await EnsureAdminProjectRolesAsync(conn, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "プロジェクトメタデータの同期に失敗しました");
            throw;
        }
    }

    /// <summary>
    /// admin ユーザーにプロジェクト役割を付与します
    /// </summary>
    private static async Task EnsureAdminProjectRolesAsync(SqliteConnection conn, ILogger logger)
    {
        // admin ユーザーが存在するか確認
        var adminUser = await conn.QueryFirstOrDefaultAsync<(int Id, string? DefaultProjectName)>(@"
            SELECT id, default_project_name FROM app_user WHERE user_name = 'admin'
        ");

        if (adminUser.Id == 0)
        {
            return; // admin ユーザーがいない場合は何もしない
        }

        // 全プロジェクトに Admin ロールを付与
        await conn.ExecuteAsync(@"
            INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, created_at)
            SELECT @UserId, name, 'Admin', @CreatedAt
            FROM projects
            WHERE NOT EXISTS (
                SELECT 1 FROM app_user_project_role WHERE user_id = @UserId AND project_name = projects.name
            )
        ", new
        {
            UserId = adminUser.Id,
            CreatedAt = DateTime.UtcNow
        });

        // デフォルトプロジェクトが設定されていない場合は設定
        if (string.IsNullOrEmpty(adminUser.DefaultProjectName))
        {
            // 'framework' プロジェクトを優先的に検索
            var defaultProject = await conn.ExecuteScalarAsync<string>(
                "SELECT name FROM projects WHERE name = 'framework' LIMIT 1"
            );

            // 'framework' がない場合は最初に見つかったプロジェクトを使用
            if (string.IsNullOrEmpty(defaultProject))
            {
                defaultProject = await conn.ExecuteScalarAsync<string>(
                    "SELECT name FROM projects ORDER BY created_at LIMIT 1"
                );
            }

            if (!string.IsNullOrEmpty(defaultProject))
            {
                await conn.ExecuteAsync(@"
                    UPDATE app_user SET default_project_name = @ProjectName WHERE user_name = 'admin'
                ", new { ProjectName = defaultProject });

                logger.LogInformation("admin ユーザーのデフォルトプロジェクトを設定しました：{ProjectName}", defaultProject);
            }
        }

        logger.LogInformation("admin ユーザーに全プロジェクトの Admin ロールを付与しました");
    }

    /// <summary>
    /// パスワードをハッシュします（ASP.NET Core Identity PasswordHasher 互換）
    /// </summary>
    private static string HashPassword(string password)
    {
        var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
        return passwordHasher.HashPassword(null!, password);
    }
}
