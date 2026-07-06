// ファイル概要: R2-01 PR-4。`--validate-schemas` CLI 入口の実体。
// Web ホストを起動せずに SchemaValidationRunner を実行し、
// GitHub Actions 注釈形式（::error file=...）で違反を出力、違反有無で終了コード 0/1 を返す。
// CI（build-and-test.yml）から `dotnet run --project NetYamlForge -c Release -- --validate-schemas` で呼ばれる。

using Microsoft.Extensions.Configuration;

namespace NetYamlForge.Services.Validation;

/// <summary>
/// R2-01: プロジェクト YAML のスキーマ検証を CLI として実行するヘルパー。
/// アプリの通常起動フローとは独立に呼び出せ、CI のゲートに用いる。
/// </summary>
public static class SchemaValidationCli
{
    /// <summary>
    /// スキーマ検証を実行し、終了コードを返す（0=違反なし / 1=違反あり）。
    /// </summary>
    public static int Run(string[] args)
    {
        // appsettings + 環境変数 + コマンドラインから Forge:SchemaValidation を読み取る。
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var options = new SchemaValidationOptions();
        config.GetSection(SchemaValidationOptions.SectionName).Bind(options);

        // CLI は明示的なリンターとして呼ばれるため、Mode=Off 以外は常に検証し、
        // 違反があれば終了コード 1 を返す（Warn/Strict の区別は CLI では失敗可否に影響しない）。
        if (options.Mode == SchemaValidationMode.Off)
        {
            Console.WriteLine("schema-lint: mode=Off, skipped.");
            return 0;
        }

        var projectsRoot = ResolveProjectsRoot();
        if (projectsRoot is null)
        {
            Console.Error.WriteLine(
                "::warning::schema-lint: projects ルートが見つかりませんでした（projects/ または NetYamlForge/projects/）。検証をスキップします。");
            return 0;
        }

        var runner = new SchemaValidationRunner();
        var violations = runner.ValidateAll(projectsRoot, options);

        if (violations.Count == 0)
        {
            Console.WriteLine($"schema-lint: OK — 違反なし（root={projectsRoot}）。");
            return 0;
        }

        foreach (var v in violations)
        {
            // GitHub Actions 注釈形式。行番号は未追跡のため 1 固定。
            var file = ToRelative(v.FilePath);
            var message = Sanitize($"[{v.SchemaName}] {v.Pointer}: {v.Message}");
            Console.WriteLine($"::error file={file},line=1::{message}");
        }

        Console.Error.WriteLine($"schema-lint: {violations.Count} 件の違反を検出しました。");
        return 1;
    }

    /// <summary>projects ルートの候補を順に探し、最初に存在するものを返す。</summary>
    internal static string? ResolveProjectsRoot()
    {
        var cwd = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(cwd, "projects"),
            Path.Combine(cwd, "NetYamlForge", "projects"),
            Path.Combine(AppContext.BaseDirectory, "projects"),
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>可能なら実行ディレクトリからの相対パスに変換（GH 注釈のリンク精度向上）。</summary>
    private static string ToRelative(string fullPath)
    {
        try
        {
            var rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath);
            return rel.Replace('\\', '/');
        }
        catch
        {
            return fullPath.Replace('\\', '/');
        }
    }

    /// <summary>GH 注釈が壊れないよう改行を除去する。</summary>
    private static string Sanitize(string s) =>
        s.Replace("\r", " ").Replace("\n", " ").Trim();
}
