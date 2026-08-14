// ファイル概要: ai-card プロジェクトの E2E テスト用ファクトリです。
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

public sealed class AiCardTestFixture : WebApplicationFactory<Program>
{
    public const string ProjectName = "ai-card";

    public string TempContentRoot { get; }

    public string TenantDbPath =>
        Path.Combine(TempContentRoot, "projects", ProjectName, "database", $"{ProjectName}.db");

    public string TenantDbConnectionString => $"Data Source={TenantDbPath};Pooling=False";

    private static readonly string[] ExcludedFileSuffixes = { ".db", ".db-wal", ".db-shm", ".env" };
    private static readonly string[] ExcludedDirectoryNames = { "jobs", "Controllers", "bin", "obj" };

    public AiCardTestFixture()
    {
        var appProjectDir = FindAppProjectDirectory();
        TempContentRoot = Path.Combine(Path.GetTempPath(), "nyf-ai-card-e2e", Guid.NewGuid().ToString("N"));
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
CREATE TABLE IF NOT EXISTS [ai_identity] (
  [id] INTEGER PRIMARY KEY AUTOINCREMENT,
  [ai_id] TEXT NOT NULL,
  [display_name] TEXT NOT NULL,
  [owner_type] TEXT NOT NULL DEFAULT 'individual',
  [organization] TEXT,
  [role] TEXT,
  [email] TEXT,
  [endpoint_url] TEXT,
  [public_key] TEXT,
  [is_active] INTEGER NOT NULL DEFAULT 1,
  [verified] INTEGER NOT NULL DEFAULT 0,
  [created_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  [updated_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS [ai_profile] (
  [id] INTEGER PRIMARY KEY AUTOINCREMENT,
  [ai_identity_id] INTEGER NOT NULL,
  [goals_json] TEXT,
  [can_share_json] TEXT,
  [cannot_share_json] TEXT,
  [expertise_json] TEXT,
  [greeting_message] TEXT,
  [ai_instructions] TEXT,
  [updated_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY ([ai_identity_id]) REFERENCES [ai_identity]([id])
);
CREATE TABLE IF NOT EXISTS [handshake_session] (
  [id] INTEGER PRIMARY KEY AUTOINCREMENT,
  [session_token] TEXT NOT NULL,
  [initiator_id] INTEGER NOT NULL,
  [responder_name] TEXT,
  [responder_email] TEXT,
  [responder_company] TEXT,
  [state] TEXT NOT NULL DEFAULT 'pending',
  [handshake_type] TEXT NOT NULL DEFAULT 'asymmetric',
  [intent_type] TEXT,
  [intent_topic] TEXT,
  [intent_context_json] TEXT,
  [offered_permissions_json] TEXT,
  [granted_permissions_json] TEXT,
  [conversation_summary] TEXT,
  [next_actions_json] TEXT,
  [expires_at] TEXT,
  [created_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  [updated_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY ([initiator_id]) REFERENCES [ai_identity]([id])
);
CREATE TABLE IF NOT EXISTS [chat_message] (
  [id] INTEGER PRIMARY KEY AUTOINCREMENT,
  [session_id] INTEGER NOT NULL,
  [role] TEXT NOT NULL,
  [content] TEXT NOT NULL,
  [metadata_json] TEXT,
  [created_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY ([session_id]) REFERENCES [handshake_session]([id])
);
CREATE TABLE IF NOT EXISTS [ahp_permission] (
  [id] INTEGER PRIMARY KEY AUTOINCREMENT,
  [ai_identity_id] INTEGER NOT NULL,
  [resource_type] TEXT NOT NULL,
  [resource_name] TEXT NOT NULL,
  [access_level] TEXT NOT NULL DEFAULT 'public',
  [expires_at] TEXT,
  [is_active] INTEGER NOT NULL DEFAULT 1,
  [created_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY ([ai_identity_id]) REFERENCES [ai_identity]([id])
);
CREATE TABLE IF NOT EXISTS [qr_token] (
  [id] INTEGER PRIMARY KEY AUTOINCREMENT,
  [ai_identity_id] INTEGER NOT NULL,
  [token] TEXT NOT NULL,
  [intent_type] TEXT,
  [intent_topic] TEXT,
  [qr_url] TEXT NOT NULL,
  [scan_count] INTEGER NOT NULL DEFAULT 0,
  [max_scans] INTEGER,
  [is_active] INTEGER NOT NULL DEFAULT 1,
  [expires_at] TEXT,
  [created_at] TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY ([ai_identity_id]) REFERENCES [ai_identity]([id])
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