// ファイル概要: golden-template サンプルプロジェクトを起動する E2E テスト用ファクトリです。
// NetYamlForgeWebApplicationFactory（blog 用）を踏襲しつつ、golden-template の
// ticket / ticket_comment テーブルを「NOT NULL DEFAULT」付きの実スキーマで
// 事前構築します。
//
// なぜ事前構築が必要か:
//   起動時の AutoMigrateMissingColumnsAsync は「テーブルが存在しない場合のみ」
//   YAML 定義から自動生成します。YAML には default/notnull 情報が無いため、
//   自動生成すると status/priority は NULL 許容列になり、本来の
//   「NOT NULL DEFAULT」不一致バグ（--ai-scaffold が直接 CREATE TABLE した
//   実 DB では発生する）が再現できません。
//   そこで実 DB と同一スキーマを先に作り、既存テーブルとして温存させます。

using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

// DCS003 抑制理由: テストファクトリ内で実 DB と同一スキーマのテナント DB を
// 事前構築するために直接 SQLite 接続を生成します（アプリ側 DI は使用不可）。
#pragma warning disable DCS003
namespace NetYamlForge.Tests.Integration;

/// <summary>
/// golden-template プロジェクトだけを読み込む隔離テストホスト。
/// ticket テーブルは実 DB と同じ「NOT NULL DEFAULT」スキーマで事前構築します。
/// </summary>
public sealed class GoldenTemplateWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ProjectName = "golden-template";

    public string TempContentRoot { get; }

    public string TenantDbPath =>
        Path.Combine(TempContentRoot, "projects", ProjectName, "database", $"{ProjectName}.db");

    public string TenantDbConnectionString => $"Data Source={TenantDbPath};Pooling=False";

    private static readonly string[] ExcludedFileSuffixes = { ".db", ".db-wal", ".db-shm", ".env" };
    private static readonly string[] ExcludedDirectoryNames = { "jobs", "Controllers", "bin", "obj" };

    public GoldenTemplateWebApplicationFactory()
    {
        var appProjectDir = FindAppProjectDirectory();
        TempContentRoot = Path.Combine(Path.GetTempPath(), "nyf-golden-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempContentRoot);

        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var source = Path.Combine(appProjectDir, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(TempContentRoot, fileName));
            }
        }

        Directory.CreateDirectory(Path.Combine(TempContentRoot, "wwwroot"));

        var sourceProjectDir = Path.Combine(appProjectDir, "projects", ProjectName);
        if (!Directory.Exists(sourceProjectDir))
        {
            throw new DirectoryNotFoundException($"サンプルプロジェクトが見つかりません: {sourceProjectDir}");
        }

        var targetProjectDir = Path.Combine(TempContentRoot, "projects", ProjectName);
        CopyDirectoryFiltered(sourceProjectDir, targetProjectDir);

        Directory.CreateDirectory(Path.Combine(targetProjectDir, "database"));

        // 実 DB と同一スキーマ（NOT NULL DEFAULT）でテナント DB を事前構築する。
        // これにより起動時の YAML 自動建表（NULL 許容になる）を回避し、
        // 実運用で発生したバグ条件を忠実に再現する。
        CreateTenantSchema();
    }

    private void CreateTenantSchema()
    {
        using var conn = new SqliteConnection(TenantDbConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS [ticket] (
  [id] INTEGER PRIMARY KEY AUTOINCREMENT,
  [subject] TEXT NOT NULL,
  [status] TEXT NOT NULL DEFAULT 'open',
  [priority] TEXT NOT NULL DEFAULT 'normal',
  [created_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS [ticket_comment] (
  [id] INTEGER PRIMARY KEY AUTOINCREMENT,
  [ticket_id] INTEGER NOT NULL,
  [body] TEXT NOT NULL,
  [created_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY ([ticket_id]) REFERENCES [ticket]([id])
);";
        cmd.ExecuteNonQuery();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(TempContentRoot);
        builder.UseEnvironment("Development");

        var tempSystemDb = Path.Combine(TempContentRoot, "system_test.db");
        builder.ConfigureAppConfiguration((context, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SystemDbPath"] = tempSystemDb
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                    .Build();
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            SqliteConnection.ClearAllPools();
            TestProcessCwdGuard.RestoreSafeCwd();
            if (Directory.Exists(TempContentRoot))
            {
                Directory.Delete(TempContentRoot, recursive: true);
            }
        }
        catch
        {
            // 一時ディレクトリ削除失敗はテスト結果に影響させない
        }
    }

    private static string FindAppProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "NetYamlForge");
            if (File.Exists(Path.Combine(candidate, "Program.cs")) &&
                Directory.Exists(Path.Combine(candidate, "projects", ProjectName)))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "NetYamlForge アプリプロジェクトのディレクトリが見つかりません。" +
            $"探索開始位置: {AppContext.BaseDirectory}");
    }

    private static void CopyDirectoryFiltered(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (ExcludedFileSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            File.Copy(file, Path.Combine(targetDir, name));
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (ExcludedDirectoryNames.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            CopyDirectoryFiltered(subDir, Path.Combine(targetDir, name));
        }
    }
}
