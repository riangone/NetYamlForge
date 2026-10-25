using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetYamlForge.Services;
using YamlDotNet.Serialization;

namespace NetYamlForge.Services.AI.AgentStudio.Manifestation;

/// <summary>
/// 交付完整性与语法诊断验证器：负责在物理落盘后，对 YAML、C# 语法、SQL 及声明式契约执行深度诊断
/// </summary>
public static class ManifestationValidator
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    /// <summary>
    /// 对已落盘的项目进行全方位静态与语法诊断
    /// </summary>
    public static ManifestationDiagnosticReport ValidateProject(string projectDir, string projectName)
    {
        var issues = new List<DiagnosticIssue>();

        if (!Directory.Exists(projectDir))
        {
            issues.Add(new DiagnosticIssue(".", null, "目标项目目录不存在", DiagnosticSeverityLevel.Error));
            return new ManifestationDiagnosticReport(false, issues);
        }

        // 1. 验证 project.yaml 存在性与语法
        var projectYamlPath = Path.Combine(projectDir, "project.yaml");
        if (!File.Exists(projectYamlPath))
        {
            projectYamlPath = Path.Combine(projectDir, "project.yml");
        }

        if (!File.Exists(projectYamlPath))
        {
            issues.Add(new DiagnosticIssue("project.yaml", null, "缺少核心配置文件 project.yaml", DiagnosticSeverityLevel.Error, "NYF_VALIDATOR_001"));
        }
        else
        {
            try
            {
                var yamlContent = File.ReadAllText(projectYamlPath);
                var dict = YamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);
                if (dict == null || (!dict.ContainsKey("name") && !dict.ContainsKey("Name")))
                {
                    issues.Add(new DiagnosticIssue("project.yaml", 1, "project.yaml 中必须定义 'name' 属性", DiagnosticSeverityLevel.Error, "NYF_VALIDATOR_002"));
                }
            }
            catch (Exception ex)
            {
                issues.Add(new DiagnosticIssue("project.yaml", ExtractLineNumber(ex.Message), $"project.yaml 解析语法错误: {ex.Message}", DiagnosticSeverityLevel.Error, "NYF_VALIDATOR_003"));
            }
        }

        // 2. 验证 entities/*.yml 语法与规范
        var entitiesDir = Path.Combine(projectDir, "entities");
        var declaredActionHandlers = new List<(string EntityPath, string HandlerName, int Line)>();

        if (Directory.Exists(entitiesDir))
        {
            foreach (var ymlFile in Directory.GetFiles(entitiesDir, "*.yml").Concat(Directory.GetFiles(entitiesDir, "*.yaml")))
            {
                var relPath = Path.GetRelativePath(projectDir, ymlFile).Replace('\\', '/');
                var text = File.ReadAllText(ymlFile);

                try
                {
                    var parsed = YamlDeserializer.Deserialize<object>(text);
                    if (parsed == null)
                    {
                        issues.Add(new DiagnosticIssue(relPath, 1, "实体配置文件内容为空", DiagnosticSeverityLevel.Warning, "NYF_VALIDATOR_004"));
                    }
                }
                catch (Exception ex)
                {
                    issues.Add(new DiagnosticIssue(relPath, ExtractLineNumber(ex.Message), $"实体 YAML 解析失败: {ex.Message}", DiagnosticSeverityLevel.Error, "NYF_VALIDATOR_005"));
                }

                if (!text.Contains("entities:"))
                {
                    issues.Add(new DiagnosticIssue(relPath, 1, "实体 YAML 缺少根键 'entities:'", DiagnosticSeverityLevel.Error, "NYF_VALIDATOR_006"));
                }

                // 提取声明的 actions
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var match = Regex.Match(lines[i], @"handler:\s*['""]?([a-zA-Z0-9_]+)['""]?");
                    if (match.Success)
                    {
                        declaredActionHandlers.Add((relPath, match.Groups[1].Value.Trim(), i + 1));
                    }
                }
            }
        }

        // 3. 验证 pages/*.yaml 语法与 Schema
        var pagesDir = Path.Combine(projectDir, "pages");
        if (Directory.Exists(pagesDir))
        {
            foreach (var pageFile in Directory.GetFiles(pagesDir, "*.yaml").Concat(Directory.GetFiles(pagesDir, "*.yml")))
            {
                var relPath = Path.GetRelativePath(projectDir, pageFile).Replace('\\', '/');
                try
                {
                    var pageText = File.ReadAllText(pageFile);
                    _ = YamlDeserializer.Deserialize<object>(pageText);

                    var results = YamlSchemaValidator.TryValidateUiPageYaml(pageText);
                    if (!results.IsValid)
                    {
                        issues.Add(new DiagnosticIssue(relPath, null, "UI Page Schema 规范校验失败 (请确认 sourceType 枚举与字段约束)", DiagnosticSeverityLevel.Error, "NYF_VALIDATOR_007"));
                    }
                }
                catch (Exception ex)
                {
                    issues.Add(new DiagnosticIssue(relPath, ExtractLineNumber(ex.Message), $"声明式页面 YAML 解析失败: {ex.Message}", DiagnosticSeverityLevel.Error, "NYF_VALIDATOR_007"));
                }
            }
        }

        // 4. 验证 Hooks/*.cs 语法分析 (Roslyn CSharpSyntaxTree)
        var hooksDir = Path.Combine(projectDir, "Hooks");
        var allHookCode = "";
        if (Directory.Exists(hooksDir))
        {
            foreach (var csFile in Directory.GetFiles(hooksDir, "*.cs", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(projectDir, csFile).Replace('\\', '/');
                var code = File.ReadAllText(csFile);
                allHookCode += "\n" + code;

                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var diagnostics = syntaxTree.GetDiagnostics();

                foreach (var diag in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    var lineSpan = diag.Location.GetLineSpan();
                    var lineNum = lineSpan.IsValid ? lineSpan.StartLinePosition.Line + 1 : 1;
                    issues.Add(new DiagnosticIssue(
                        relPath,
                        lineNum,
                        $"Roslyn C# 编译错误: {diag.GetMessage()}",
                        DiagnosticSeverityLevel.Error,
                        diag.Id));
                }
            }
        }

        // 5. 校验声明的 Action Handlers 是否存在 C# 实现（严格防空定义）
        foreach (var (entityPath, handlerName, line) in declaredActionHandlers)
        {
            if (string.IsNullOrWhiteSpace(handlerName)) continue;

            var pattern = $@"(?:Name\s*=>\s*""{Regex.Escape(handlerName)}""|string\s+Name\s*\{{\s*get;\s*\}}\s*=\s*""{Regex.Escape(handlerName)}"")";
            if (!Regex.IsMatch(allHookCode, pattern, RegexOptions.IgnoreCase))
            {
                issues.Add(new DiagnosticIssue(
                    entityPath,
                    line,
                    $"未找到动作处理器 '{handlerName}' 的物理 C# 实现 (ICustomActionHandler)",
                    DiagnosticSeverityLevel.Error,
                    "NYF_VALIDATOR_008"));
            }
        }

        // 6. 验证 database/init_schema.sql
        var schemaPath = Path.Combine(projectDir, "database", "init_schema.sql");
        if (File.Exists(schemaPath))
        {
            try
            {
                var sql = File.ReadAllText(schemaPath);
                SqlSafetyGuard.EnsureSafeSchemaSql(sql, $"init_schema.sql for project '{projectName}'");
            }
            catch (Exception ex)
            {
                issues.Add(new DiagnosticIssue(
                    "database/init_schema.sql",
                    null,
                    $"SQL 安全检查未通过: {ex.Message}",
                    DiagnosticSeverityLevel.Error,
                    "NYF_VALIDATOR_009"));
            }
        }

        var isValid = issues.All(i => i.Severity != DiagnosticSeverityLevel.Error);
        return new ManifestationDiagnosticReport(isValid, issues);
    }

    private static int? ExtractLineNumber(string errorMsg)
    {
        var match = Regex.Match(errorMsg, @"Line:\s*(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var line))
        {
            return line;
        }
        return null;
    }
}
