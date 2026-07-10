// ファイル概要: SystemDatabaseInitializer.SyncProjectsAsync の回帰テストです。
// 背景: この処理は ProjectManager が実際にロードしたプロジェクトを system.db の
// projects / app_user_project_role テーブルへ反映し、admin に各プロジェクトの
// アクセス権を自動付与します。過去に Program.cs の起動シーケンスへ配線されておらず、
// 「projects/ に新しいサブプロジェクトを追加しても /UserHome （マイホーム）の
// 一覧に永久に出てこない」という不具合が発生していました（golden-template で発覚）。
// この呼び出しが再び外れることを防ぐための直接検証です。

using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NetYamlForge.Data.Schemas;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class SystemDatabaseInitializerSyncProjectsTests
{
    private static ProjectInfo MakeFakeProject(string name) => new ProjectInfo
    {
        Name = name,
        DisplayName = $"{name} 表示名",
        ProjectDir = "/tmp/does-not-matter",
        ConnectionString = "Data Source=:memory:",
    };

    [Fact]
    public async Task SyncProjectsAsync_RegistersNewProject_AndGrantsAdminRole()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"nyf-systemdb-test-{Guid.NewGuid():N}.db");
        try
        {
            var logger = NullLogger.Instance;

            // 1) system.db の初期化（テーブル作成 + デフォルト admin ユーザー作成）
            await SystemDatabaseInitializer.InitializeAsync(logger, dbPath);

            // 2) ProjectManager が「golden-template」を含む複数プロジェクトをロードした状況を模擬
            var projects = new[]
            {
                MakeFakeProject("todo-app"),
                MakeFakeProject("golden-template"),
            };
            await SystemDatabaseInitializer.SyncProjectsAsync(projects, logger, dbPath);

            // 3) projects テーブルに golden-template が登録されていること
            var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
#pragma warning disable DCS003 // テストコードから直接検証用の接続を張るため許容
            await using var conn = new SqliteConnection(connectionString);
#pragma warning restore DCS003
            await conn.OpenAsync();

            var projectNames = (await conn.QueryAsync<string>("SELECT name FROM projects")).ToList();
            Assert.Contains("golden-template", projectNames);
            Assert.Contains("todo-app", projectNames);

            // 4) admin ユーザーに golden-template への Admin ロールが自動付与されていること
            var adminRoleCount = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM app_user_project_role pr
                JOIN app_user u ON u.id = pr.user_id
                WHERE u.user_name = 'admin' AND pr.project_name = 'golden-template'");
            Assert.Equal(1, adminRoleCount);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}
