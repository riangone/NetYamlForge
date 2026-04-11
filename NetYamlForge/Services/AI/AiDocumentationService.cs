using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.AI;

/// <summary>
/// AI 文档自动生成服务
/// 使用 Harness AI Pipeline 为项目自动生成文档（README、API 文档、架构文档等）
/// </summary>
public class AiDocumentationService
{
    private readonly AiPipelineService _pipelineService;
    private readonly ILogger<AiDocumentationService> _logger;

    public AiDocumentationService(
        AiPipelineService pipelineService,
        ILogger<AiDocumentationService> logger)
    {
        _pipelineService = pipelineService;
        _logger = logger;
    }

    /// <summary>
    /// 生成项目 README 文档
    /// </summary>
    /// <param name="projectName">项目名称</param>
    /// <param name="projectDir">项目目录</param>
    /// <param name="outputPath">输出文件路径（可选）</param>
    /// <param name="timeout">超时秒数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>文档生成结果</returns>
    public async Task<DocumentationResult> GenerateReadmeAsync(
        string projectName,
        string projectDir,
        string? outputPath = null,
        int? timeout = null,
        CancellationToken ct = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var workDir = Path.Combine("/tmp/nyf-harness", $"doc-readme-{taskId}");
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("[AI-Doc] 开始生成 README - TaskId={TaskId}, Project={Project}",
            taskId, projectName);

        try
        {
            var context = await BuildProjectContextAsync(projectDir, ct);
            var prompt = BuildReadmePrompt(projectName, context);

            var result = await _pipelineService.ExecutePipelineAsync(
                prompt: prompt,
                projectName: projectName,
                targetProjectDir: workDir,
                timeout: timeout,
                ct: ct);

            var readmePath = outputPath ?? Path.Combine(projectDir, "README.md");
            var generatedReadme = Path.Combine(workDir, "README.md");

            if (result.Success && File.Exists(generatedReadme))
            {
                File.Copy(generatedReadme, readmePath, overwrite: true);
                _logger.LogInformation("[AI-Doc] README 已生成 - Path={Path}", readmePath);
            }

            return new DocumentationResult
            {
                TaskId = taskId,
                Success = result.Success,
                DocumentType = "README",
                OutputPath = readmePath,
                GeneratedFiles = result.GeneratedFiles,
                WorkDirectory = workDir,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-Doc] README 生成失败 - TaskId={TaskId}", taskId);
            return new DocumentationResult
            {
                TaskId = taskId,
                Success = false,
                DocumentType = "README",
                WorkDirectory = workDir,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 生成 API 文档
    /// </summary>
    public async Task<DocumentationResult> GenerateApiDocumentationAsync(
        string projectName,
        string projectDir,
        string? outputPath = null,
        int? timeout = null,
        CancellationToken ct = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var workDir = Path.Combine("/tmp/nyf-harness", $"api-doc-{taskId}");
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("[AI-Doc] 开始生成 API 文档 - TaskId={TaskId}", taskId);

        try
        {
            var controllers = FindControllers(projectDir);
            var context = await BuildApiContextAsync(projectDir, controllers, ct);
            var prompt = BuildApiDocPrompt(projectName, context);

            var result = await _pipelineService.ExecutePipelineAsync(
                prompt: prompt,
                projectName: projectName,
                targetProjectDir: workDir,
                timeout: timeout,
                ct: ct);

            var outputPathActual = outputPath ?? Path.Combine(projectDir, "docs", "API.md");
            var generatedDoc = Path.Combine(workDir, "API.md");

            if (result.Success && File.Exists(generatedDoc))
            {
                var outputDir = Path.GetDirectoryName(outputPathActual);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                File.Copy(generatedDoc, outputPathActual, overwrite: true);
                _logger.LogInformation("[AI-Doc] API 文档已生成 - Path={Path}", outputPathActual);
            }

            return new DocumentationResult
            {
                TaskId = taskId,
                Success = result.Success,
                DocumentType = "API Documentation",
                OutputPath = outputPathActual,
                GeneratedFiles = result.GeneratedFiles,
                WorkDirectory = workDir,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-Doc] API 文档生成失败 - TaskId={TaskId}", taskId);
            return new DocumentationResult
            {
                TaskId = taskId,
                Success = false,
                DocumentType = "API Documentation",
                WorkDirectory = workDir,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 生成架构文档
    /// </summary>
    public async Task<DocumentationResult> GenerateArchitectureDocAsync(
        string projectName,
        string projectDir,
        string? outputPath = null,
        int? timeout = null,
        CancellationToken ct = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var workDir = Path.Combine("/tmp/nyf-harness", $"arch-doc-{taskId}");
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("[AI-Doc] 开始生成架构文档 - TaskId={TaskId}", taskId);

        try
        {
            var context = await BuildArchitectureContextAsync(projectDir, ct);
            var prompt = BuildArchitectureDocPrompt(projectName, context);

            var result = await _pipelineService.ExecutePipelineAsync(
                prompt: prompt,
                projectName: projectName,
                targetProjectDir: workDir,
                timeout: timeout,
                ct: ct);

            var outputPathActual = outputPath ?? Path.Combine(projectDir, "docs", "ARCHITECTURE.md");
            var generatedDoc = Path.Combine(workDir, "ARCHITECTURE.md");

            if (result.Success && File.Exists(generatedDoc))
            {
                var outputDir = Path.GetDirectoryName(outputPathActual);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                File.Copy(generatedDoc, outputPathActual, overwrite: true);
                _logger.LogInformation("[AI-Doc] 架构文档已生成 - Path={Path}", outputPathActual);
            }

            return new DocumentationResult
            {
                TaskId = taskId,
                Success = result.Success,
                DocumentType = "Architecture Documentation",
                OutputPath = outputPathActual,
                GeneratedFiles = result.GeneratedFiles,
                WorkDirectory = workDir,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-Doc] 架构文档生成失败 - TaskId={TaskId}", taskId);
            return new DocumentationResult
            {
                TaskId = taskId,
                Success = false,
                DocumentType = "Architecture Documentation",
                WorkDirectory = workDir,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 生成实体文档
    /// </summary>
    public async Task<DocumentationResult> GenerateEntityDocumentationAsync(
        string projectName,
        string projectDir,
        string? outputPath = null,
        int? timeout = null,
        CancellationToken ct = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var workDir = Path.Combine("/tmp/nyf-harness", $"entity-doc-{taskId}");
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("[AI-Doc] 开始生成实体文档 - TaskId={TaskId}", taskId);

        try
        {
            var entities = FindEntityYamlFiles(projectDir);
            var context = await BuildEntityContextAsync(projectDir, entities, ct);
            var prompt = BuildEntityDocPrompt(projectName, context);

            var result = await _pipelineService.ExecutePipelineAsync(
                prompt: prompt,
                projectName: projectName,
                targetProjectDir: workDir,
                timeout: timeout,
                ct: ct);

            var outputPathActual = outputPath ?? Path.Combine(projectDir, "docs", "ENTITIES.md");
            var generatedDoc = Path.Combine(workDir, "ENTITIES.md");

            if (result.Success && File.Exists(generatedDoc))
            {
                var outputDir = Path.GetDirectoryName(outputPathActual);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                File.Copy(generatedDoc, outputPathActual, overwrite: true);
                _logger.LogInformation("[AI-Doc] 实体文档已生成 - Path={Path}", outputPathActual);
            }

            return new DocumentationResult
            {
                TaskId = taskId,
                Success = result.Success,
                DocumentType = "Entity Documentation",
                OutputPath = outputPathActual,
                GeneratedFiles = result.GeneratedFiles,
                WorkDirectory = workDir,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-Doc] 实体文档生成失败 - TaskId={TaskId}", taskId);
            return new DocumentationResult
            {
                TaskId = taskId,
                Success = false,
                DocumentType = "Entity Documentation",
                WorkDirectory = workDir,
                ErrorMessage = ex.Message
            };
        }
    }

    // ===== 辅助方法 =====

    /// <summary>
    /// 构建项目上下文
    /// </summary>
    private async Task<string> BuildProjectContextAsync(string projectDir, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 项目结构");

        // 列出目录结构
        var dirs = Directory.GetDirectories(projectDir, "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(projectDir, d))
            .Where(d => !d.Contains("database") && !d.Contains(".git") && !d.Contains("node_modules"))
            .Take(50);

        sb.AppendLine("```");
        foreach (var dir in dirs)
        {
            sb.AppendLine(dir + "/");
        }
        sb.AppendLine("```");

        // 列出关键文件
        var files = Directory.GetFiles(projectDir, "*.*", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileName(f))
            .Where(f => f.EndsWith(".yaml") || f.EndsWith(".yml") || f.EndsWith(".cs") || f.EndsWith(".md"));

        sb.AppendLine("\n## 关键文件");
        foreach (var file in files)
        {
            sb.AppendLine($"- {file}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建 README Prompt
    /// </summary>
    private string BuildReadmePrompt(string projectName, string context)
    {
        return $@"为项目 '{projectName}' 生成专业的 README.md 文档。

{context}

## 要求
README 应包含以下内容：
1. 项目简介
2. 主要功能特性
3. 技术栈
4. 快速开始指南
5. 项目结构说明
6. 开发指南
7. 部署说明

请使用 Markdown 格式，确保文档清晰、专业、完整。
输出到 README.md 文件。";
    }

    /// <summary>
    /// 查找所有控制器
    /// </summary>
    private List<string> FindControllers(string projectDir)
    {
        var controllersDir = Path.Combine(projectDir, "Controllers");
        if (!Directory.Exists(controllersDir))
            return new List<string>();

        return Directory.GetFiles(controllersDir, "*Controller.cs", SearchOption.AllDirectories)
            .ToList();
    }

    /// <summary>
    /// 构建 API 上下文
    /// </summary>
    private async Task<string> BuildApiContextAsync(
        string projectDir, 
        List<string> controllers,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# API 控制器");
        sb.AppendLine($"共 {controllers.Count} 个控制器");
        sb.AppendLine();

        foreach (var controller in controllers.Take(10))
        {
            if (File.Exists(controller))
            {
                sb.AppendLine($"## 控制器: {Path.GetFileName(controller)}");
                var content = await File.ReadAllTextAsync(controller, ct);
                sb.AppendLine("```csharp");
                sb.AppendLine(content.Length > 1000 ? content.Substring(0, 1000) + "\n// ... (已截断)" : content);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建 API 文档 Prompt
    /// </summary>
    private string BuildApiDocPrompt(string projectName, string context)
    {
        return $@"为项目 '{projectName}' 生成详细的 API 文档。

{context}

## 要求
API 文档应包含：
1. 每个端点的 URL 和 HTTP 方法
2. 请求参数说明
3. 请求/响应示例
4. 错误码说明
5. 认证和授权要求

请使用 Markdown 格式，使用表格和代码块提高可读性。
输出到 API.md 文件。";
    }

    /// <summary>
    /// 构建架构上下文
    /// </summary>
    private async Task<string> BuildArchitectureContextAsync(
        string projectDir, 
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        
        // 分析项目结构
        var servicesDir = Path.Combine(projectDir, "Services");
        var modelsDir = Path.Combine(projectDir, "Models");
        var controllersDir = Path.Combine(projectDir, "Controllers");

        sb.AppendLine("# 项目架构信息");
        sb.AppendLine();

        if (Directory.Exists(servicesDir))
        {
            var services = Directory.GetFiles(servicesDir, "*.cs", SearchOption.AllDirectories).Length;
            sb.AppendLine($"- 服务类: {services} 个");
        }

        if (Directory.Exists(modelsDir))
        {
            var models = Directory.GetFiles(modelsDir, "*.cs", SearchOption.AllDirectories).Length;
            sb.AppendLine($"- 模型类: {models} 个");
        }

        if (Directory.Exists(controllersDir))
        {
            var controllers = Directory.GetFiles(controllersDir, "*.cs", SearchOption.AllDirectories).Length;
            sb.AppendLine($"- 控制器: {controllers} 个");
        }

        sb.AppendLine();
        sb.AppendLine("## 目录结构");
        sb.AppendLine("```");
        
        var allDirs = Directory.GetDirectories(projectDir, "*", SearchOption.TopDirectoryOnly)
            .Select(d => Path.GetRelativePath(projectDir, d))
            .Where(d => !d.Contains("database") && !d.Contains(".git"));

        foreach (var dir in allDirs)
        {
            sb.AppendLine($"{dir}/");
        }
        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// 构建架构文档 Prompt
    /// </summary>
    private string BuildArchitectureDocPrompt(string projectName, string context)
    {
        return $@"为项目 '{projectName}' 生成详细的架构文档。

{context}

## 要求
架构文档应包含：
1. 系统架构概述
2. 组件图/模块划分
3. 数据流说明
4. 设计模式和架构模式
5. 技术选型理由
6. 扩展点说明

请使用 Markdown 格式，可以使用 Mermaid 图表。
输出到 ARCHITECTURE.md 文件。";
    }

    /// <summary>
    /// 查找实体 YAML 文件
    /// </summary>
    private List<string> FindEntityYamlFiles(string projectDir)
    {
        var entitiesDir = Path.Combine(projectDir, "entities");
        if (!Directory.Exists(entitiesDir))
            return new List<string>();

        return Directory.GetFiles(entitiesDir, "*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(entitiesDir, "*.yaml", SearchOption.TopDirectoryOnly))
            .ToList();
    }

    /// <summary>
    /// 构建实体上下文
    /// </summary>
    private async Task<string> BuildEntityContextAsync(
        string projectDir,
        List<string> entityFiles,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 实体定义");
        sb.AppendLine($"共 {entityFiles.Count} 个实体");
        sb.AppendLine();

        foreach (var entityFile in entityFiles.Take(10))
        {
            if (File.Exists(entityFile))
            {
                sb.AppendLine($"## 实体文件: {Path.GetFileName(entityFile)}");
                var content = await File.ReadAllTextAsync(entityFile, ct);
                sb.AppendLine("```yaml");
                sb.AppendLine(content.Length > 800 ? content.Substring(0, 800) + "\n# ... (已截断)" : content);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建实体文档 Prompt
    /// </summary>
    private string BuildEntityDocPrompt(string projectName, string context)
    {
        return $@"为项目 '{projectName}' 生成实体文档。

{context}

## 要求
实体文档应包含：
1. 每个实体的详细说明
2. 字段列表（名称、类型、约束）
3. 实体关系图
4. 使用示例

请使用 Markdown 格式，使用表格展示字段信息。
输出到 ENTITIES.md 文件。";
    }
}

/// <summary>
/// 文档生成结果
/// </summary>
public class DocumentationResult
{
    public string TaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<string> GeneratedFiles { get; set; } = new();
    public string WorkDirectory { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
