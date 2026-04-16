using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Services;

namespace NetYamlForge.Services.AI;

/// <summary>
/// Harness 上下文适配器
/// 将 NetYamlForge 的项目结构、实体定义等信息转换为 Harness AI Pipeline 可理解的上下文
/// </summary>
public class HarnessContextAdapter
{
    private readonly ILogger<HarnessContextAdapter> _logger;
    private readonly ProjectManager _projectManager;

    public HarnessContextAdapter(
        ILogger<HarnessContextAdapter> logger,
        ProjectManager projectManager)
    {
        _logger = logger;
        _projectManager = projectManager;
    }

    /// <summary>
    /// 构建 AI Pipeline 的上下文字符串
    /// </summary>
    /// <param name="projectName">项目名称</param>
    /// <param name="includeEntities">是否包含实体定义</param>
    /// <param name="includePages">是否包含页面配置</param>
    /// <param name="includeProjectStructure">是否包含项目结构</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>格式化的上下文字符串</returns>
    public async Task<string> BuildContextAsync(
        string projectName,
        bool includeEntities = true,
        bool includePages = true,
        bool includeProjectStructure = true,
        CancellationToken ct = default)
    {
        var contextParts = new List<string>();

        // 项目基本信息
        if (_projectManager.TryGet(projectName, out var project))
        {
            contextParts.Add(BuildProjectInfoContext(project!));
        }

        // 实体定义
        if (includeEntities && project != null)
        {
            var entitiesContext = await BuildEntitiesContextAsync(project, ct);
            if (!string.IsNullOrEmpty(entitiesContext))
            {
                contextParts.Add(entitiesContext);
            }
        }

        // 页面配置
        if (includePages && project != null)
        {
            var pagesContext = await BuildPagesContextAsync(project, ct);
            if (!string.IsNullOrEmpty(pagesContext))
            {
                contextParts.Add(pagesContext);
            }
        }

        // 项目结构
        if (includeProjectStructure && project != null)
        {
            var structureContext = BuildProjectStructureContext(project);
            if (!string.IsNullOrEmpty(structureContext))
            {
                contextParts.Add(structureContext);
            }
        }

        return string.Join("\n\n", contextParts);
    }

    /// <summary>
    /// 构建项目基本信息上下文
    /// </summary>
    private string BuildProjectInfoContext(ProjectInfo project)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 项目信息");
        sb.AppendLine($"- 名称: {project.Name}");
        sb.AppendLine($"- 显示名称: {project.DisplayName}");
        sb.AppendLine($"- 数据库类型: {project.DatabaseType}");

        if (project.Layout != null)
        {
            sb.AppendLine("- 布局配置:");
            sb.AppendLine($"  - ダッシュボードテーマ: {project.Layout.DashboardTheme ?? "default"}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建实体定义上下文
    /// </summary>
    private async Task<string> BuildEntitiesContextAsync(ProjectInfo project, CancellationToken ct)
    {
        var entities = project.EntityMetadata.GetAll();
        if (entities == null || entities.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("# 实体定义");
        sb.AppendLine($"共 {entities.Count} 个实体");

        foreach (var kvp in entities.Take(10)) // 限制数量避免上下文过长
        {
            if (ct.IsCancellationRequested) break;

            var entityName = kvp.Key;
            var entity = kvp.Value;

            sb.AppendLine($"\n## 实体: {entityName}");
            sb.AppendLine($"- 表名: {entity.Table}");
            sb.AppendLine($"- 显示名称: {entity.DisplayName}");

            if (entity.Columns != null && entity.Columns.Count > 0)
            {
                sb.AppendLine($"- 字段 ({entity.Columns.Count} 个):");
                foreach (var colKvp in entity.Columns.Take(20)) // 限制字段数量
                {
                    var colName = colKvp.Key;
                    var col = colKvp.Value;
                    var required = col.Required ? " [必需]" : "";
                    var type = col.Type ?? "string";
                    sb.AppendLine($"  - {colName} ({type}){required}");
                }

                if (entity.Columns.Count > 20)
                {
                    sb.AppendLine($"  ... 还有 {entity.Columns.Count - 20} 个字段");
                }
            }

            if (entity.Joins != null && entity.Joins.Count > 0)
            {
                sb.AppendLine($"- 关系 (Joins):");
                foreach (var join in entity.Joins)
                {
                    sb.AppendLine($"  - {join.Type} -> {join.Table} ON {join.On}");
                }
            }
        }

        if (entities.Count > 10)
        {
            sb.AppendLine($"\n... 还有 {entities.Count - 10} 个实体（为保持上下文简洁已省略）");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建页面配置上下文
    /// </summary>
    private async Task<string> BuildPagesContextAsync(ProjectInfo project, CancellationToken ct)
    {
        var projectDir = project.ProjectDir;
        if (string.IsNullOrEmpty(projectDir))
            return string.Empty;

        var pagesDir = Path.Combine(projectDir, "pages");
        if (!Directory.Exists(pagesDir))
            return string.Empty;

        var pageFiles = Directory.GetFiles(pagesDir, "*.yaml", SearchOption.TopDirectoryOnly);
        if (pageFiles.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("# 页面配置");
        sb.AppendLine($"共 {pageFiles.Length} 个页面");

        foreach (var pageFile in pageFiles.Take(5)) // 限制数量
        {
            if (ct.IsCancellationRequested) break;

            var fileName = Path.GetFileName(pageFile);
            sb.AppendLine($"\n## 页面: {fileName}");
            
            try
            {
                var content = await File.ReadAllTextAsync(pageFile, ct);
                // 只取前 500 字符作为上下文
                if (content.Length > 500)
                {
                    sb.AppendLine(content.Substring(0, 500));
                    sb.AppendLine("... (已截断)");
                }
                else
                {
                    sb.AppendLine(content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取页面文件失败: {File}", pageFile);
            }
        }

        if (pageFiles.Length > 5)
        {
            sb.AppendLine($"\n... 还有 {pageFiles.Length - 5} 个页面（为保持上下文简洁已省略）");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建项目结构上下文
    /// </summary>
    private string BuildProjectStructureContext(ProjectInfo project)
    {
        var projectDir = project.ProjectDir;
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("# 项目结构");

        var dirs = Directory.GetDirectories(projectDir, "*", SearchOption.TopDirectoryOnly)
            .Select(d => Path.GetRelativePath(projectDir, d))
            .Where(d => !d.StartsWith("database") && !d.StartsWith(".git"))
            .ToList();

        var files = Directory.GetFiles(projectDir, "*", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetRelativePath(projectDir, f))
            .Where(f => f.EndsWith(".yaml") || f.EndsWith(".yml") || f.EndsWith(".md"))
            .ToList();

        if (dirs.Count > 0)
        {
            sb.AppendLine("目录:");
            foreach (var dir in dirs)
            {
                sb.AppendLine($"  - {dir}/");
            }
        }

        if (files.Count > 0)
        {
            sb.AppendLine("文件:");
            foreach (var file in files)
            {
                sb.AppendLine($"  - {file}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 生成 AI Prompt 模板（包含上下文）
    /// </summary>
    /// <param name="projectName">项目名称</param>
    /// <param name="taskDescription">任务描述</param>
    /// <param name="rule">AI 生成规则</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>完整的 Prompt</returns>
    public async Task<string> GeneratePromptWithContextAsync(
        string projectName,
        string taskDescription,
        AiGenerationRule rule,
        CancellationToken ct = default)
    {
        var context = await BuildContextAsync(
            projectName,
            includeEntities: rule.Context.Entities.Count > 0,
            includePages: rule.Context.Pages.Count > 0,
            includeProjectStructure: rule.Context.IncludeProjectStructure,
            ct: ct);

        var promptTemplate = rule.Pipeline.PromptTemplate;
        if (string.IsNullOrEmpty(promptTemplate))
        {
            // 默认模板
            promptTemplate = @"请根据以下项目上下文完成任务：

{{CONTEXT}}

## 任务
{{TASK}}

## 要求
- 遵循项目现有的代码风格和架构模式
- 确保生成的代码可以集成到现有项目中
- 包含必要的注释和文档";
        }

        // 替换变量
        var prompt = promptTemplate
            .Replace("{{CONTEXT}}", context)
            .Replace("{{TASK}}", taskDescription)
            .Replace("{{PROJECT}}", projectName);

        if (!string.IsNullOrEmpty(rule.Context.AdditionalInfo))
        {
            prompt += $"\n\n## 额外说明\n{rule.Context.AdditionalInfo}";
        }

        return prompt;
    }
}
