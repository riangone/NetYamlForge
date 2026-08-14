// ファイル概要: memo-app プロジェクトの E2E テスト用ファクトリです。
// --ai-scaffold によって自動生成されました。

#pragma warning disable DCS003

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace NetYamlForge.Tests.Integration;

public sealed class MemoAppTestFixture : WebApplicationFactory<Program>
{
    public const string ProjectName = "memo-app";

    public string TempContentRoot { get; }

    public string TenantDbPath =>
        Path.Combine(TempContentRoot, "projects", ProjectName, "database", $"{ProjectName}.db");

    public string TenantDbConnectionString => $"Data Source={TenantDbPath};Pooling=False";

    private static readonly string[] ExcludedFileSuffixes = { ".db", ".db-wal", ".db-shm", ".env" };
    private static readonly string[] ExcludedDirectoryNames = { "jobs", "Controllers", "bin", "obj" };

    public MemoAppTestFixture()
    {
        var appProjectDir = FindAppProjectDirectory();
        TempContentRoot = Path.Combine(Path.GetTempPath(), "nyf-memo-app-e2e", Guid.NewGuid().ToString("N"));
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
            throw new DirectoryNotFoundException($"プロジェクトが見つかりません: {sourceProjectDir}");
        }

        var targetProjectDir = Path.Combine(TempContentRoot, "projects", ProjectName);
        CopyDirectoryFiltered(sourceProjectDir, targetProjectDir);

        Directory.CreateDirectory(Path.Combine(targetProjectDir, "database"));

        CreateTenantSchema();
    }

    private void CreateTenantSchema()
    {
        using var conn = new SqliteConnection(TenantDbConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS [memo] (
  [id] INTEGER PRIMARY KEY AUTOINCREMENT,
  [title] TEXT NOT NULL,
  [body] TEXT,
  [priority] TEXT NOT NULL DEFAULT 'normal',
  [created_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
";
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