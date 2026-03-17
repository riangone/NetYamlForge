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
        if (string.Equals(dbType, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return $$"""
name: {{projectName}}
displayName: {{displayName}}
version: "1.0.0"
database:
  type: {{dbType}}
  path: {{dbPath}}
features:
  multiLanguage: true
  userAuthentication: true
""" + Environment.NewLine;
        }

        return $$"""
name: {{projectName}}
displayName: {{displayName}}
version: "1.0.0"
database:
  type: {{dbType}}
  connectionString: {{dbConnectionString}}
features:
  multiLanguage: true
  userAuthentication: true
""" + Environment.NewLine;
    }

    private static string BuildHomePageYaml(string projectName, string displayName)
    {
        return $$"""
hero:
  eyebrow: {{displayName}}
  eyebrowKey: projects.{{projectName}}.home.hero.eyebrow
  title: {{displayName}} Workspace
  titleKey: projects.{{projectName}}.home.hero.title
  description: Starter workspace generated from your database schema.
  descriptionKey: projects.{{projectName}}.home.hero.description
  primaryActionLabel: Open Dashboard
  primaryActionLabelKey: projects.{{projectName}}.home.hero.primaryActionLabel
  primaryActionUrl: /{{projectName}}/Dashboard
  secondaryActionLabel: Open Schema Definitions
  secondaryActionLabelKey: projects.{{projectName}}.home.hero.secondaryActionLabel
  secondaryActionUrl: /{{projectName}}/DynamicEntity/AllDefinitions
  highlights:
    - Template Ready
    - YAML First
  highlightKeys:
    - projects.{{projectName}}.home.hero.highlights.0
    - projects.{{projectName}}.home.hero.highlights.1

projectsSectionTitle: {{displayName}}
projectsSectionTitleKey: projects.{{projectName}}.home.sections.projects.title
projectsSectionLead: Build your business screens from this starter project.
projectsSectionLeadKey: projects.{{projectName}}.home.sections.projects.lead
capabilitiesSectionTitle: Core Capabilities
capabilitiesSectionTitleKey: projects.{{projectName}}.home.sections.capabilities.title
solutionsSectionTitle: Solution Matrix
solutionsSectionTitleKey: projects.{{projectName}}.home.sections.solutions.title
quickActionsSectionTitle: Quick Actions
quickActionsSectionTitleKey: projects.{{projectName}}.home.sections.quickActions.title
openWorkspaceLabel: Open Workspace
openWorkspaceLabelKey: projects.{{projectName}}.home.actions.openWorkspace
emptyProjectsMessage: No projects found under projects/.
emptyProjectsMessageKey: projects.{{projectName}}.home.emptyProjectsMessage

quickActions:
  - label: Dashboard
    labelKey: projects.{{projectName}}.home.quickActions.0.label
    url: /{{projectName}}/Dashboard
    style: btn-primary
    icon: 📊
  - label: Entity Definitions
    labelKey: projects.{{projectName}}.home.quickActions.1.label
    url: /{{projectName}}/DynamicEntity/AllDefinitions
    style: btn-outline
    icon: 🧩
  - label: Starter Page
    labelKey: projects.{{projectName}}.home.quickActions.2.label
    url: /{{projectName}}/Page/StarterOverview
    style: btn-outline
    icon: 🧭
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
""" + Environment.NewLine;
    }

    private static string BuildI18nYaml(string projectName)
    {
        return $$"""
translations:
  projects.{{projectName}}.template.welcome:
    en-US: Starter template
    zh-CN: Starter template
    ja-JP: Starter template
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
            sb.AppendLine("  []");
        }
        else
        {
            for (var i = 0; i < entityNames.Count; i++)
            {
                var entity = entityNames[i];
                var label = $"{ToDisplayName(entity)} Count";
                sb.AppendLine($"  - label: {label}");
                sb.AppendLine($"    labelKey: projects.{projectName}.dashboard.stats.{i}.label");
                sb.AppendLine($"    entity: {entity}");
                sb.AppendLine("    aggregate: count");
                sb.AppendLine("    icon: 📊");
                sb.AppendLine("    color: badge-info");
            }
        }

        sb.AppendLine("charts: []");
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

        var viewCshtml = """
@model Dictionary<string, (IEnumerable<Dictionary<string, object>> Rows, int Total)>
@{
    var pageName = ViewData["PageName"]?.ToString() ?? "StarterOverview";
    var title = ViewData["Title"]?.ToString() ?? "Starter Overview";
}
<div class="space-y-4">
    <h1 class="text-2xl font-bold">@title</h1>
    <p class="opacity-70">This is an auto-generated starter page template: @pageName</p>
    @foreach (var section in Model)
    {
        <div class="card bg-base-100 border border-base-300 shadow-sm">
            <div class="card-body">
                <h2 class="card-title">@(section.Key) (@(section.Value.Total))</h2>
                <div class="overflow-x-auto">
                    <table class="table table-zebra table-sm w-full">
                        <thead>
                            <tr>
                                @if (section.Value.Rows.Any())
                                {
                                    @foreach (var col in section.Value.Rows.First().Keys)
                                    {
                                        <th>@col</th>
                                    }
                                }
                            </tr>
                        </thead>
                        <tbody>
                            @foreach (var row in section.Value.Rows)
                            {
                                <tr>
                                    @foreach (var val in row.Values)
                                    {
                                        <td>@(val?.ToString())</td>
                                    }
                                </tr>
                            }
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
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
    <title>@ViewData["Title"] - {{displayName}}</title>
    <script type="importmap"></script>
    <link href="https://cdn.jsdelivr.net/npm/daisyui@5" rel="stylesheet" type="text/css" />
    <script src="https://cdn.jsdelivr.net/npm/@@tailwindcss/browser@4"></script>
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/NetYamlForge.styles.css" asp-append-version="true" />
</head>
<body class="min-h-screen bg-base-200">
    @{
        var currentProject = Context.GetRouteValue("project")?.ToString() ?? "{{projectName}}";
    }
    <header class="navbar bg-base-100 border-b border-base-300 px-4">
        <div class="flex-1">
            <a class="btn btn-ghost text-xl"
               asp-controller="Home"
               asp-action="Project"
               asp-route-project="@currentProject">{{displayName}}</a>
        </div>
        <div class="flex-none gap-2">
            <a class="btn btn-ghost btn-sm"
               asp-controller="Dashboard"
               asp-action="Index"
               asp-route-project="@currentProject">Dashboard</a>
            <a class="btn btn-ghost btn-sm"
               asp-controller="Page"
               asp-action="Index"
               asp-route-project="@currentProject"
               asp-route-pageName="StarterOverview">Starter</a>
            <a class="btn btn-ghost btn-sm"
               asp-controller="DynamicEntity"
               asp-action="AllDefinitions"
               asp-route-project="@currentProject">Entities</a>
        </div>
    </header>
    <main class="p-4 lg:p-6">
        @RenderBody()
    </main>
    <script src="https://unpkg.com/htmx.org@1.9.12"></script>
    <script src="~/js/site.js" asp-append-version="true"></script>
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

このディレクトリは `--init-project` で生成された最小テンプレートです。

## 次の作業

1. `project.yaml` の DB 設定を確認
2. `dotnet run -- --scaffold-entities --project={{projectName}}` を実行
3. `entities/` と `pages/` に必要な YAML を追加
4. `/{{projectName}}` と `/{{projectName}}/Dashboard` を確認
""" + Environment.NewLine;
    }
}
