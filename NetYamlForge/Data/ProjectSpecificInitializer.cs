// ファイル概要：プロジェクト固有の DB マイグレーション（列追加等）を実行する初期化クラスです。
// DbInitializer から呼ばれ、既存テーブルへの ALTER TABLE ADD COLUMN を安全に行います。
// 例：attendance-ops プロジェクトの ApprovedAt / ApprovedBy 列の追加。

using System.Data;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using NetYamlForge.Data.Seeders;
using NetYamlForge.Services;
using NetYamlForge.Models;
using NetYamlForge.Services.DynamicEntity;

namespace NetYamlForge.Data;

/// <summary>
/// プロジェクト固有の DB 初期化（カラム追加等）。
/// 例：attendance-ops プロジェクトに ApprovedAt カラムを追加。
/// </summary>
public class ProjectSpecificInitializer
{
    private readonly CommonTestUserSeeder _commonTestUserSeeder = new();
    private readonly AutoDealerTestUserSeeder _autoDealerTestUserSeeder = new();
    private readonly ProjectSpecificTestUserSeeder _projectSpecificTestUserSeeder = new();

    public async Task EnsureProjectSpecificColumnsAsync(
        IDbConnection conn,
        string projectName,
        string dbType,
        IEntityMetadataProvider metadataProvider,
        ILogger logger,
        string? projectDir = null)
    {
        // 首先运行项目特定的初始化和数据种子
        // contact-manager: 初期データロード
        if (string.Equals(projectName, "contact-manager", StringComparison.OrdinalIgnoreCase))
        {
            await InitializeContactManagerAsync(conn as SqliteConnection, logger);
        }
        // todo-app: プロジェクト固有テーブルを init_seed.sql から初期化
        else if (string.Equals(projectName, "todo-app", StringComparison.OrdinalIgnoreCase))
        {
            await InitializeTodoAppAsync(conn as SqliteConnection, logger);
            // 创建 todo-app 测试用户
            await _projectSpecificTestUserSeeder.EnsureProjectSpecificTestUsersAsync(conn, projectName, logger);
        }
        // task-management: init.sql でテーブル作成後、CreatedBy 列追加 & TaskComment テーブル作成
        else if (string.Equals(projectName, "task-management", StringComparison.OrdinalIgnoreCase))
        {
            await RunInitSeedSqlIfExistsAsync(conn as SqliteConnection, projectName, logger, projectDir);
            await EnsureColumnAsync(conn as SqliteConnection, "Task", "CreatedBy", "TEXT", logger);
            await EnsureTaskCommentTableAsync(conn as SqliteConnection, logger);
            // 创建 task-management 测试用户
            await _projectSpecificTestUserSeeder.EnsureProjectSpecificTestUsersAsync(conn, projectName, logger);
        }
        // attendance-ops: 承認カラム追加
        else if (string.Equals(projectName, "attendance-ops", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureColumnAsync(conn as SqliteConnection, "LeaveRequest", "ApprovedAt", "TEXT", logger);
            await EnsureColumnAsync(conn as SqliteConnection, "OvertimeRequest", "ApprovedAt", "TEXT", logger);
        }
        else
        {
            // 汎用フォールバック: database/init_seed.sql が存在すれば実行
            await RunInitSeedSqlIfExistsAsync(conn as SqliteConnection, projectName, logger, projectDir);

            // auto-dealer-demo: デモユーザー作成 + テストユーザー作成
            if (string.Equals(projectName, "auto-dealer-demo", StringComparison.OrdinalIgnoreCase))
            {
                await new AutoDealerDemoSeeder().EnsureDemoUsersAsync(conn, logger);
                // 创建汽车销售项目的全面测试用户
                await _autoDealerTestUserSeeder.EnsureAutoDealerTestUsersAsync(conn, logger);
            }
            // framework: 框架管理测试用户
            else if (string.Equals(projectName, "framework", StringComparison.OrdinalIgnoreCase))
            {
                await _projectSpecificTestUserSeeder.EnsureProjectSpecificTestUsersAsync(conn, projectName, logger);
            }
            // biz-docs: 业务文档测试用户
            else if (string.Equals(projectName, "biz-docs", StringComparison.OrdinalIgnoreCase))
            {
                await _projectSpecificTestUserSeeder.EnsureProjectSpecificTestUsersAsync(conn, projectName, logger);
            }
            // inventory: 库存管理测试用户
            else if (string.Equals(projectName, "inventory", StringComparison.OrdinalIgnoreCase))
            {
                await _projectSpecificTestUserSeeder.EnsureProjectSpecificTestUsersAsync(conn, projectName, logger);
            }
            // ui-showcase: UI 展示测试用户
            else if (string.Equals(projectName, "ui-showcase", StringComparison.OrdinalIgnoreCase))
            {
                await _projectSpecificTestUserSeeder.EnsureProjectSpecificTestUsersAsync(conn, projectName, logger);
            }
            // 其他项目使用通用测试用户
            else
            {
                await _projectSpecificTestUserSeeder.EnsureProjectSpecificTestUsersAsync(conn, projectName, logger);
            }
        }

        // 调用基于 YAML 实体配置的自动物理列追加迁移机制
        await AutoMigrateMissingColumnsAsync(conn, projectName, dbType, metadataProvider, logger);
    }

    // 認証テーブル名（エンティティテーブル判定から除外）
    private static readonly HashSet<string> AuthTableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AppUser", "AppUserRole", "AppRolePermission", "AppUserSavedView", "AuditLog"
    };

    /// <summary>
    /// エンティティテーブルが存在しない場合のみ、database/init.sql → init_seed.sql の順で実行します。
    /// 既にテーブルが存在する場合は既存データを保持してスキップします。
    /// </summary>
    private static async Task RunInitSeedSqlIfExistsAsync(
        SqliteConnection? conn,
        string projectName,
        ILogger logger,
        string? projectDir = null)
    {
        if (conn == null) return;

        // エンティティテーブルが既に存在するか確認
        var tables = await conn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table'");
        var hasEntityTables = tables.Any(t => !AuthTableNames.Contains(t) && !t.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase));
        if (hasEntityTables)
        {
            logger.LogDebug("プロジェクト '{Name}' のエンティティテーブルは既に存在します。初期化をスキップします。", projectName);
            return;
        }

        string ResolveDbFile(string fileName) =>
            new[]
            {
                // プロジェクトの実ディレクトリ（ProjectManager が解決済み）を最優先。
                // CWD やビルド出力に依存せず、コンテンツルートが変わっても動作する。
                string.IsNullOrWhiteSpace(projectDir)
                    ? string.Empty
                    : Path.Combine(projectDir, "database", fileName),
                Path.Combine(AppContext.BaseDirectory, "projects", projectName, "database", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName, "database", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "NetYamlForge", "projects", projectName, "database", fileName),
            }.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p)) ?? string.Empty;

        // init.sql でテーブルを作成してから init_seed.sql でデータを投入
        var initSqlPath = ResolveDbFile("init.sql");
        if (!string.IsNullOrEmpty(initSqlPath))
        {
            logger.LogInformation("プロジェクト '{Name}' の init.sql を実行します: {Path}", projectName, initSqlPath);
            var initSql = await File.ReadAllTextAsync(initSqlPath);
            await conn.ExecuteAsync(initSql);
        }

        var seedSqlPath = ResolveDbFile("init_seed.sql");
        if (!string.IsNullOrEmpty(seedSqlPath))
        {
            logger.LogInformation("プロジェクト '{Name}' の init_seed.sql を実行します: {Path}", projectName, seedSqlPath);
            var seedSql = await File.ReadAllTextAsync(seedSqlPath);
            await conn.ExecuteAsync(seedSql);
        }

        if (!string.IsNullOrEmpty(initSqlPath) || !string.IsNullOrEmpty(seedSqlPath))
        {
            logger.LogInformation("プロジェクト '{Name}' の DB 初期化が完了しました。", projectName);
        }
    }

    /// <summary>
    /// task-management の TaskComment テーブルを作成します（存在しない場合のみ）。
    /// </summary>
    private static async Task EnsureTaskCommentTableAsync(SqliteConnection? conn, ILogger logger)
    {
        if (conn == null) return;

        var tables = await conn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='TaskComment'");
        if (tables.Any())
        {
            logger.LogDebug("TaskComment テーブルは既に存在します。スキップします。");
            return;
        }

        // DCS001 抑制理由：ハードコードされたスキーマ定義のため安全
#pragma warning disable DCS001
        await conn.ExecuteAsync(@"
CREATE TABLE ""TaskComment"" (
    ""Id""          INTEGER PRIMARY KEY AUTOINCREMENT,
    ""TaskId""      INTEGER NOT NULL,
    ""CommentText"" TEXT    NOT NULL,
    ""PostedBy""    TEXT    NOT NULL DEFAULT 'unknown',
    ""PostedAt""    TEXT    NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY (""TaskId"") REFERENCES ""Task""(""Id"")
)");
#pragma warning restore DCS001
        logger.LogInformation("TaskComment テーブルを作成しました。");
    }

    /// <summary>
    /// todo-app プロジェクトの初期化（テーブルが存在しない場合 init_seed.sql を実行）
    /// </summary>
    private static async Task InitializeTodoAppAsync(SqliteConnection? conn, ILogger logger)
    {
        if (conn == null) return;

        // Category テーブルの存在確認
        var tables = await conn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='Category'");
        if (tables.Any())
        {
            logger.LogInformation("todo-app のテーブルは既に存在します。初期化をスキップします。");
            return;
        }

        // init_seed.sql を検索（複数のパスを试行）
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "projects", "todo-app", "database", "init_seed.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "projects", "todo-app", "database", "init_seed.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "NetYamlForge", "projects", "todo-app", "database", "init_seed.sql"),
        };

        var initSqlPath = candidates.FirstOrDefault(File.Exists);
        if (initSqlPath == null)
        {
            logger.LogWarning("todo-app の初期化スクリプトが見つかりません。検索パス: {Paths}",
                string.Join(", ", candidates));
            return;
        }

        logger.LogInformation("todo-app の初期化スクリプトを実行します: {Path}", initSqlPath);
        var sql = await File.ReadAllTextAsync(initSqlPath);
        await conn.ExecuteAsync(sql);
        logger.LogInformation("todo-app の初期化が完了しました。");
    }

    /// <summary>
    /// contact-manager プロジェクトの初期化
    /// </summary>
    private static async Task InitializeContactManagerAsync(SqliteConnection? conn, ILogger logger)
    {
        if (conn == null) return;

        var initSqlPath = Path.Combine(AppContext.BaseDirectory, "projects", "contact-manager", "database", "init.sql");
        
        if (!File.Exists(initSqlPath))
        {
            // 開発環境用のパス
            initSqlPath = Path.Combine(Directory.GetCurrentDirectory(), "NetYamlForge", "projects", "contact-manager", "database", "init.sql");
        }

        if (!File.Exists(initSqlPath))
        {
            logger.LogWarning("contact-manager の初期化スクリプトが見つかりません：{Path}", initSqlPath);
            return;
        }

        // テーブル存在チェック
        var tables = await conn.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type='table'");
        var tableList = tables.ToList();

        if (tableList.Contains("contact") && tableList.Contains("company") && tableList.Contains("interaction"))
        {
            logger.LogInformation("contact-manager のテーブルは既に存在します。初期化をスキップします。");
            return;
        }

        logger.LogInformation("contact-manager の初期化スクリプトを実行します...");
        var sql = await File.ReadAllTextAsync(initSqlPath);
        await conn.ExecuteAsync(sql);
        logger.LogInformation("contact-manager の初期化が完了しました。");
    }

    /// <summary>
    /// SQLite テーブルに列が存在しない場合、追加します。
    /// </summary>
    private static async Task EnsureColumnAsync(
        SqliteConnection? conn,
        string tableName,
        string columnName,
        string columnType,
        ILogger logger)
    {
        if (conn == null) return;

        // DCS001 抑制理由：tableName/columnName/columnType はすべて呼び出し元ハードコード値（ユーザー入力なし）
#pragma warning disable DCS001
        var columns = await conn.QueryAsync<string>($"SELECT name FROM pragma_table_info('{tableName}')");
        if (columns.Any(c => string.Equals(c, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await conn.ExecuteAsync($"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnType}");
#pragma warning restore DCS001
        logger.LogInformation("列を追加しました：{Table}.{Column}", tableName, columnName);
    }

    /// <summary>
    /// auto-dealer-demo プロジェクトの初期化
    /// </summary>
    private static async Task InitializeAutoDealerDemoAsync(SqliteConnection? conn, ILogger logger)
    {
        if (conn == null) return;

        // customers テーブルの存在確認
        var tables = await conn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='customers'");
        if (tables.Any())
        {
            logger.LogInformation("auto-dealer-demo のテーブルは既に存在します。初期化をスキップします。");
            return;
        }

        // init.sql を検索（複数のパスを試行）
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "projects", "auto-dealer-demo", "database", "init.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "projects", "auto-dealer-demo", "database", "init.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "NetYamlForge", "projects", "auto-dealer-demo", "database", "init.sql"),
        };

        var initSqlPath = candidates.FirstOrDefault(File.Exists);
        if (initSqlPath == null)
        {
            logger.LogWarning("auto-dealer-demo の初期化スクリプトが見つかりません。検索パス：{Paths}",
                string.Join(", ", candidates));
            return;
        }

        logger.LogInformation("auto-dealer-demo の初期化スクリプトを実行します：{Path}", initSqlPath);
        var sql = await File.ReadAllTextAsync(initSqlPath);
        await conn.ExecuteAsync(sql);
        logger.LogInformation("auto-dealer-demo の初期化が完了しました。");
    }

    /// <summary>
    /// 基于 YAML 实体定义的自动物理列追加迁移机制
    /// </summary>
    private static async Task AutoMigrateMissingColumnsAsync(
        IDbConnection conn,
        string projectName,
        string dbType,
        IEntityMetadataProvider metadataProvider,
        ILogger logger)
    {
        if (conn is not System.Data.Common.DbConnection dbConn) return;

        try
        {
            var entities = metadataProvider.GetAll();
            foreach (var entry in entities)
            {
                var entityName = entry.Key;
                var entity = entry.Value;
                var tableName = entity.Table;

                if (string.IsNullOrWhiteSpace(tableName)) continue;

                // 1. 检查表是否存在
                bool tableExists;
                try
                {
                    using var cmd = dbConn.CreateCommand();
                    cmd.CommandText = $"SELECT 1 FROM \"{tableName}\" WHERE 1=0";
                    await cmd.ExecuteNonQueryAsync();
                    tableExists = true;
                }
                catch
                {
                    tableExists = false;
                }

                if (!tableExists)
                {
                    logger.LogInformation("项目 '{Project}' 检测到表 '{Table}' 在数据库中不存在。正在自动创建该表...", 
                        projectName, tableName);
                    await AutoCreateTableAsync(dbConn, tableName, entity, dbType, logger);
                    tableExists = true;
                }

                // 2. 获取表目前已有的物理列
                var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = dbConn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT * FROM \"{tableName}\" WHERE 1=0";
                    using var reader = await cmd.ExecuteReaderAsync();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        existingColumns.Add(reader.GetName(i));
                    }
                }

                // 3. 对比 YAML 定义 of Columns，找出物理缺失的列
                if (entity.Columns == null) continue;

                foreach (var colEntry in entity.Columns)
                {
                    var colName = colEntry.Key;
                    var colDef = colEntry.Value;

                    // 如果是虚拟计算列（包含 Expression），不需要在数据库中添加实际物理列
                    if (!string.IsNullOrWhiteSpace(colDef.Expression)) continue;

                    // 如果已经是主键或者物理列已存在，跳过
                    if (existingColumns.Contains(colName)) continue;

                    // 4. 执行自动列迁移
                    var sqlType = SqlTypeMapper.MapYamlTypeToSqlType(colDef.Type, dbType);
                    
                    logger.LogInformation("项目 '{Project}' 检测到表 '{Table}' 缺失物理列 '{Column}' (YAML 定义类型为 '{YamlType}')。正在自动追加物理列...", 
                        projectName, tableName, colName, colDef.Type);

                    // DCS001 抑制理由：表名、列名和数据类型均为经过校验或映射的安全值，非外部传入的用户输入
#pragma warning disable DCS001
                    var alterSql = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{colName}\" {sqlType}";
                    if (string.Equals(dbType, "sqlserver", StringComparison.OrdinalIgnoreCase))
                    {
                        alterSql = $"ALTER TABLE \"{tableName}\" ADD \"{colName}\" {sqlType}";
                    }
                    
                    using var alterCmd = dbConn.CreateCommand();
                    alterCmd.CommandText = alterSql;
                    await alterCmd.ExecuteNonQueryAsync();
#pragma warning restore DCS001

                    logger.LogInformation("项目 '{Project}' 的表 '{Table}' 已成功追加物理列 '{Column}'", projectName, tableName, colName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "项目 '{Project}' 在执行通用列自动迁移时遇到异常", projectName);
        }
    }

    /// <summary>
    /// 当 YAML 中定义的表不存在时，自动根据其属性列创建对应物理表
    /// </summary>
    private static async Task AutoCreateTableAsync(
        System.Data.Common.DbConnection dbConn,
        string tableName,
        EntityDefinition entity,
        string dbType,
        ILogger logger)
    {
        var createTableSql = TableDdlBuilder.BuildCreateTableSql(tableName, entity, dbType);
        logger.LogInformation("自动建表 SQL: {Sql}", createTableSql);

        using var cmd = dbConn.CreateCommand();
        cmd.CommandText = createTableSql;
        await cmd.ExecuteNonQueryAsync();
    }
}
