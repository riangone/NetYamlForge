// ファイル概要: 最小構成のサブプロジェクト雛形を生成します。

using System.Text;
using System.Text.RegularExpressions;
using NetYamlForge.Models;
using NetYamlForge.Services.Cli;

namespace NetYamlForge.Services;

public static class ProjectTemplateScaffolder
{
    private enum I18nFallbackMode
    {
        Display,
        Raw
    }

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly Regex ProjectNamePattern = new("^[a-z0-9][a-z0-9-]{1,62}$", RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedDbTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqlite", "sqlserver", "postgresql", "postgres", "mysql", "mariadb"
    };

    public static int Run(
        string currentDir,
        string? projectName,
        string? displayName = null,
        bool forceOverwrite = false,
        string? dbType = null,
        string? dbPath = null,
        string? dbConnectionString = null,
        bool autoScaffold = true,
        string? i18nFallbackMode = null,
        CliScaffoldResult? result = null)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            Console.Error.WriteLine("--project=<name> を指定してください。");
            return 1;
        }

        projectName = projectName.Trim();
        if (!ProjectNamePattern.IsMatch(projectName))
        {
            Console.Error.WriteLine("project 名は 2-63 文字の英小文字・数字・ハイフンのみ使用できます。");
            return 1;
        }

        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? ToDisplayName(projectName)
            : displayName.Trim();
        var resolvedDbType = string.IsNullOrWhiteSpace(dbType) ? "sqlite" : dbType.Trim().ToLowerInvariant();
        var fallbackMode = ResolveI18nFallbackMode(i18nFallbackMode);
        if (fallbackMode == null)
        {
            Console.Error.WriteLine("未対応の i18n fallback mode です。--i18n-fallback-mode=display|raw を指定してください。");
            return 1;
        }

        if (!SupportedDbTypes.Contains(resolvedDbType))
        {
            Console.Error.WriteLine($"未対応の db type です: {resolvedDbType}");
            return 1;
        }

        if (resolvedDbType == "sqlite")
        {
            dbPath = string.IsNullOrWhiteSpace(dbPath) ? $"database/{projectName}.db" : dbPath.Trim();
        }
        else
        {
            dbPath = null;
            if (string.IsNullOrWhiteSpace(dbConnectionString))
            {
                Console.Error.WriteLine($"db type={resolvedDbType} の場合は --db-connection を指定してください。");
                return 1;
            }
        }

        var contentRoot = ResolveContentRoot(currentDir);
        var projectsRoot = Path.Combine(contentRoot, "projects");
        var targetDir = Path.Combine(projectsRoot, projectName);

        if (Directory.Exists(targetDir))
        {
            if (!forceOverwrite)
            {
                Console.Error.WriteLine($"既に存在します: {targetDir}");
                Console.Error.WriteLine("上書きする場合は --force を指定してください。");
                return 1;
            }

            Directory.Delete(targetDir, recursive: true);
        }

        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(Path.Combine(targetDir, "config"));
        Directory.CreateDirectory(Path.Combine(targetDir, "database"));
        Directory.CreateDirectory(Path.Combine(targetDir, "entities"));
        Directory.CreateDirectory(Path.Combine(targetDir, "pages"));
        Directory.CreateDirectory(Path.Combine(targetDir, "docs"));
        Directory.CreateDirectory(Path.Combine(targetDir, "views"));

        WriteFile(Path.Combine(targetDir, "project.yaml"), BuildProjectYaml(projectName, resolvedDisplayName, resolvedDbType, dbPath, dbConnectionString));
        WriteFile(Path.Combine(targetDir, "config", "home-page.yml"), BuildHomePageYaml(projectName, resolvedDisplayName));
        WriteFile(Path.Combine(targetDir, "config", "layout.yml"), BuildLayoutYaml(projectName, resolvedDisplayName));
        WriteFile(Path.Combine(targetDir, "config", "i18n.yml"), BuildI18nYaml(projectName));
        WriteFile(Path.Combine(targetDir, "docs", "README-ja.md"), BuildReadme(projectName, resolvedDisplayName));
        WriteFile(Path.Combine(targetDir, "views", "_ViewImports.cshtml"), BuildViewImportsCshtml());
        WriteFile(Path.Combine(targetDir, "views", "_ViewStart.cshtml"), BuildViewStartCshtml());
        WriteFile(Path.Combine(targetDir, "views", "_Layout.cshtml"), BuildProjectLayoutCshtml(projectName, resolvedDisplayName));

        if (resolvedDbType == "sqlite" && !string.IsNullOrWhiteSpace(dbPath))
        {
            var dbFilePath = Path.IsPathRooted(dbPath)
                ? dbPath
                : Path.GetFullPath(Path.Combine(targetDir, dbPath));
            var dbDir = Path.GetDirectoryName(dbFilePath);
            if (!string.IsNullOrWhiteSpace(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }
            if (!File.Exists(dbFilePath))
            {
                WriteFile(dbFilePath, string.Empty);
            }
        }
        WriteFile(Path.Combine(targetDir, "entities", ".gitkeep"), string.Empty);
        WriteFile(Path.Combine(targetDir, "pages", ".gitkeep"), string.Empty);
        WriteFile(Path.Combine(targetDir, "views", ".gitkeep"), string.Empty);
        WriteFile(Path.Combine(targetDir, "dashboard.yml"), BuildDashboardYaml(projectName, Array.Empty<string>()));

        if (autoScaffold)
        {
            var scaffoldGeneratedResult = EntityYamlScaffolder.Run(contentRoot, projectName, true, "entities.generated", true);
            var scaffoldEntitiesResult = EntityYamlScaffolder.Run(contentRoot, projectName, true, "entities", true);
            if (scaffoldGeneratedResult != 0 || scaffoldEntitiesResult != 0)
            {
                Console.Error.WriteLine("entities 自動生成に失敗しました。DB 接続設定を確認してください。");
                return 1;
            }

            var entityNames = LoadEntityNames(targetDir);
            WriteFile(Path.Combine(targetDir, "dashboard.yml"), BuildDashboardYaml(projectName, entityNames));
            WriteStarterPageAndView(targetDir, projectName, entityNames);
            WriteFile(Path.Combine(targetDir, "config", "i18n.yml"), BuildI18nYamlFromProjectFiles(targetDir, projectName, fallbackMode.Value));
        }

        Console.WriteLine($"[ok] project template created: {targetDir}");
        Console.WriteLine($"next: dotnet run -- --scaffold-entities --project={projectName}");
        if (result != null)
        {
            result.Project = projectName;
            result.GeneratedFiles.Add(targetDir);
            result.NextSteps.Add($"dotnet run -- --scaffold-entities --project={projectName}");
        }
        return 0;
    }

    private static string ResolveContentRoot(string currentDir)
    {
        if (Directory.Exists(Path.Combine(currentDir, "projects")))
        {
            return currentDir;
        }

        var sub = Path.Combine(currentDir, "NetYamlForge");
        if (Directory.Exists(Path.Combine(sub, "projects")))
        {
            return sub;
        }

        throw new DirectoryNotFoundException($"content root を解決できません: {currentDir}");
    }

    private static void WriteFile(string path, string content)
    {
        File.WriteAllText(path, content, Utf8NoBom);
    }

    private static string ToDisplayName(string projectName)
    {
        var parts = projectName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return projectName;
        }

        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string BuildProjectYaml(
        string projectName,
        string displayName,
        string dbType,
        string? dbPath,
        string? dbConnectionString)
    {
        var dbSection = string.Equals(dbType, "sqlite", StringComparison.OrdinalIgnoreCase)
            ? $"  type: {dbType}\n  path: {dbPath}"
            : $"  type: {dbType}\n  connectionString: {dbConnectionString}";

        return $$"""
name: {{projectName}}
displayName: {{displayName}}
version: "1.0.0"

database:
{{dbSection}}

features:
  multiLanguage: true
  userAuthentication: true
  dashboard: true
  pages: false

layout:
  dashboardTheme: workspace
  header:
    title: "{{displayName}}"
  navigation:
    showDashboard: true
    entities: []
    items:
      - label: Home
        url: /{{projectName}}
        icon: 🏠
      - label: Dashboard
        controller: Dashboard
        action: Index
        icon: 📊

home_page:
  icon: "🗂️"
  tagline: "{{displayName}}"
  tags: []
""" + Environment.NewLine;
    }

    private static string BuildHomePageYaml(string projectName, string displayName)
    {
        return $$"""
hero:
  eyebrow: {{displayName}}
  title: Welcome to {{displayName}}
  description: Edit entities/ YAML files to customize your screens. Run --scaffold-entities to regenerate from DB.
  primaryActionLabel: Open Dashboard
  primaryActionUrl: /{{projectName}}/Dashboard
  secondaryActionLabel: View Schema
  secondaryActionUrl: /{{projectName}}/DynamicEntity/AllDefinitions
  highlights:
    - YAML First
    - Auto CRUD
    - Multi-language

quickActions:
  - label: Dashboard
    url: /{{projectName}}/Dashboard
    style: btn-primary
    icon: 📊
  - label: Entity Schema
    url: /{{projectName}}/DynamicEntity/AllDefinitions
    style: btn-outline
    icon: 🧩
  - label: Starter Page
    url: /{{projectName}}/Page/StarterOverview
    style: btn-outline
    icon: 🧭
  - label: Docs
    url: /{{projectName}}/docs/README-ja.md
    style: btn-ghost
    icon: 📖
""" + Environment.NewLine;
    }

    private static string BuildLayoutYaml(string projectName, string displayName)
    {
        return $$"""
header:
  title: {{displayName}}
  showProjectBadge: true

sidebar:
  enabled: false

navigation:
  showDashboard: true
  entities: []
  items:
    - label: Home
      url: /{{projectName}}
      icon: 🏠
    - label: Dashboard
      controller: Dashboard
      action: Index
      icon: 📊
    - label: Schema
      controller: DynamicEntity
      action: AllDefinitions
      icon: 🧩

footer:
  text: "{{displayName}} — powered by NetYamlForge"
  showVersion: true
""" + Environment.NewLine;
    }

    private static string BuildI18nYaml(string projectName)
    {
        return $$"""
# i18n translation file for project: {{projectName}}
# Keys are referenced by labelKey / titleKey / descriptionKey in YAML definitions.
# Supported locales: en-US, zh-CN, ja-JP, ko-KR
# Add keys here as you add labelKey references to entities/, pages/, dashboard.yml, etc.
translations:
  nav.home:
    en-US: Home
    zh-CN: 首页
    ja-JP: ホーム
    ko-KR: 홈
  nav.dashboard:
    en-US: Dashboard
    zh-CN: 仪表盘
    ja-JP: ダッシュボード
    ko-KR: 대시보드
  nav.schema:
    en-US: Schema
    zh-CN: 数据模型
    ja-JP: スキーマ
    ko-KR: 스키마
  common.search:
    en-US: Search
    zh-CN: 搜索
    ja-JP: 検索
    ko-KR: 검색
  common.reset:
    en-US: Reset
    zh-CN: 重置
    ja-JP: リセット
    ko-KR: 초기화
  common.save:
    en-US: Save
    zh-CN: 保存
    ja-JP: 保存
    ko-KR: 저장
  common.delete:
    en-US: Delete
    zh-CN: 删除
    ja-JP: 削除
    ko-KR: 삭제
  common.cancel:
    en-US: Cancel
    zh-CN: 取消
    ja-JP: キャンセル
    ko-KR: 취소
  common.edit:
    en-US: Edit
    zh-CN: 编辑
    ja-JP: 編集
    ko-KR: 편집
  common.create:
    en-US: Create
    zh-CN: 新建
    ja-JP: 作成
    ko-KR: 만들기
  common.total:
    en-US: Total
    zh-CN: 合计
    ja-JP: 合計
    ko-KR: 합계
  common.loading:
    en-US: Loading...
    zh-CN: 加载中...
    ja-JP: 読み込み中...
    ko-KR: 로딩 중...
""" + Environment.NewLine;
    }

    private static string[] LoadEntityNames(string projectDir)
    {
        var entitiesDir = Path.Combine(projectDir, "entities");
        if (!Directory.Exists(entitiesDir))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(entitiesDir, "*.yml")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static string BuildDashboardYaml(string projectName, IReadOnlyList<string> entityNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("stats:");
        if (entityNames.Count == 0)
        {
            sb.AppendLine("  # Add stat cards here. Example:");
            sb.AppendLine("  # - label: Total Records");
            sb.AppendLine("  #   entity: my_entity");
            sb.AppendLine("  #   aggregate: count");
            sb.AppendLine("  #   icon: 📊");
            sb.AppendLine("  #   color: badge-info");
            sb.AppendLine("  []");
        }
        else
        {
            var colors = new[] { "badge-info", "badge-success", "badge-warning", "badge-error", "badge-primary", "badge-secondary" };
            for (var i = 0; i < entityNames.Count; i++)
            {
                var entity = entityNames[i];
                var label = $"{ToDisplayName(entity)} Count";
                var color = colors[i % colors.Length];
                sb.AppendLine($"  - label: {label}");
                sb.AppendLine($"    entity: {entity}");
                sb.AppendLine("    aggregate: count");
                sb.AppendLine("    icon: 📊");
                sb.AppendLine($"    color: {color}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("charts:");
        if (entityNames.Count == 0)
        {
            sb.AppendLine("  # Add charts here. Example:");
            sb.AppendLine("  # - id: trend");
            sb.AppendLine("  #   title: Monthly Trend");
            sb.AppendLine("  #   type: bar");
            sb.AppendLine("  #   source: \"SELECT strftime('%Y-%m', CreatedAt) AS Month, COUNT(*) AS Count FROM YourTable GROUP BY Month ORDER BY Month DESC LIMIT 12\"");
            sb.AppendLine("  #   xAxis: Month");
            sb.AppendLine("  #   yAxis: Count");
            sb.AppendLine("  []");
        }
        else
        {
            var firstEntity = entityNames[0];
            var firstTable = firstEntity; // will be resolved at runtime
            sb.AppendLine($"  # Auto-generated chart example based on first entity: {firstEntity}");
            sb.AppendLine($"  # Uncomment and adjust the SQL to match your actual table/columns.");
            sb.AppendLine($"  # - id: {firstEntity}_trend");
            sb.AppendLine($"  #   title: {ToDisplayName(firstEntity)} Monthly Trend");
            sb.AppendLine("  #   type: bar");
            sb.AppendLine($"  #   source: \"SELECT strftime('%Y-%m', CreatedAt) AS Month, COUNT(*) AS Count FROM {firstTable} GROUP BY Month ORDER BY Month DESC LIMIT 12\"");
            sb.AppendLine("  #   xAxis: Month");
            sb.AppendLine("  #   yAxis: Count");
            sb.AppendLine("  []");
        }

        return sb.ToString();
    }

    private static string BuildI18nYamlFromProjectFiles(string projectDir, string projectName, I18nFallbackMode fallbackMode)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        map[$"projects.{projectName}.template.welcome"] = "Starter template";

        void ExtractFrom(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var lines = File.ReadAllLines(filePath);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                foreach (Match keyRefMatch in Regex.Matches(line, @"(?<key>(projects|entities)\.[A-Za-z0-9_.-]+)"))
                {
                    var key = keyRefMatch.Groups["key"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
                    {
                        map[key] = DeriveLabelFromI18nKey(key, fallbackMode);
                    }
                }

                var inlineMatch = Regex.Match(line, @"label:\s*['""]?(?<label>[^'"",]+)['""]?.*labelKey:\s*['""]?(?<key>[^'""]+)['""]?");
                if (inlineMatch.Success)
                {
                    var key = inlineMatch.Groups["key"].Value.Trim();
                    var label = inlineMatch.Groups["label"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(label))
                    {
                        map[key] = label;
                    }
                }

                var labelMatch = Regex.Match(line, @"^\s*-?\s*label:\s*['""]?(?<label>[^'""]+)['""]?\s*$");
                if (labelMatch.Success && i + 1 < lines.Length)
                {
                    var next = lines[i + 1];
                    var keyMatch = Regex.Match(next, @"^\s*labelKey:\s*['""]?(?<key>[^'""]+)['""]?\s*$");
                    if (keyMatch.Success)
                    {
                        var key = keyMatch.Groups["key"].Value.Trim();
                        var label = labelMatch.Groups["label"].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(label))
                        {
                            map[key] = label;
                        }
                    }
                }

                var displayInline = Regex.Match(line, @"displayName:\s*['""]?(?<label>[^'""]+)['""]?.*displayNameKey:\s*['""]?(?<key>[^'""]+)['""]?");
                if (displayInline.Success)
                {
                    var key = displayInline.Groups["key"].Value.Trim();
                    var label = displayInline.Groups["label"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(label))
                    {
                        map[key] = label;
                    }
                }

                var displayNameMatch = Regex.Match(line, @"^\s*displayName:\s*['""]?(?<label>[^'""]+)['""]?\s*$");
                if (displayNameMatch.Success && i + 1 < lines.Length)
                {
                    var next = lines[i + 1];
                    var keyMatch = Regex.Match(next, @"^\s*displayNameKey:\s*['""]?(?<key>[^'""]+)['""]?\s*$");
                    if (keyMatch.Success)
                    {
                        var key = keyMatch.Groups["key"].Value.Trim();
                        var label = displayNameMatch.Groups["label"].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(label))
                        {
                            map[key] = label;
                        }
                    }
                }

                var titleMatch = Regex.Match(line, @"^\s*-?\s*title:\s*['""]?(?<title>[^'""]+)['""]?\s*$");
                if (titleMatch.Success && i + 1 < lines.Length)
                {
                    var next = lines[i + 1];
                    var keyMatch = Regex.Match(next, @"^\s*titleKey:\s*['""]?(?<key>[^'""]+)['""]?\s*$");
                    if (keyMatch.Success)
                    {
                        var key = keyMatch.Groups["key"].Value.Trim();
                        var title = titleMatch.Groups["title"].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(title))
                        {
                            map[key] = title;
                        }
                    }
                }

                var scalarMatch = Regex.Match(line, @"^\s*(?<name>[A-Za-z][A-Za-z0-9]*):\s*(?<val>[^#\{\[][^#]*?)\s*$");
                if (scalarMatch.Success)
                {
                    var keyName = scalarMatch.Groups["name"].Value.Trim();
                    if (!keyName.EndsWith("Key", StringComparison.Ordinal))
                    {
                        var scalarValue = scalarMatch.Groups["val"].Value.Trim().Trim('\'', '"');
                        if (!string.IsNullOrWhiteSpace(scalarValue))
                        {
                            var lookAhead = i + 1 < lines.Length ? lines[i + 1] : string.Empty;
                            var keyMatch = Regex.Match(lookAhead, $@"^\s*{keyName}Key:\s*['""]?(?<key>[^'""]+)['""]?\s*$");
                            if (keyMatch.Success)
                            {
                                var i18nKey = keyMatch.Groups["key"].Value.Trim();
                                if (!string.IsNullOrWhiteSpace(i18nKey))
                                {
                                    map[i18nKey] = scalarValue;
                                }
                            }
                        }
                    }
                }
            }
        }

        ExtractFrom(Path.Combine(projectDir, "dashboard.yml"));
        ExtractFrom(Path.Combine(projectDir, "config", "home-page.yml"));

        var entitiesDir = Path.Combine(projectDir, "entities");
        if (Directory.Exists(entitiesDir))
        {
            foreach (var file in Directory.GetFiles(entitiesDir, "*.yml"))
            {
                ExtractFrom(file);
            }
        }

        var generatedEntitiesDir = Path.Combine(projectDir, "entities.generated");
        if (Directory.Exists(generatedEntitiesDir))
        {
            foreach (var file in Directory.GetFiles(generatedEntitiesDir, "*.yml"))
            {
                ExtractFrom(file);
            }
        }

        var pagesDir = Path.Combine(projectDir, "pages");
        if (Directory.Exists(pagesDir))
        {
            foreach (var file in Directory.GetFiles(pagesDir, "*.yml"))
            {
                ExtractFrom(file);
            }
            foreach (var file in Directory.GetFiles(pagesDir, "*.yaml"))
            {
                ExtractFrom(file);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("translations:");
        foreach (var pair in map)
        {
            sb.AppendLine($"  {pair.Key}:");
            sb.AppendLine($"    en-US: {QuoteYaml(pair.Value)}");
            sb.AppendLine($"    zh-CN: {QuoteYaml(pair.Value)}");
            sb.AppendLine($"    ja-JP: {QuoteYaml(pair.Value)}");
        }
        return sb.ToString();
    }

    private static I18nFallbackMode? ResolveI18nFallbackMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return I18nFallbackMode.Display;
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "display" => I18nFallbackMode.Display,
            "raw" => I18nFallbackMode.Raw,
            _ => null
        };
    }

    private static string DeriveLabelFromI18nKey(string key, I18nFallbackMode fallbackMode)
    {
        var segments = key
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (segments.Count == 0)
        {
            return key;
        }

        var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "projects", "project", "home", "dashboard", "pages", "entities", "entity",
            "sections", "stats", "charts", "columns", "forms", "quickActions", "hero"
        };

        var selected = segments[^1];
        if (selected.All(char.IsDigit) && segments.Count >= 2)
        {
            selected = segments[^2];
        }

        if ((selected.Equals("label", StringComparison.OrdinalIgnoreCase) ||
             selected.Equals("title", StringComparison.OrdinalIgnoreCase) ||
             selected.Equals("description", StringComparison.OrdinalIgnoreCase)) &&
            segments.Count >= 2)
        {
            var prev = segments[^2];
            if (!prev.All(char.IsDigit) && !generic.Contains(prev))
            {
                selected = prev;
            }
        }

        if (fallbackMode == I18nFallbackMode.Raw)
        {
            return selected;
        }

        return ToDisplayName(selected.Replace("-", "_", StringComparison.Ordinal));
    }

    private static string QuoteYaml(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static void WriteStarterPageAndView(string projectDir, string projectName, IReadOnlyList<string> entityNames)
    {
        var primaryEntity = entityNames.FirstOrDefault();
        var hasPrimaryEntity = !string.IsNullOrWhiteSpace(primaryEntity);
        var primaryTable = hasPrimaryEntity
            ? ResolveEntityTable(projectDir, primaryEntity!)
            : string.Empty;

        var pageYamlBuilder = new StringBuilder();
        pageYamlBuilder.AppendLine($$"""
title: Starter Overview
titleKey: projects.{{projectName}}.pages.starterOverview.title
description: Auto-generated starter page
descriptionKey: projects.{{projectName}}.pages.starterOverview.description
""");
        if (hasPrimaryEntity)
        {
            pageYamlBuilder.AppendLine($"main_table: {primaryTable}");
        }
        pageYamlBuilder.AppendLine();
        pageYamlBuilder.AppendLine("""

ui:
  page:
    layout: single
    density: comfortable

sections:
""");
        if (hasPrimaryEntity)
        {
            pageYamlBuilder.AppendLine($$"""
  - id: {{primaryEntity}}_list
    title: {{ToDisplayName(primaryEntity!)}} List
    titleKey: projects.{{projectName}}.pages.starterOverview.sections.0.title
    source_type: table
    source: {{primaryTable}}
    page_size: 20
    editable: false
    read_only: true
    ui:
      component: DataTable
      selectable: none
""");
        }
        else
        {
            pageYamlBuilder.AppendLine($$"""
  - id: starter_sample
    title: Starter Sample
    titleKey: projects.{{projectName}}.pages.starterOverview.sections.0.title
    source_type: custom
    source: SELECT 1 AS SampleValue
    columns: [SampleValue]
    page_size: 20
    editable: false
    read_only: true
    ui:
      component: DataTable
      selectable: none
""");
        }

        WriteFile(Path.Combine(projectDir, "pages", "StarterOverview.yaml"), pageYamlBuilder.ToString() + Environment.NewLine);

        var viewCshtml = $$"""
@model Dictionary<string, (IEnumerable<Dictionary<string, object>> Rows, int Total)>
@{
    var title = ViewData["Title"]?.ToString() ?? "Starter Overview";
    var currentProject = Context.GetRouteValue("project")?.ToString() ?? "{{projectName}}";
}

<div class="space-y-6">
    <!-- Page header -->
    <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div>
            <h1 class="text-2xl font-bold">@title</h1>
            <p class="text-sm opacity-60 mt-1">
                Auto-generated overview — edit
                <code class="font-mono bg-base-200 px-1 rounded">pages/StarterOverview.yaml</code>
                or
                <code class="font-mono bg-base-200 px-1 rounded">entities/*.yml</code>
                to customize.
            </p>
        </div>
        <div class="flex gap-2 flex-wrap">
            <a class="btn btn-sm btn-outline"
               asp-controller="Dashboard" asp-action="Index" asp-route-project="@currentProject">
                📊 Dashboard
            </a>
            <a class="btn btn-sm btn-ghost"
               asp-controller="DynamicEntity" asp-action="AllDefinitions" asp-route-project="@currentProject">
                🧩 Schema
            </a>
        </div>
    </div>

    <!-- Summary stats -->
    @if (Model.Any())
    {
        <div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
            @foreach (var section in Model)
            {
                <div class="stat bg-base-100 rounded-box border border-base-300 shadow-sm p-4">
                    <div class="stat-title text-xs truncate">@section.Key</div>
                    <div class="stat-value text-2xl">@section.Value.Total</div>
                    <div class="stat-desc">records</div>
                </div>
            }
        </div>
    }

    <!-- Data sections -->
    @if (!Model.Any())
    {
        <div class="hero bg-base-100 rounded-box border border-base-300 min-h-48">
            <div class="hero-content text-center">
                <div>
                    <p class="text-4xl mb-3">🧭</p>
                    <h2 class="text-xl font-semibold">No sections yet</h2>
                    <p class="opacity-60 mt-1 text-sm">
                        Run <code class="font-mono bg-base-200 px-1 rounded">--scaffold-entities</code> to auto-generate entity definitions,
                        then edit <code class="font-mono bg-base-200 px-1 rounded">pages/StarterOverview.yaml</code>.
                    </p>
                </div>
            </div>
        </div>
    }
    else
    {
        @foreach (var section in Model)
        {
            <div class="card bg-base-100 border border-base-300 shadow-sm">
                <div class="card-body p-4">
                    <div class="flex items-center justify-between mb-2">
                        <h2 class="card-title text-base">@section.Key</h2>
                        <span class="badge badge-ghost badge-sm">@section.Value.Total total</span>
                    </div>
                    <div class="overflow-x-auto">
                        <table class="table table-zebra table-sm w-full">
                            <thead>
                                <tr class="bg-base-200">
                                    @if (section.Value.Rows.Any())
                                    {
                                        @foreach (var col in section.Value.Rows.First().Keys)
                                        {
                                            <th class="text-xs font-semibold uppercase tracking-wide">@col</th>
                                        }
                                    }
                                </tr>
                            </thead>
                            <tbody>
                                @if (!section.Value.Rows.Any())
                                {
                                    <tr><td colspan="99" class="text-center opacity-50 py-6">No records</td></tr>
                                }
                                else
                                {
                                    @foreach (var row in section.Value.Rows)
                                    {
                                        <tr class="hover">
                                            @foreach (var val in row.Values)
                                            {
                                                <td class="text-sm">@(val?.ToString() ?? "—")</td>
                                            }
                                        </tr>
                                    }
                                }
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>
        }
    }
</div>
""";
        WriteFile(Path.Combine(projectDir, "views", "StarterOverview.cshtml"), viewCshtml + Environment.NewLine);
    }

    private static string BuildViewImportsCshtml()
    {
        return """
@using NetYamlForge
@using NetYamlForge.Models
@using NetYamlForge.Controllers
#nullable disable
@using Microsoft.AspNetCore.Mvc
@using Microsoft.AspNetCore.Mvc.Rendering
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
""" + Environment.NewLine;
    }

    private static string BuildViewStartCshtml()
    {
        return """
@{
    Layout = "_Layout";
}
""" + Environment.NewLine;
    }

    private static string BuildProjectLayoutCshtml(string projectName, string displayName)
    {
        return $$"""
@using NetYamlForge.Localization
@using Microsoft.Extensions.Localization
@inject IStringLocalizer<SharedResource> L
<!DOCTYPE html>
<html lang="@System.Globalization.CultureInfo.CurrentUICulture.Name" data-theme="corporate">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@(ViewData["Title"] != null ? ViewData["Title"] + " — " : ""){{displayName}}</title>
    <link href="https://cdn.jsdelivr.net/npm/daisyui@5" rel="stylesheet" type="text/css" />
    <script src="https://cdn.jsdelivr.net/npm/@@tailwindcss/browser@4"></script>
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/NetYamlForge.styles.css" asp-append-version="true" />
</head>
<body class="min-h-screen bg-base-200 flex flex-col">
    @{
        var currentProject = Context.GetRouteValue("project")?.ToString() ?? "{{projectName}}";
        var currentController = ViewContext.RouteData.Values["controller"]?.ToString() ?? "";
        var currentAction = ViewContext.RouteData.Values["action"]?.ToString() ?? "";
    }

    <!-- ===== Navbar ===== -->
    <header class="navbar bg-base-100 border-b border-base-300 px-4 shadow-sm sticky top-0 z-50">
        <div class="navbar-start gap-2">
            <!-- Mobile hamburger -->
            <div class="dropdown lg:hidden">
                <label tabindex="0" class="btn btn-ghost btn-sm">
                    <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
                    </svg>
                </label>
                <ul tabindex="0" class="menu menu-sm dropdown-content mt-3 z-[1] p-2 shadow bg-base-100 rounded-box w-52">
                    <li><a asp-controller="Home" asp-action="Project" asp-route-project="@currentProject">🏠 Home</a></li>
                    <li><a asp-controller="Dashboard" asp-action="Index" asp-route-project="@currentProject">📊 Dashboard</a></li>
                    <li><a asp-controller="Page" asp-action="Index" asp-route-project="@currentProject" asp-route-pageName="StarterOverview">🧭 Overview</a></li>
                    <li><a asp-controller="DynamicEntity" asp-action="AllDefinitions" asp-route-project="@currentProject">🧩 Schema</a></li>
                </ul>
            </div>
            <!-- Brand -->
            <a class="btn btn-ghost text-lg font-bold tracking-tight"
               asp-controller="Home"
               asp-action="Project"
               asp-route-project="@currentProject">
                {{displayName}}
            </a>
        </div>

        <div class="navbar-center hidden lg:flex">
            <ul class="menu menu-horizontal px-1 gap-1">
                <li>
                    <a asp-controller="Home" asp-action="Project" asp-route-project="@currentProject"
                       class="@(currentController == "Home" ? "active" : "")">
                        🏠 Home
                    </a>
                </li>
                <li>
                    <a asp-controller="Dashboard" asp-action="Index" asp-route-project="@currentProject"
                       class="@(currentController == "Dashboard" ? "active" : "")">
                        📊 Dashboard
                    </a>
                </li>
                <li>
                    <a asp-controller="Page" asp-action="Index" asp-route-project="@currentProject" asp-route-pageName="StarterOverview"
                       class="@(currentController == "Page" ? "active" : "")">
                        🧭 Overview
                    </a>
                </li>
                <li>
                    <a asp-controller="DynamicEntity" asp-action="AllDefinitions" asp-route-project="@currentProject"
                       class="@(currentController == "DynamicEntity" ? "active" : "")">
                        🧩 Schema
                    </a>
                </li>
            </ul>
        </div>

        <div class="navbar-end gap-2">
            <!-- Language switcher -->
            <div class="dropdown dropdown-end">
                <label tabindex="0" class="btn btn-ghost btn-sm btn-circle" title="Language">
                    🌐
                </label>
                <ul tabindex="0" class="dropdown-content menu p-2 shadow bg-base-100 rounded-box w-32 z-[1]">
                    <li><a href="?lang=en-US">English</a></li>
                    <li><a href="?lang=zh-CN">中文</a></li>
                    <li><a href="?lang=ja-JP">日本語</a></li>
                    <li><a href="?lang=ko-KR">한국어</a></li>
                </ul>
            </div>
            <!-- Theme toggle -->
            <label class="swap swap-rotate btn btn-ghost btn-sm btn-circle" title="Toggle theme">
                <input type="checkbox" id="theme-toggle" />
                <svg class="swap-off h-5 w-5" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M5.64,17l-.71.71a1,1,0,0,0,0,1.41,1,1,0,0,0,1.41,0l.71-.71A1,1,0,0,0,5.64,17ZM5,12a1,1,0,0,0-1-1H3a1,1,0,0,0,0,2H4A1,1,0,0,0,5,12Zm7-7a1,1,0,0,0,1-1V3a1,1,0,0,0-2,0V4A1,1,0,0,0,12,5ZM5.64,7.05a1,1,0,0,0,.7.29,1,1,0,0,0,.71-.29,1,1,0,0,0,0-1.41l-.71-.71A1,1,0,0,0,4.93,6.34Zm12,.29a1,1,0,0,0,.7-.29l.71-.71a1,1,0,1,0-1.41-1.41L17,5.64a1,1,0,0,0,0,1.41A1,1,0,0,0,17.66,7.34ZM21,11H20a1,1,0,0,0,0,2h1a1,1,0,0,0,0-2Zm-9,8a1,1,0,0,0-1,1v1a1,1,0,0,0,2,0V20A1,1,0,0,0,12,19ZM18.36,17A1,1,0,0,0,17,18.36l.71.71a1,1,0,0,0,1.41,0,1,1,0,0,0,0-1.41ZM12,6.5A5.5,5.5,0,1,0,17.5,12,5.51,5.51,0,0,0,12,6.5Zm0,9A3.5,3.5,0,1,1,15.5,12,3.5,3.5,0,0,1,12,15.5Z"/>
                </svg>
                <svg class="swap-on h-5 w-5" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M21.64,13a1,1,0,0,0-1.05-.14,8.05,8.05,0,0,1-3.37.73A8.15,8.15,0,0,1,9.08,5.49a8.59,8.59,0,0,1,.25-2A1,1,0,0,0,8,2.36,10.14,10.14,0,1,0,22,14.05,1,1,0,0,0,21.64,13Zm-9.5,6.69A8.14,8.14,0,0,1,7.08,5.22v.27A10.15,10.15,0,0,0,17.22,15.63a9.79,9.79,0,0,0,2.1-.22A8.11,8.11,0,0,1,12.14,19.73Z"/>
                </svg>
            </label>
        </div>
    </header>

    <!-- ===== Breadcrumbs ===== -->
    @if (ViewData["Breadcrumbs"] is IEnumerable<(string Label, string? Url)> crumbs)
    {
        <div class="px-4 py-2 bg-base-100 border-b border-base-200">
            <div class="breadcrumbs text-sm max-w-screen-xl mx-auto">
                <ul>
                    <li><a asp-controller="Home" asp-action="Project" asp-route-project="@currentProject">{{displayName}}</a></li>
                    @foreach (var (label, url) in crumbs)
                    {
                        if (url != null)
                        {
                            <li><a href="@url">@label</a></li>
                        }
                        else
                        {
                            <li>@label</li>
                        }
                    }
                </ul>
            </div>
        </div>
    }

    <!-- ===== Main content ===== -->
    <main class="flex-1 p-4 lg:p-6 max-w-screen-xl mx-auto w-full">
        @RenderBody()
    </main>

    <!-- ===== Footer ===== -->
    <footer class="footer footer-center p-4 bg-base-100 border-t border-base-300 text-base-content text-xs opacity-60">
        <div>
            <p>{{displayName}} — powered by <a href="https://github.com/yourorg/NetYamlForge" class="link link-hover">NetYamlForge</a></p>
        </div>
    </footer>

    <script src="https://unpkg.com/htmx.org@1.9.12"></script>
    <script src="~/js/site.js" asp-append-version="true"></script>
    <script>
        // Theme toggle persistence
        const toggle = document.getElementById('theme-toggle');
        const html = document.documentElement;
        const saved = localStorage.getItem('nyfTheme');
        if (saved === 'dark') { html.setAttribute('data-theme', 'dark'); toggle.checked = true; }
        toggle?.addEventListener('change', () => {
            const theme = toggle.checked ? 'dark' : 'corporate';
            html.setAttribute('data-theme', theme);
            localStorage.setItem('nyfTheme', toggle.checked ? 'dark' : 'light');
        });
    </script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
""" + Environment.NewLine;
    }

    private static string ResolveEntityTable(string projectDir, string entityName)
    {
        var entityFile = Path.Combine(projectDir, "entities", $"{entityName}.yml");
        if (!File.Exists(entityFile))
        {
            return entityName;
        }

        foreach (var line in File.ReadLines(entityFile))
        {
            var m = Regex.Match(line, @"^\s*table:\s*['""]?(?<table>[^'""]+)['""]?\s*$");
            if (m.Success)
            {
                var table = m.Groups["table"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(table))
                {
                    return table;
                }
            }
        }

        return entityName;
    }

    private static string BuildReadme(string projectName, string displayName)
    {
        return $$"""
# {{displayName}}

> `--init-project` で自動生成されたサブプロジェクトです。

## ディレクトリ構成

```
{{projectName}}/
├── project.yaml          # プロジェクト設定（DB接続・機能フラグ）
├── dashboard.yml         # ダッシュボード（統計カード・チャート）
├── config/
│   ├── home-page.yml     # ホームページのヒーロー・クイックアクション
│   ├── layout.yml        # ナビゲーション・ヘッダー・フッター
│   └── i18n.yml          # 翻訳キー（en-US / zh-CN / ja-JP / ko-KR）
├── entities/             # ★ エンティティ定義（手動編集 or scaffold 生成）
├── entities.generated/   # scaffold 自動生成（上書き可）
├── pages/                # カスタムページ定義（YAML）
├── database/             # SQLite DB ファイル
├── views/                # Razor カスタムビュー（.cshtml）
└── docs/
    └── README-ja.md      # このファイル
```

## クイックスタート

```bash
# 1. DB からエンティティ YAML を自動生成
dotnet run -- --scaffold-entities --project={{projectName}}

# 2. ブラウザで確認
# → http://localhost:5000/{{projectName}}
# → http://localhost:5000/{{projectName}}/Dashboard
```

## エンティティ定義の書き方

`entities/my_entity.yml` の最小構成:

```yaml
entities:
  my_entity:
    table: MyTable
    key: Id
    displayName: My Entity
    columns:
      Id:   { type: int, identity: true, label: ID, sortable: true }
      Name: { type: string, required: true, label: Name, searchable: true }
    forms:
      Name: { type: string, required: true, label: Name, editable: true }
    filters:
      Name: { type: like, label: Name }
```

## カスタマイズ例

### ナビに項目を追加（`config/layout.yml`）

```yaml
navigation:
  items:
    - label: My Page
      url: /{{projectName}}/Page/MyPage
      icon: 📋
```

### ダッシュボードにチャートを追加（`dashboard.yml`）

```yaml
charts:
  - id: monthly
    title: Monthly Trend
    type: bar
    source: "SELECT strftime('%Y-%m', CreatedAt) AS Month, COUNT(*) AS Count FROM MyTable GROUP BY Month ORDER BY Month DESC LIMIT 12"
    xAxis: Month
    yAxis: Count
```

### i18n キーを追加（`config/i18n.yml`）

```yaml
translations:
  my.custom.label:
    en-US: My Label
    zh-CN: 我的标签
    ja-JP: 私のラベル
    ko-KR: 내 레이블
```

## トラブルシューティング

| 症状 | 対処 |
|------|------|
| entities/ が空 | `--scaffold-entities` を実行 |
| YAML 構文エラー | `dotnet test` でバリデーション確認 |
| 画面が反映されない | サーバー再起動 (`dotnet run`) |
| フックを追加したい | `dotnet run -- --scaffold-hook --name=MyHook --project={{projectName}}` |

詳細ドキュメント: `docs/developer-tutorial-ja.md` / `docs/COMMON_HOOKS.md`
""" + Environment.NewLine;
    }
}
