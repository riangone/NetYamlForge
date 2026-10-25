using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI.AgentStudio;

namespace NetYamlForge.Services.AI.AgentStudio.Manifestation;

/// <summary>
/// 自愈修补引擎服务接口
/// </summary>
public interface IManifestationSelfHealingService
{
    /// <summary>
    /// 对诊断出的问题执行智能自愈修补
    /// </summary>
    Task<HealingAttemptRecord> AttemptRepairAsync(
        string projectDir,
        string projectName,
        ManifestationDiagnosticReport report,
        int attemptNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 自愈修补引擎实现：结合规则驱动的确定性修复与大语言模型/CLI 智能推演修复
/// </summary>
public class ManifestationSelfHealingService : IManifestationSelfHealingService
{
    private readonly ICliChainService? _cliChainService;
    private readonly ILogger<ManifestationSelfHealingService> _logger;

    public ManifestationSelfHealingService(
        ILogger<ManifestationSelfHealingService> logger,
        ICliChainService? cliChainService = null)
    {
        _logger = logger;
        _cliChainService = cliChainService;
    }

    public async Task<HealingAttemptRecord> AttemptRepairAsync(
        string projectDir,
        string projectName,
        ManifestationDiagnosticReport report,
        int attemptNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SelfHealing] Attempt #{Attempt} starting for project '{Project}'. Found {Count} diagnostic issues.",
            attemptNumber, projectName, report.Issues.Count);

        var repairedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var repairLogs = new List<string>();

        var errorIssues = report.Issues.Where(i => i.Severity == DiagnosticSeverityLevel.Error).ToList();

        foreach (var issue in errorIssues)
        {
            var filePath = Path.Combine(projectDir, issue.RelativePath);

            // 策略 1: 缺少 Action Handler 实现自动补全 (NYF_VALIDATOR_008)
            if (issue.RuleId == "NYF_VALIDATOR_008")
            {
                var match = Regex.Match(issue.Message, @"'([a-zA-Z0-9_]+)'");
                if (match.Success)
                {
                    var handlerName = match.Groups[1].Value.Trim();
                    var scaffoldedPath = ScaffoldActionHandler(projectDir, projectName, handlerName);
                    if (!string.IsNullOrEmpty(scaffoldedPath))
                    {
                        var rel = Path.GetRelativePath(projectDir, scaffoldedPath).Replace('\\', '/');
                        repairedFiles.Add(rel);
                        repairLogs.Add($"✅ 自动补全动作处理器桩代码: '{handlerName}' -> `{rel}`");
                    }
                }
                continue;
            }

            // 策略 2: 实体 YAML 缺少 'entities:' 根键 (NYF_VALIDATOR_006)
            if (issue.RuleId == "NYF_VALIDATOR_006" && File.Exists(filePath))
            {
                try
                {
                    var raw = await File.ReadAllTextAsync(filePath, cancellationToken);
                    if (!raw.Contains("entities:"))
                    {
                        var entityKey = Path.GetFileNameWithoutExtension(filePath);
                        var indented = string.Join("\n", raw.Split('\n').Select(l => "    " + l));
                        var fixedContent = $"entities:\n  {entityKey}:\n{indented}\n";
                        await File.WriteAllTextAsync(filePath, fixedContent, Encoding.UTF8, cancellationToken);
                        repairedFiles.Add(issue.RelativePath);
                        repairLogs.Add($"✅ 规范化实体 YAML 结构并包裹根键: `{issue.RelativePath}`");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SelfHealing] Failed to auto-fix YAML entities root key in {Path}", filePath);
                }
                continue;
            }

            // 策略 3: C# 语法错误修复 (Roslyn C# Compilation Error)
            if (issue.Message.Contains("Roslyn C#") && File.Exists(filePath))
            {
                var fixedLocally = await TryFixSimpleCSharpSyntaxAsync(filePath, issue, cancellationToken);
                if (fixedLocally)
                {
                    repairedFiles.Add(issue.RelativePath);
                    repairLogs.Add($"✅ 本地规则修复 C# 语法错误: `{issue.RelativePath}` (Line {issue.Line})");
                    continue;
                }

                // 若规则无法修复且 CLI 服务可用，启动 AI 诊断自愈
                if (_cliChainService != null)
                {
                    var fixedByAi = await TryRepairCSharpViaAiAsync(filePath, issue, projectName, cancellationToken);
                    if (fixedByAi)
                    {
                        repairedFiles.Add(issue.RelativePath);
                        repairLogs.Add($"🤖 智能体推演修复 C# 代码编译问题: `{issue.RelativePath}`");
                        continue;
                    }
                }
            }

            // 策略 4: 缺少 project.yaml 自动脚手架补全
            if (issue.RuleId == "NYF_VALIDATOR_001")
            {
                var projectYamlPath = Path.Combine(projectDir, "project.yaml");
                var scaffoldedYaml = $@"name: {projectName}
displayName: ""{projectName}""
database:
  type: sqlite
  path: database/{projectName}.db
layout:
  title: ""{projectName}""
  landingPageByRole:
    admin: ""/{projectName}/Page/HomePage""
features:
  pages: true
  dashboard: true
";
                await File.WriteAllTextAsync(projectYamlPath, scaffoldedYaml, Encoding.UTF8, cancellationToken);
                repairedFiles.Add("project.yaml");
                repairLogs.Add("✅ 自动补全缺失的根配置文件: `project.yaml`");
            }

            // 策略 5: 声明式 UI 页面枚举规范化 (纠正 sourceType: entity 为合规的 sourceType: table)
            if (issue.RuleId == "NYF_VALIDATOR_007" && File.Exists(filePath))
            {
                try
                {
                    var raw = await File.ReadAllTextAsync(filePath, cancellationToken);
                    if (raw.Contains("sourceType: entity"))
                    {
                        var fixedContent = raw.Replace("sourceType: entity", "sourceType: table");
                        await File.WriteAllTextAsync(filePath, fixedContent, Encoding.UTF8, cancellationToken);
                        repairedFiles.Add(issue.RelativePath);
                        repairLogs.Add($"✅ 自动修正 UI 页面规范: sourceType: entity -> sourceType: table (`{issue.RelativePath}`)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SelfHealing] Failed to auto-fix UI page yaml schema in {Path}", filePath);
                }
            }

            // 策略 6: 实体 YAML 常见属性名自愈 (将误用的 fields: 自动替换为标准的 columns:)
            if (File.Exists(filePath) && (issue.RelativePath.StartsWith("entities/", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var raw = await File.ReadAllTextAsync(filePath, cancellationToken);
                    if (raw.Contains("fields:") && !raw.Contains("columns:"))
                    {
                        var fixedContent = raw.Replace("fields:", "columns:");
                        await File.WriteAllTextAsync(filePath, fixedContent, Encoding.UTF8, cancellationToken);
                        repairedFiles.Add(issue.RelativePath);
                        repairLogs.Add($"✅ 自动修正实体属性规范: fields: -> columns: (`{issue.RelativePath}`)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SelfHealing] Failed to auto-fix fields->columns in {Path}", filePath);
                }
            }
        }

        var anyRepaired = repairedFiles.Count > 0;
        var summary = anyRepaired
            ? $"自愈尝试成功修补 {repairedFiles.Count} 个文件:\n" + string.Join("\n", repairLogs)
            : "未找到适用自动规则修复的问题";

        _logger.LogInformation("[SelfHealing] Attempt #{Attempt} finished. Modified files: {Count}. Summary: {Summary}",
            attemptNumber, repairedFiles.Count, summary);

        return new HealingAttemptRecord(
            Attempt: attemptNumber,
            Strategy: "Deterministic Rules + AI Assisted Repair",
            Succeeded: anyRepaired,
            Summary: summary,
            RepairedFiles: repairedFiles.ToList()
        );
    }

    private static string? ScaffoldActionHandler(string projectDir, string projectName, string handlerName)
    {
        try
        {
            var hooksDir = Path.Combine(projectDir, "Hooks");
            Directory.CreateDirectory(hooksDir);

            var cleanProjectNamespace = Regex.Replace(projectName, @"[^a-zA-Z0-9]", "");
            var className = ToPascalCase(handlerName) + "Handler";
            var stubCode = $@"using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.{cleanProjectNamespace}.Hooks;

/// <summary>
/// 诊断自愈引擎自动生成的动作处理器桩实现 ({handlerName})
/// </summary>
public sealed class {className} : ICustomActionHandler
{{
    public string Name => ""{handlerName}"";

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {{
        return Task.FromResult(ActionHandlerResult.Success());
    }}
}}
";
            var stubPath = Path.Combine(hooksDir, $"{className}.cs");
            File.WriteAllText(stubPath, stubCode, Encoding.UTF8);
            return stubPath;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TryFixSimpleCSharpSyntaxAsync(string filePath, DiagnosticIssue issue, CancellationToken ct)
    {
        try
        {
            var code = await File.ReadAllTextAsync(filePath, ct);

            // 1. 修复大括号未闭合（CS1513: } expected）
            if (issue.Message.Contains("CS1513") || issue.Message.Contains("} expected"))
            {
                var openCount = code.Count(c => c == '{');
                var closeCount = code.Count(c => c == '}');
                if (openCount > closeCount)
                {
                    var missing = new string('}', openCount - closeCount);
                    var fixedCode = code.TrimEnd() + "\n" + string.Join("\n", missing.Select(c => "}")) + "\n";
                    await File.WriteAllTextAsync(filePath, fixedCode, Encoding.UTF8, ct);
                    return true;
                }
            }

            // 2. 修复行末缺少分号（CS1002: ; expected）
            if (issue.Message.Contains("CS1002") || issue.Message.Contains("; expected"))
            {
                if (issue.Line.HasValue && issue.Line.Value > 0)
                {
                    var lines = code.Split('\n');
                    var targetIdx = issue.Line.Value - 1;
                    if (targetIdx < lines.Length)
                    {
                        var targetLine = lines[targetIdx].TrimEnd();
                        if (!targetLine.EndsWith(';') && !targetLine.EndsWith('{') && !targetLine.EndsWith('}'))
                        {
                            lines[targetIdx] = targetLine + ";";
                            await File.WriteAllTextAsync(filePath, string.Join("\n", lines), Encoding.UTF8, ct);
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryRepairCSharpViaAiAsync(string filePath, DiagnosticIssue issue, string projectName, CancellationToken ct)
    {
        if (_cliChainService == null) return false;

        try
        {
            var currentCode = await File.ReadAllTextAsync(filePath, ct);
            var prompt = $@"
你是一个精通 C# 12 和 ASP.NET Core 的编译器自愈专家。
目标项目: {projectName}
文件路径: {issue.RelativePath}
诊断错误信息: {issue.Message} (行号: {issue.Line})

当前文件完整代码如下:
```csharp
{currentCode}
```

请修复上述代码中的编译/语法错误。
输出要求:
仅输出修复后的完整 C# 代码，包裹在 ```csharp ... ``` 代码块内，不要包含其他解释文字。
";

            var result = await _cliChainService.PromptAsync(
                prompt: prompt,
                imagePath: null,
                projectName: projectName,
                preferredProvider: "opencode",
                model: null,
                variant: null,
                timeoutSeconds: 60,
                cancellationToken: ct);

            if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
            {
                var match = Regex.Match(result.Text, @"```(?:csharp|cs)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
                var repairedCode = match.Success ? match.Groups[1].Value.Trim() : result.Text.Trim();

                if (repairedCode.Contains("namespace") || repairedCode.Contains("class"))
                {
                    await File.WriteAllTextAsync(filePath, repairedCode, Encoding.UTF8, ct);
                    _logger.LogInformation("[SelfHealing] Successfully repaired {Path} via AI CLI", filePath);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SelfHealing] AI repair attempt failed for {Path}", filePath);
        }

        return false;
    }

    private static string ToPascalCase(string text)
    {
        var parts = text.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (p.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p[1..]);
            }
        }
        return sb.Length > 0 ? sb.ToString() : "Custom";
    }
}
