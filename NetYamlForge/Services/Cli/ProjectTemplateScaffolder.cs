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
    sourceType: table
    source: {{primaryTable}}
    pageSize: 20
    editable: false
    readOnly: true
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
    sourceType: custom
    source: SELECT 1 AS SampleValue
    columns: [SampleValue]
    pageSize: 20
    editable: false
    readOnly: true
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
            @foreach (var grp in Model)
            {
                <div class="stat bg-base-100 rounded-box border border-base-300 shadow-sm p-4">
                    <div class="stat-title text-xs truncate">@grp.Key</div>
                    <div class="stat-value text-2xl">@grp.Value.Total</div>
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
        @foreach (var grp in Model)
        {
            <div class="card bg-base-100 border border-base-300 shadow-sm">
                <div class="card-body p-4">
                    <div class="flex items-center justify-between mb-2">
                        <h2 class="card-title text-base">@grp.Key</h2>
                        <span class="badge badge-ghost badge-sm">@grp.Value.Total total</span>
                    </div>
                    <div class="overflow-x-auto">
                        <table class="table table-zebra table-sm w-full">
                            <thead>
                                <tr class="bg-base-200">
                                    @if (grp.Value.Rows.Any())
                                    {
                                        @foreach (var col in grp.Value.Rows.First().Keys)
                                        {
                                            <th class="text-xs font-semibold uppercase tracking-wide">@col</th>
                                        }
                                    }
                                </tr>
                            </thead>
                            <tbody>
                                @if (!grp.Value.Rows.Any())
                                {
                                    <tr><td colspan="99" class="text-center opacity-50 py-6">No records</td></tr>
                                }
                                else
                                {
                                    @foreach (var row in grp.Value.Rows)
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
@using NetYamlForge.Models
@using Microsoft.Extensions.Localization
@inject IStringLocalizer<SharedResource> L
@inject NetYamlForge.Services.IEntityMetadataProvider EntityMetadataProvider
@inject NetYamlForge.Services.ProjectScope ProjectScope
<!DOCTYPE html>
<html lang="@System.Globalization.CultureInfo.CurrentUICulture.Name" data-theme="corporate">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@(ViewData["Title"] != null ? ViewData["Title"] + " — " : ""){{displayName}}</title>
    <script>
        window.NetYamlForgeConfig = {
            pathBase: '@Url.Content("~/").TrimEnd('/')',
            currentProject: '@Context.GetRouteValue("project")?.ToString()'
        };
    </script>
    <link href="@Url.Content("~/lib/daisyui/daisyui.min.css")" rel="stylesheet" type="text/css" />
    <script src="@Url.Content("~/lib/tailwindcss/browser.min.js")"></script>
    <link rel="stylesheet" href="@Url.Content("~/css/site.css")" asp-append-version="true" />
    <link rel="stylesheet" href="@Url.Content("~/NetYamlForge.styles.css")" asp-append-version="true" />
    <link rel="stylesheet" href="@Url.Content("~/css/ai-assistant.css")" asp-append-version="true" />
    @await RenderSectionAsync("Styles", required: false)
</head>
@functions {
    private sealed class NavSection
    {
        public string? Title { get; }
        public List<ProjectNavigationItemConfig> Items { get; } = new();
        public NavSection(string? title) => Title = title;
    }
}
<body class="min-h-screen bg-base-200" data-user-authenticated="@(User.Identity?.IsAuthenticated ?? false ? "true" : "false")">
    @{
        var showSidebar = User.Identity?.IsAuthenticated ?? false;
        var isAdminUser = User?.IsInRole("Admin") ?? false;
        var projectLayout = ProjectScope?.Current?.Layout;
        var navConfig = projectLayout?.Navigation;
        var headerConfig = projectLayout?.Header;
        var footerConfig = projectLayout?.Footer;
        var allEntities = EntityMetadataProvider.GetAll()
            .Where(pair => isAdminUser || pair.Value.IsPublic)
            .OrderBy(pair => pair.Value.GetDisplayName(), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var entityDefinitions = navConfig != null && navConfig.Entities.Count > 0
            ? allEntities.Where(pair => navConfig.Entities.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(pair => navConfig.Entities.IndexOf(pair.Key))
                .ToList()
            : allEntities;
        var currentProject = Context.GetRouteValue("project")?.ToString() ?? "{{projectName}}";
        var headerTitle = headerConfig?.Title ?? "{{displayName}}";
    }
    <div class="drawer">
        <input id="app-sidebar" type="checkbox" class="drawer-toggle" />

        <div class="drawer-content flex flex-col min-h-screen">
            <!-- ===== Navbar ===== -->
            <header class="navbar bg-base-100 border-b border-base-300 px-3 lg:px-6 sticky top-0 z-30">
                @if (showSidebar)
                {
                    <div class="flex-none">
                        <label for="app-sidebar" class="btn btn-ghost btn-square">
                            <svg xmlns="http://www.w3.org/2000/svg" class="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
                            </svg>
                        </label>
                    </div>
                }
                <div class="flex-1">
                    <a class="btn btn-ghost text-xl font-bold tracking-tight"
                       asp-controller="Home"
                       asp-action="Project"
                       asp-route-project="@currentProject">@headerTitle</a>
                </div>
                <div class="flex-none gap-2">
                    <div class="dropdown dropdown-end">
                        <div tabindex="0" role="button" class="btn btn-ghost btn-circle avatar">
                            <div class="w-10 rounded-full bg-base-300 text-base-content grid place-items-center font-semibold">
                                @if (User.Identity?.IsAuthenticated ?? false)
                                {
                                    @((User.Identity?.Name ?? "U").Substring(0, 1).ToUpperInvariant())
                                }
                                else
                                {
                                    @("G")
                                }
                            </div>
                        </div>
                        <ul tabindex="0" class="menu menu-sm dropdown-content bg-base-100 rounded-box z-[1] mt-3 w-64 p-2 shadow">
                            @if (User.Identity?.IsAuthenticated ?? false)
                            {
                                <li class="menu-title">
                                    <span>@L["Hello"], @(User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? User.Identity?.Name)</span>
                                </li>
                            }
                            <li class="menu-title"><span>Language</span></li>
                            <li>
                                <div class="flex gap-2 px-2 py-1">
                                    <form asp-controller="Localization" asp-action="SetLanguage" method="post">
                                        <input type="hidden" name="returnUrl" value="@Context.Request.Path@Context.Request.QueryString" />
                                        <input type="hidden" name="culture" value="en-US" />
                                        <button type="submit" class="btn btn-ghost btn-sm btn-circle @(System.Globalization.CultureInfo.CurrentUICulture.Name == "en-US" ? "btn-active" : "")" title="English">🇺🇸</button>
                                    </form>
                                    <form asp-controller="Localization" asp-action="SetLanguage" method="post">
                                        <input type="hidden" name="returnUrl" value="@Context.Request.Path@Context.Request.QueryString" />
                                        <input type="hidden" name="culture" value="zh-CN" />
                                        <button type="submit" class="btn btn-ghost btn-sm btn-circle @(System.Globalization.CultureInfo.CurrentUICulture.Name == "zh-CN" ? "btn-active" : "")" title="中文">🇨🇳</button>
                                    </form>
                                    <form asp-controller="Localization" asp-action="SetLanguage" method="post">
                                        <input type="hidden" name="returnUrl" value="@Context.Request.Path@Context.Request.QueryString" />
                                        <input type="hidden" name="culture" value="ja-JP" />
                                        <button type="submit" class="btn btn-ghost btn-sm btn-circle @(System.Globalization.CultureInfo.CurrentUICulture.Name == "ja-JP" ? "btn-active" : "")" title="日本語">🇯🇵</button>
                                    </form>
                                    <form asp-controller="Localization" asp-action="SetLanguage" method="post">
                                        <input type="hidden" name="returnUrl" value="@Context.Request.Path@Context.Request.QueryString" />
                                        <input type="hidden" name="culture" value="ko-KR" />
                                        <button type="submit" class="btn btn-ghost btn-sm btn-circle @(System.Globalization.CultureInfo.CurrentUICulture.Name == "ko-KR" ? "btn-active" : "")" title="한국어">🇰🇷</button>
                                    </form>
                                </div>
                            </li>
                            @if (User.Identity?.IsAuthenticated ?? false)
                            {
                                @if (User.IsInRole("Admin"))
                                {
                                    <li><a asp-controller="Users" asp-action="Index" asp-route-project="@currentProject">@L["Users"]</a></li>
                                }
                                <li>
                                    <form asp-controller="Account" asp-action="Logout" asp-route-project="@currentProject" method="post">
                                        <button type="submit" class="text-left w-full">@L["Logout"]</button>
                                    </form>
                                </li>
                            }
                            else
                            {
                                <li><a asp-controller="Account" asp-action="Login" asp-route-project="@currentProject">@L["Login"]</a></li>
                            }
                        </ul>
                    </div>
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
            <main class="p-4 lg:p-6">
                @RenderBody()
            </main>
        </div>

        <!-- ===== Left Sidebar ===== -->
        @if (showSidebar)
        {
            <div class="drawer-side z-40">
                <label for="app-sidebar" aria-label="close sidebar" class="drawer-overlay"></label>
                <aside class="bg-base-100 w-72 min-h-full border-r border-base-300">
                    <div class="p-4 border-b border-base-300 flex items-center justify-between">
                        <div>
                            <div class="text-sm opacity-70">Navigation</div>
                            <div class="font-semibold">{{displayName}}</div>
                        </div>
                        <label for="app-sidebar" class="btn btn-ghost btn-sm btn-square" aria-label="close menu">
                            <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </label>
                    </div>
                    <ul class="menu p-3 gap-1 w-full">
                        @{
                            var isDashboard = string.Equals(
                                Context.Request.RouteValues["controller"]?.ToString(),
                                "Dashboard",
                                StringComparison.OrdinalIgnoreCase);
                            var showDashboard = navConfig?.ShowDashboard ?? true;
                            var navSections = new List<NavSection>();
                            if (navConfig?.Items != null && navConfig.Items.Count > 0)
                            {
                                foreach (var item in navConfig.Items)
                                {
                                    var showItem = !item.AdminOnly || isAdminUser;
                                    if (!showItem) continue;
                                    var sectionName = string.IsNullOrWhiteSpace(item.Section) ? null : item.Section!.Trim();
                                    var section = navSections.FirstOrDefault(s => string.Equals(s.Title, sectionName, StringComparison.Ordinal));
                                    if (section == null)
                                    {
                                        section = new NavSection(sectionName);
                                        navSections.Add(section);
                                    }
                                    section.Items.Add(item);
                                }
                            }
                        }
                        @if (showDashboard)
                        {
                            <li>
                                <a class="@(isDashboard ? "active" : "")"
                                   asp-controller="Dashboard"
                                   asp-action="Index"
                                   asp-route-project="@currentProject">📊 Dashboard</a>
                            </li>
                        }
                        @if (navSections.Count > 0)
                        {
                            foreach (var section in navSections)
                            {
                                if (!string.IsNullOrEmpty(section.Title))
                                {
                                    <li class="menu-title mt-1"><span>@(section.Title)</span></li>
                                }
                                foreach (var item in section.Items)
                                {
                                    var itemLabel = I18nText.Resolve(item.LabelI18n, item.Label, item.LabelKey);
                                    @if (!string.IsNullOrEmpty(item.Url))
                                    {
                                        <li><a href="@item.Url">@itemLabel</a></li>
                                    }
                                    else if (!string.IsNullOrEmpty(item.Controller))
                                    {
                                        <li>
                                            <a asp-controller="@item.Controller"
                                               asp-action="@(item.Action ?? "Index")"
                                               asp-route-project="@currentProject">@itemLabel</a>
                                        </li>
                                    }
                                }
                            }
                        }
                        @if (entityDefinitions.Count > 0)
                        {
                            <li class="menu-title mt-1"><span>Entities</span></li>
                            @foreach (var definition in entityDefinitions)
                            {
                                var isActive = string.Equals(
                                    Context.Request.RouteValues["controller"]?.ToString(),
                                    "DynamicEntity",
                                    StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(
                                        Context.Request.Query["entity"],
                                        definition.Key,
                                        StringComparison.OrdinalIgnoreCase);
                                var isDefinitionPage = isActive &&
                                    string.Equals(
                                        Context.Request.RouteValues["action"]?.ToString(),
                                        "Definition",
                                        StringComparison.OrdinalIgnoreCase);
                                @if (isAdminUser)
                                {
                                    <li>
                                        <details @(isActive ? "open" : "")>
                                            <summary class="@(isActive && !isDefinitionPage ? "active" : "")">
                                                @definition.Value.GetDisplayName()
                                                <span class="badge badge-sm @(definition.Value.IsPublic ? "badge-success" : "badge-ghost") ml-auto">
                                                    @(definition.Value.IsPublic ? "Pub" : "Priv")
                                                </span>
                                            </summary>
                                            <ul>
                                                <li>
                                                    <a class="@(isActive && !isDefinitionPage ? "active" : "")"
                                                       asp-controller="DynamicEntity"
                                                       asp-action="Index"
                                                       asp-route-project="@currentProject"
                                                       asp-route-entity="@definition.Key">List</a>
                                                </li>
                                                <li>
                                                    <a class="@(isDefinitionPage ? "active" : "")"
                                                       asp-controller="DynamicEntity"
                                                       asp-action="Definition"
                                                       asp-route-project="@currentProject"
                                                       asp-route-entity="@definition.Key">Definition</a>
                                                </li>
                                            </ul>
                                        </details>
                                    </li>
                                }
                                else
                                {
                                    <li>
                                        <a class="@(isActive ? "active" : "")"
                                           asp-controller="DynamicEntity"
                                           asp-action="Index"
                                           asp-route-project="@currentProject"
                                           asp-route-entity="@definition.Key">@definition.Value.GetDisplayName()</a>
                                    </li>
                                }
                            }
                        }
                        @if (isAdminUser)
                        {
                            <li class="menu-title mt-2"><span>Admin</span></li>
                            <li>
                                <a asp-controller="BatchJob"
                                   asp-action="Index"
                                   asp-route-project="@currentProject"
                                   class="@(string.Equals(Context.Request.RouteValues["controller"]?.ToString(), "BatchJob", StringComparison.OrdinalIgnoreCase) ? "active" : "")">
                                    Batch Jobs
                                </a>
                            </li>
                            <li>
                                <a asp-controller="DynamicEntity"
                                   asp-action="AllDefinitions"
                                   asp-route-project="@currentProject"
                                   class="@(string.Equals(Context.Request.RouteValues["action"]?.ToString(), "AllDefinitions", StringComparison.OrdinalIgnoreCase) ? "active" : "")">
                                    Schema
                                </a>
                            </li>
                            <li><a asp-controller="Users" asp-action="Index" asp-route-project="@currentProject">@L["Users"]</a></li>
                        }
                    </ul>
                </aside>
            </div>
        }
    </div>

    <!-- ===== CRUD 確認ダイアログ ===== -->
    <dialog id="crud-confirm-modal" class="modal">
        <div class="modal-box max-w-sm">
            <h3 class="font-bold text-lg mb-4" id="crud-confirm-msg">確認</h3>
            <div class="flex gap-3 justify-end">
                <button id="crud-confirm-ok" class="btn btn-primary btn-sm">OK</button>
                <button id="crud-confirm-cancel" class="btn btn-ghost btn-sm">キャンセル</button>
            </div>
        </div>
        <form method="dialog" class="modal-backdrop"><button>close</button></form>
    </dialog>

    <!-- ===== Entity Picker モーダル ===== -->
    <dialog id="entity-picker-modal" class="modal">
        <div class="modal-box max-w-3xl">
            <div class="flex items-center justify-between mb-3">
                <h3 id="entity-picker-title" class="font-bold text-lg">Select</h3>
                <form method="dialog"><button class="btn btn-ghost btn-sm btn-circle">✕</button></form>
            </div>
            <div class="mb-3">
                <input type="text" id="entity-picker-search" class="input input-bordered w-full"
                       placeholder="Search..." oninput="entityPickerSearch(this.value)" />
            </div>
            <div id="entity-picker-content" class="min-h-32"></div>
            <div id="entity-picker-multi-footer" class="mt-3 hidden">
                <form method="dialog"><button class="btn btn-primary btn-sm">Done</button></form>
            </div>
        </div>
        <form method="dialog" class="modal-backdrop"><button>close</button></form>
    </dialog>

    <script src="@Url.Content("~/lib/htmx/htmx.min.js")"></script>
    <script src="@Url.Content("~/js/site.js")" asp-append-version="true"></script>
    <script>
        // CRUD確認ダイアログ
        var _confirmCallback = null;
        function showConfirmDialog(msg, onOk) {
            _confirmCallback = onOk;
            var msgEl = document.getElementById('crud-confirm-msg');
            if (msgEl) msgEl.textContent = msg;
            document.getElementById('crud-confirm-modal')?.showModal();
        }
        document.addEventListener('DOMContentLoaded', function () {
            document.getElementById('crud-confirm-ok')?.addEventListener('click', function () {
                document.getElementById('crud-confirm-modal').close();
                if (_confirmCallback) { var cb = _confirmCallback; _confirmCallback = null; cb(); }
            });
            document.getElementById('crud-confirm-cancel')?.addEventListener('click', function () {
                document.getElementById('crud-confirm-modal').close();
                _confirmCallback = null;
            });
        });
        document.body.addEventListener('htmx:confirm', function (evt) {
            var msg = evt.detail.question;
            if (!msg) { evt.preventDefault(); evt.detail.issueRequest(true); return; }
            evt.preventDefault();
            showConfirmDialog(msg, function () { evt.detail.issueRequest(true); });
        });
        document.addEventListener('submit', function (evt) {
            var form = evt.target;
            var msg = form && form.dataset ? form.dataset.confirmMsg : '';
            if (!msg || msg.length === 0) return;
            if (form.dataset.skipConfirm === '1') { delete form.dataset.skipConfirm; return; }
            evt.preventDefault();
            showConfirmDialog(msg, function () {
                form.dataset.skipConfirm = '1';
                if (typeof form.requestSubmit === 'function') { form.requestSubmit(); } else { form.submit(); }
            });
        }, true);

        // Entity Picker
        var _pickerConfig = null;
        var _pickerSearchTimer = null;
        var _pickerBaseUrl = '@@Url.Content("~/" + currentProject + "/DynamicEntity/PickerList")';
        function openEntityPicker(btn) {

            _pickerConfig = { fieldName: btn.dataset.pickerField, entity: btn.dataset.pickerEntity,
                displayCol: btn.dataset.pickerDisplayCol || 'Id', query: btn.dataset.pickerQuery || '',
                multi: btn.dataset.pickerMulti === 'true', sourceButton: btn };
            var title = document.getElementById('entity-picker-title');
            if (title) title.textContent = 'Select ' + _pickerConfig.entity;
            var footer = document.getElementById('entity-picker-multi-footer');
            if (footer) footer.classList.toggle('hidden', !_pickerConfig.multi);
            var searchEl = document.getElementById('entity-picker-search');
            if (searchEl) searchEl.value = '';
            loadPickerContent('', 1);
            document.getElementById('entity-picker-modal').showModal();
        }
        function loadPickerContent(search, page) {
            if (!_pickerConfig) return;
            var cfg = _pickerConfig;
            var url = _pickerBaseUrl + '?entity=' + encodeURIComponent(cfg.entity)
                + '&targetField=' + encodeURIComponent(cfg.fieldName)
                + '&displayColumn=' + encodeURIComponent(cfg.displayCol)
                + '&displayColumns=' + encodeURIComponent(cfg.displayCol)
                + '&query=' + encodeURIComponent(cfg.query || '')
                + '&multi=' + (cfg.multi ? 'true' : 'false')
                + '&search=' + encodeURIComponent(search || '')
                + '&page=' + (page || 1);
            htmx.ajax('GET', url, { target: '#entity-picker-content', swap: 'innerHTML' });
        }
        function entityPickerSearch(value) {
            clearTimeout(_pickerSearchTimer);
            _pickerSearchTimer = setTimeout(function () { loadPickerContent(value, 1); }, 300);
        }
        function loadPickerPage(page) {
            var search = document.getElementById('entity-picker-search')?.value ?? '';
            loadPickerContent(search, page);
        }
        function pickerSelectFromRow(row) {
            if (!_pickerConfig) return;
            var id = row.dataset.pickerId, label = row.dataset.pickerLabel, cfg = _pickerConfig;
            var container = row.closest('[data-picker-container]');
            var selector = function(name) {
                var el = container ? container.querySelector('[id="' + name + '"]') : null;
                return el || document.querySelector('[id="' + name + '"]');
            };
            if (cfg.multi) {
                var hidden = selector('picker-value-' + cfg.fieldName);
                if (!hidden) return;
                var vals = hidden.value ? hidden.value.split(',').filter(function(v) { return v !== ''; }) : [];
                if (vals.indexOf(id) !== -1) return;
                vals.push(id);
                hidden.value = vals.join(',');
                var chips = selector('picker-chips-' + cfg.fieldName);
                if (chips) {
                    var chip = document.createElement('div');
                    chip.className = 'badge badge-neutral gap-1';
                    chip.dataset.id = id;
                    chip.innerHTML = label + ' <button type="button" onclick="removePickerChip(\'' + cfg.fieldName + '\',\'' + id.replace(/'/g, "\\'") + '\',this.parentElement)">✕</button>';
                    chips.appendChild(chip);
                }
            } else {
                var hidden2 = selector('picker-value-' + cfg.fieldName);
                var display = selector('picker-display-' + cfg.fieldName);
                if (hidden2) hidden2.value = id;
                if (display) { if (display.tagName === 'INPUT') { display.value = label; } else { display.textContent = label; } }
                document.getElementById('entity-picker-modal').close();
            }
        }
        function removePickerChip(fieldName, id, chipEl) {
            var container = chipEl?.closest('[data-picker-container]');
            var hidden = container ? container.querySelector('[id="picker-value-' + fieldName + '"]') : document.querySelector('[id="picker-value-' + fieldName + '"]');
            if (hidden) { hidden.value = hidden.value.split(',').filter(function(v) { return v !== id && v !== ''; }).join(','); }
            if (chipEl) chipEl.remove();
        }
        function clearPickerValue(fieldName, sourceEl) {
            var container = sourceEl?.closest('[data-picker-container]');
            var sel = function(n) { return (container ? container.querySelector('[id="' + n + '"]') : null) || document.querySelector('[id="' + n + '"]'); };
            var h = sel('picker-value-' + fieldName), d = sel('picker-display-' + fieldName);
            if (h) h.value = '';
            if (d) { if (d.tagName === 'INPUT') { d.value = ''; } else { d.textContent = '(All)'; } }
        }
        function clearPickerFilterValue(fieldName, sourceEl) {
            var container = sourceEl?.closest('[data-picker-container]');
            var sel = function(n) { return (container ? container.querySelector('[id="' + n + '"]') : null) || document.querySelector('[id="' + n + '"]'); };
            var h = sel('picker-value-' + fieldName), d = sel('picker-display-' + fieldName), c = sel('picker-chips-' + fieldName);
            if (h) h.value = '';
            if (d) { if (d.tagName === 'INPUT') { d.value = ''; } else { d.textContent = '(All)'; } }
            if (c) c.innerHTML = '';
        }
        function toggleCheckboxGroup(fieldName, checkbox) {
            var hidden = document.getElementById('checkbox-group-value-' + fieldName);
            if (!hidden) return;
            var values = [];
            document.querySelectorAll('input[type=checkbox][name=' + fieldName + ']').forEach(function(cb) { if (cb.checked) values.push(cb.value); });
            hidden.value = values.join(',');
        }
        function toggleSwitchGroup(fieldName, toggle) {
            var hidden = document.getElementById('switch-group-value-' + fieldName);
            if (!hidden) return;
            var values = [];
            document.querySelectorAll('input[type=checkbox][name=' + fieldName + ']').forEach(function(t) { if (t.checked) values.push(t.value); });
            hidden.value = values.join(',');
        }
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
