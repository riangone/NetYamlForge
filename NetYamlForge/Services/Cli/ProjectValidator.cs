// CLI コマンド --validate-project のエントリポイント。
// サブプロジェクトのナビリンク・シードデータ・ホームページを静的検証する。

using System.Text.RegularExpressions;

namespace NetYamlForge.Services.Cli;

public static class ProjectValidator
{
    private static readonly string[] PlaceholderKeywords =
        ["TBD", "N/A", "TODO", "PLACEHOLDER", "Optimized", "Enhanced", "Coming Soon"];

    public static int Run(string rootDir, string? projectName, CliScaffoldResult result)
    {

        var projectsDir = ResolveProjectsDir(rootDir);
        if (projectsDir is null)
        {
            result.Success = false;
            result.ExitCode = 1;
            result.Errors.Add("projects/ ディレクトリが見つかりません。");
            Console.Error.WriteLine("❌ projects/ ディレクトリが見つかりません。");
            return 1;
        }

        var projects = string.IsNullOrWhiteSpace(projectName)
            ? GetAllProjects(projectsDir)
            : [projectName];

        var allPass = true;

        foreach (var proj in projects)
        {
            result.Project = proj;
            var projectDir = Path.Combine(projectsDir, proj);
            if (!Directory.Exists(projectDir))
            {
                Fail(result, $"[{proj}] プロジェクトディレクトリが見つかりません: {projectDir}");
                allPass = false;
                continue;
            }

            Console.WriteLine($"\n=== Validating: {proj} ===");
            var pass = ValidateProject(proj, projectDir, result);
            allPass = allPass && pass;
        }

        result.Success = allPass;
        result.ExitCode = allPass ? 0 : 1;

        Console.WriteLine();
        if (allPass)
            WriteColored("✅ All checks PASSED", ConsoleColor.Green);
        else
            WriteColored($"❌ {result.Errors.Count} issue(s) found", ConsoleColor.Red);

        return result.ExitCode;
    }

    // ── per-project checks ────────────────────────────────────────────────────

    private static bool ValidateProject(string projName, string projectDir, CliScaffoldResult result)
    {
        var pass = true;
        pass &= CheckNavLinks(projName, projectDir, result);
        pass &= CheckSeedData(projName, projectDir, result);
        pass &= CheckHomePage(projName, projectDir, result);
        CheckDuplicateAutoItems(projName, projectDir, result);
        return pass;
    }

    // ── 1. nav / home-page link resolution ───────────────────────────────────

    private static bool CheckNavLinks(string projName, string projectDir, CliScaffoldResult result)
    {
        var pass = true;
        var urls = new List<(string source, string url)>();

        foreach (var f in new[]
        {
            Path.Combine(projectDir, "project.yaml"),
            Path.Combine(projectDir, "config", "layout.yml"),
            Path.Combine(projectDir, "config", "home-page.yml"),
        })
        {
            if (File.Exists(f))
                urls.AddRange(ExtractUrls(File.ReadAllText(f), Path.GetFileName(f)));
        }

        var pagesDir    = Path.Combine(projectDir, "pages");
        var entitiesDir = Path.Combine(projectDir, "entities");

        Console.WriteLine("[1] Nav / URL links");
        foreach (var (source, url) in urls.DistinctBy(x => x.url))
        {
            if (!ResolveUrl(url, projName, pagesDir, entitiesDir, out var why))
            {
                Fail(result, $"[{projName}] リンク切れ ({source}): {url} — {why}");
                pass = false;
            }
            else
            {
                Console.WriteLine($"  ✅ {url}");
            }
        }

        return pass;
    }

    private static bool ResolveUrl(string url, string projName, string pagesDir, string entitiesDir, out string reason)
    {
        reason = "";

        // /proj/Page/Name
        var pageM = Regex.Match(url, $@"/{Regex.Escape(projName)}/Page/(\w+)$", RegexOptions.IgnoreCase);
        if (pageM.Success)
        {
            var name = pageM.Groups[1].Value;
            if (!File.Exists(Path.Combine(pagesDir, $"{name}.yaml")) &&
                !File.Exists(Path.Combine(pagesDir, $"{name}.yml")))
            {
                reason = $"pages/{name}.yaml が存在しません";
                return false;
            }
            return true;
        }

        // /proj/DynamicEntity/List/table — 誤ったパターン (List action は存在しない)
        if (Regex.IsMatch(url, $@"/{Regex.Escape(projName)}/DynamicEntity/List/", RegexOptions.IgnoreCase))
        {
            reason = "DynamicEntity/List/<table> は無効です。正しくは DynamicEntity/Index?entity=<table>";
            return false;
        }

        // /proj/DynamicEntity/Index?entity=table
        var entM = Regex.Match(url, $@"/{Regex.Escape(projName)}/DynamicEntity/Index\?entity=(\w+)$", RegexOptions.IgnoreCase);
        if (entM.Success)
        {
            var tbl = entM.Groups[1].Value;
            if (!File.Exists(Path.Combine(entitiesDir, $"{tbl}.yml")) &&
                !File.Exists(Path.Combine(entitiesDir, $"{tbl}.yaml")))
            {
                reason = $"entities/{tbl}.yml が存在しません";
                return false;
            }
            return true;
        }

        // /proj/Dashboard  ← always valid (framework route)
        if (Regex.IsMatch(url, $@"/{Regex.Escape(projName)}/Dashboard$", RegexOptions.IgnoreCase))
            return true;

        // Other framework or external links — skip
        return true;
    }

    // ── 2. seed data ─────────────────────────────────────────────────────────

    private static bool CheckSeedData(string projName, string projectDir, CliScaffoldResult result)
    {
        var pass = true;
        var entitiesDir = Path.Combine(projectDir, "entities");
        if (!Directory.Exists(entitiesDir)) return true;

        Console.WriteLine("[2] Seed data (init_seed.sql)");

        var seedFile = Path.Combine(projectDir, "database", "init_seed.sql");
        if (!File.Exists(seedFile))
        {
            Warn($"  ⚠️  [{projName}] init_seed.sql が存在しません");
            // Not hard-fail — project may intend empty DB
            return true;
        }

        var seed = File.ReadAllText(seedFile).ToLowerInvariant();

        foreach (var entityFile in Directory.GetFiles(entitiesDir, "*.yml")
                                            .Concat(Directory.GetFiles(entitiesDir, "*.yaml")))
        {
            var tbl = Path.GetFileNameWithoutExtension(entityFile).ToLowerInvariant();
            // Also match CamelCase variant (e.g. entity "filter_demo" → table "FilterDemo" → lowercase "filterdemo")
            var tblNoUnderscore = tbl.Replace("_", "");
            if (seed.Contains($"into {tbl}") ||
                seed.Contains($"into \"{tbl}\"") ||
                seed.Contains($"into [{tbl}]") ||
                seed.Contains($"into {tblNoUnderscore}") ||
                seed.Contains($"into \"{tblNoUnderscore}\"") ||
                seed.Contains($"into [{tblNoUnderscore}]"))
            {
                Console.WriteLine($"  ✅ Seed: {tbl}");
            }
            else
            {
                Fail(result, $"[{projName}] シードデータなし: '{tbl}' の INSERT が init_seed.sql に見つかりません");
                pass = false;
            }
        }

        return pass;
    }

    // ── 3. home-page placeholder ──────────────────────────────────────────────

    private static bool CheckHomePage(string projName, string projectDir, CliScaffoldResult result)
    {
        var file = Path.Combine(projectDir, "config", "home-page.yml");
        if (!File.Exists(file)) return true;

        Console.WriteLine("[3] home-page.yml placeholders");

        var content = File.ReadAllText(file);
        var pass = true;

        foreach (var kw in PlaceholderKeywords)
        {
            // Use case-sensitive word-boundary match to avoid false positives (e.g. "Todo App" vs "TODO")
            var pattern = $@"\b{Regex.Escape(kw)}\b";
            if (Regex.IsMatch(content, pattern))
            {
                Fail(result, $"[{projName}] home-page.yml にプレースホルダー '{kw}' が含まれています");
                pass = false;
            }
        }

        if (pass) Console.WriteLine("  ✅ プレースホルダーなし");
        return pass;
    }

    // ── 4. duplicate auto-items (warning only, not hard fail) ────────────────

    private static void CheckDuplicateAutoItems(string projName, string projectDir, CliScaffoldResult result)
    {
        Console.WriteLine("[4] Duplicate auto-nav items");

        foreach (var yamlPath in new[]
        {
            Path.Combine(projectDir, "project.yaml"),
            Path.Combine(projectDir, "config", "layout.yml"),
        })
        {
            if (!File.Exists(yamlPath)) continue;
            var c = File.ReadAllText(yamlPath);

            var showDashboard = Regex.IsMatch(c, @"showDashboard\s*:\s*true", RegexOptions.IgnoreCase);
            var showPages     = Regex.IsMatch(c, @"showPages\s*:\s*true",     RegexOptions.IgnoreCase);
            var showEntities  = Regex.IsMatch(c, @"showEntities\s*:\s*true",  RegexOptions.IgnoreCase);

            var fn = Path.GetFileName(yamlPath);

            if (showDashboard && Regex.IsMatch(c, @"url\s*:.*?/Dashboard", RegexOptions.IgnoreCase))
                Warn($"  ⚠️  {fn}: showDashboard=true かつ手動ダッシュボードリンクあり（重複）");

            if (showPages && ExtractUrls(c, fn).Any(x => x.url.Contains("/Page/")))
                Warn($"  ⚠️  {fn}: showPages=true かつ手動 /Page/ リンクあり（重複）");

            if (showEntities && ExtractUrls(c, fn).Any(x => x.url.Contains("/DynamicEntity/")))
                Warn($"  ⚠️  {fn}: showEntities=true かつ手動 /DynamicEntity/ リンクあり（重複）");
        }

        Console.WriteLine("  ✅ 重複チェック完了");
    }

    // ── URL extraction ────────────────────────────────────────────────────────

    private static IEnumerable<(string source, string url)> ExtractUrls(string yaml, string source)
    {
        var pattern = new Regex(@"(?:url|primaryActionUrl|secondaryActionUrl)\s*:\s*([^\s#\r\n]+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        foreach (Match m in pattern.Matches(yaml))
        {
            var url = m.Groups[1].Value.Trim().Trim('"', '\'');
            if (url.StartsWith('/'))
                yield return (source, url);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string[] GetAllProjects(string projectsDir)
        => Directory.GetDirectories(projectsDir)
            .Select(Path.GetFileName)
            .Where(p => !string.IsNullOrEmpty(p) && !p!.StartsWith('_'))
            .ToArray()!;

    private static string? ResolveProjectsDir(string rootDir)
    {
        var a = Path.Combine(rootDir, "projects");
        if (Directory.Exists(a)) return a;
        var b = Path.Combine(rootDir, "NetYamlForge", "projects");
        if (Directory.Exists(b)) return b;
        return null;
    }

    private static void Warn(string msg) => WriteColored(msg, ConsoleColor.Yellow);

    private static void Fail(CliScaffoldResult result, string msg)
    {
        result.Errors.Add(msg);
        WriteColored($"  ❌ {msg}", ConsoleColor.Red);
    }

    private static void WriteColored(string msg, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(msg);
        Console.ForegroundColor = prev;
    }
}
