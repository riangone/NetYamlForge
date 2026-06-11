// ファイル概要: 「YAML 設定 → ランタイム挙動」E2E テスト用の WebApplicationFactory です。
// 一時ディレクトリにサンプルプロジェクト（blog）のコピーを作り、それを
// コンテンツルートとしてアプリ全体を起動します。テナント DB（SQLite）は
// コピーに含めず、フレームワーク起動時の初期化（init_seed.sql ＋ YAML からの
// 自動テーブル作成）で毎回再構築させます。これにより開発者の作業 DB を
// 汚さず、繰り返し実行・並列 CI 実行でも安定して動作します。

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace NetYamlForge.Tests.Integration;

/// <summary>
/// blog サンプルプロジェクトだけを読み込む隔離されたテストホストファクトリ。
/// 共有フィクスチャとして利用し、将来の E2E テストはこのクラスに乗せて拡張できます。
/// </summary>
public sealed class NetYamlForgeWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>テスト対象のサンプルプロジェクト名。</summary>
    public const string ProjectName = "blog";

    /// <summary>一時コンテンツルート（テスト終了時に削除されます）。</summary>
    public string TempContentRoot { get; }

    /// <summary>テナント SQLite DB のフルパス（フレームワークが起動時に再構築）。</summary>
    public string TenantDbPath =>
        Path.Combine(TempContentRoot, "projects", ProjectName, "database", $"{ProjectName}.db");

    /// <summary>テナント DB への接続文字列（テスト側からの検証クエリ用）。</summary>
    public string TenantDbConnectionString => $"Data Source={TenantDbPath};Pooling=False";

    // DB ファイル・一時生成物はコピーしない（フレームワークに再構築させる）
    private static readonly string[] ExcludedFileSuffixes = { ".db", ".db-wal", ".db-shm", ".env" };

    // ジョブ（cron）と独自コントローラーは E2E 最小経路に不要なため除外
    private static readonly string[] ExcludedDirectoryNames = { "jobs", "Controllers", "bin", "obj" };

    public NetYamlForgeWebApplicationFactory()
    {
        var appProjectDir = FindAppProjectDirectory();
        TempContentRoot = Path.Combine(Path.GetTempPath(), "nyf-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempContentRoot);

        // アプリ設定をコピー（Serilog / HotReload 設定など）
        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var source = Path.Combine(appProjectDir, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(TempContentRoot, fileName));
            }
        }

        // UseStaticFiles 用に空の wwwroot を用意
        Directory.CreateDirectory(Path.Combine(TempContentRoot, "wwwroot"));

        // サンプルプロジェクトを DB ファイル抜きでコピー
        var sourceProjectDir = Path.Combine(appProjectDir, "projects", ProjectName);
        if (!Directory.Exists(sourceProjectDir))
        {
            throw new DirectoryNotFoundException(
                $"サンプルプロジェクトが見つかりません: {sourceProjectDir}");
        }

        var targetProjectDir = Path.Combine(TempContentRoot, "projects", ProjectName);
        CopyDirectoryFiltered(sourceProjectDir, targetProjectDir);

        // DB 再構築先の database/ ディレクトリを保証
        Directory.CreateDirectory(Path.Combine(targetProjectDir, "database"));

        // 空の DB ファイルを置く：DB ファイルが無い場合 ChinookDownloader が
        // Chinook サンプル DB をネットワークから取得して流用してしまうため、
        // 空ファイルを先に作って init_seed.sql ＋ YAML 自動マイグレーションの
        // 決定的な再構築パスを通す（CI のネットワーク依存も排除）。
        using (File.Create(TenantDbPath)) { }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(TempContentRoot);
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Cookie 認証をテスト用スキームで置き換え、全リクエストを
            // Admin ロールのテストユーザーとして認証する
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(TempContentRoot))
            {
                Directory.Delete(TempContentRoot, recursive: true);
            }
        }
        catch
        {
            // 一時ディレクトリの削除失敗はテスト結果に影響させない
        }
    }

    /// <summary>
    /// テスト実行ディレクトリから上方向に辿り、NetYamlForge アプリのソース
    /// ディレクトリ（projects/ サンプルを含む）を特定します。
    /// </summary>
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
