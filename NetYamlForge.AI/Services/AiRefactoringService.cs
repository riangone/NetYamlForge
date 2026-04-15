using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Services;

/// <summary>
/// AI 辅助重构服务
/// 使用 Harness AI Pipeline 对现有代码进行重构、优化和改进
/// </summary>
public class AiRefactoringService
{
    private readonly AiPipelineService _pipelineService;
    private readonly ILogger<AiRefactoringService> _logger;

    public AiRefactoringService(
        AiPipelineService pipelineService,
        ILogger<AiRefactoringService> logger)
    {
        _pipelineService = pipelineService;
        _logger = logger;
    }

    /// <summary>
    /// 重构指定的代码文件
    /// </summary>
    /// <param name="filePaths">要重构的文件路径列表</param>
    /// <param name="refactorType">重构类型</param>
    /// <param name="additionalInstructions">额外的重构指令</param>
    /// <param name="timeout">超时秒数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>重构结果</returns>
    public async Task<RefactoringResult> RefactorAsync(
        List<string> filePaths,
        RefactorType refactorType,
        string? additionalInstructions = null,
        int? timeout = null,
        CancellationToken ct = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var workDir = Path.Combine("/tmp/nyf-harness", $"refactor-{taskId}");
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("[AI-Refactor] 开始重构 - TaskId={TaskId}, Files={Count}, Type={Type}",
            taskId, filePaths.Count, refactorType);

        try
        {
            // 准备重构上下文
            var context = await BuildRefactorContextAsync(filePaths, refactorType, additionalInstructions);
            
            // 构建重构 prompt
            var prompt = BuildRefactorPrompt(context, refactorType, additionalInstructions);

            // 执行 AI Pipeline
            var result = await _pipelineService.ExecutePipelineAsync(
                prompt: prompt,
                projectName: "refactoring",
                targetProjectDir: null, // 稍后手动复制
                timeout: timeout,
                ct: ct);

            if (result.Success)
            {
                // 将重构结果复制回原位置
                await ApplyRefactoringAsync(workDir, filePaths, ct);
            }

            return new RefactoringResult
            {
                TaskId = taskId,
                Success = result.Success,
                OriginalFiles = filePaths,
                RefactoredFiles = result.GeneratedFiles,
                WorkDirectory = workDir,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-Refactor] 重构失败 - TaskId={TaskId}", taskId);
            return new RefactoringResult
            {
                TaskId = taskId,
                Success = false,
                OriginalFiles = filePaths,
                WorkDirectory = workDir,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 审查代码质量
    /// </summary>
    public async Task<CodeReviewResult> ReviewCodeAsync(
        List<string> filePaths,
        ReviewType reviewType = ReviewType.Full,
        int? timeout = null,
        CancellationToken ct = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var workDir = Path.Combine("/tmp/nyf-harness", $"review-{taskId}");
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("[AI-Review] 开始代码审查 - TaskId={TaskId}, Files={Count}",
            taskId, filePaths.Count);

        try
        {
            var context = await BuildReviewContextAsync(filePaths);
            var prompt = BuildReviewPrompt(context, reviewType);

            var result = await _pipelineService.ExecutePipelineAsync(
                prompt: prompt,
                projectName: "code-review",
                targetProjectDir: workDir,
                timeout: timeout,
                ct: ct);

            var reviewReport = await ParseReviewReportAsync(workDir, result.GeneratedFiles);

            return new CodeReviewResult
            {
                TaskId = taskId,
                Success = result.Success,
                Report = reviewReport,
                WorkDirectory = workDir,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-Review] 代码审查失败 - TaskId={TaskId}", taskId);
            return new CodeReviewResult
            {
                TaskId = taskId,
                Success = false,
                WorkDirectory = workDir,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 构建重构上下文
    /// </summary>
    private async Task<string> BuildRefactorContextAsync(
        List<string> filePaths, 
        RefactorType refactorType,
        string? additionalInstructions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 重构上下文");
        sb.AppendLine($"重构类型: {refactorType}");
        sb.AppendLine($"文件数量: {filePaths.Count}");
        sb.AppendLine();

        foreach (var filePath in filePaths.Take(10)) // 限制文件数量
        {
            if (File.Exists(filePath))
            {
                sb.AppendLine($"## 文件: {filePath}");
                var content = await File.ReadAllTextAsync(filePath);
                sb.AppendLine("```");
                sb.AppendLine(content.Length > 2000 ? content.Substring(0, 2000) + "\n... (已截断)" : content);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        if (!string.IsNullOrEmpty(additionalInstructions))
        {
            sb.AppendLine("## 额外指令");
            sb.AppendLine(additionalInstructions);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建重构 Prompt
    /// </summary>
    private string BuildRefactorPrompt(string context, RefactorType refactorType, string? additionalInstructions)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("请根据以下上下文进行代码重构。");
        sb.AppendLine();
        sb.AppendLine(context);
        sb.AppendLine();

        sb.AppendLine("## 重构目标");
        switch (refactorType)
        {
            case RefactorType.CleanCode:
                sb.AppendLine("- 提高代码可读性和可维护性");
                sb.AppendLine("- 遵循 SOLID 原则和设计模式");
                sb.AppendLine("- 改进命名和代码结构");
                break;
            case RefactorType.Performance:
                sb.AppendLine("- 优化性能瓶颈");
                sb.AppendLine("- 减少不必要的内存分配");
                sb.AppendLine("- 使用更高效的数据结构和算法");
                break;
            case RefactorType.Security:
                sb.AppendLine("- 修复潜在的安全漏洞");
                sb.AppendLine("- 加强输入验证和输出编码");
                sb.AppendLine("- 遵循安全最佳实践");
                break;
            case RefactorType.Modernize:
                sb.AppendLine("- 使用最新的 C# 语言特性");
                sb.AppendLine("- 采用现代编程范式");
                sb.AppendLine("- 简化冗余代码");
                break;
        }

        sb.AppendLine();
        sb.AppendLine("## 要求");
        sb.AppendLine("- 保持原有功能不变");
        sb.AppendLine("- 添加必要的注释");
        sb.AppendLine("- 确保代码符合工业标准");

        return sb.ToString();
    }

    /// <summary>
    /// 应用重构到原文件
    /// </summary>
    private async Task ApplyRefactoringAsync(string workDir, List<string> originalFiles, CancellationToken ct)
    {
        var refactoredFiles = Directory.GetFiles(workDir, "*.*", SearchOption.AllDirectories);
        
        foreach (var refactoredFile in refactoredFiles)
        {
            if (ct.IsCancellationRequested) break;

            var relativePath = Path.GetRelativePath(workDir, refactoredFile);
            var originalFile = originalFiles.FirstOrDefault(f => 
                f.EndsWith(relativePath, StringComparison.OrdinalIgnoreCase));

            if (originalFile != null)
            {
                // 备份原文件
                var backupFile = originalFile + ".backup";
                File.Copy(originalFile, backupFile, overwrite: true);
                
                // 应用重构
                File.Copy(refactoredFile, originalFile, overwrite: true);
                
                _logger.LogInformation("[AI-Refactor] 已应用重构 - File={File}", originalFile);
            }
        }
    }

    /// <summary>
    /// 构建审查上下文
    /// </summary>
    private async Task<string> BuildReviewContextAsync(List<string> filePaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 代码审查上下文");
        sb.AppendLine($"文件数量: {filePaths.Count}");
        sb.AppendLine();

        foreach (var filePath in filePaths.Take(10))
        {
            if (File.Exists(filePath))
            {
                sb.AppendLine($"## 文件: {filePath}");
                var content = await File.ReadAllTextAsync(filePath);
                sb.AppendLine("```");
                sb.AppendLine(content.Length > 1500 ? content.Substring(0, 1500) + "\n... (已截断)" : content);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建审查 Prompt
    /// </summary>
    private string BuildReviewPrompt(string context, ReviewType reviewType)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("请对以下代码进行全面审查。");
        sb.AppendLine();
        sb.AppendLine(context);
        sb.AppendLine();

        sb.AppendLine("## 审查要求");
        sb.AppendLine("请输出审查报告到 review-report.md 文件，格式如下：");
        sb.AppendLine();
        sb.AppendLine("```markdown");
        sb.AppendLine("# 代码审查报告");
        sb.AppendLine();
        sb.AppendLine("## 总体评价");
        sb.AppendLine("（对代码质量的整体评分 1-10 分）");
        sb.AppendLine();
        sb.AppendLine("## 发现的问题");
        sb.AppendLine("- **问题 1**: [严重程度] 问题描述 + 具体位置 + 改进建议");
        sb.AppendLine("- **问题 2**: ...");
        sb.AppendLine();
        sb.AppendLine("## 改进建议");
        sb.AppendLine("1. ...");
        sb.AppendLine("2. ...");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("请确保在文件第一行写明 VERDICT: PASS 或 VERDICT: FAIL。");

        return sb.ToString();
    }

    /// <summary>
    /// 解析审查报告
    /// </summary>
    private async Task<string> ParseReviewReportAsync(string workDir, List<string> generatedFiles)
    {
        var reportFile = Path.Combine(workDir, "review-report.md");
        if (File.Exists(reportFile))
        {
            return await File.ReadAllTextAsync(reportFile);
        }

        // 如果找不到报告文件，尝试从生成的文件中查找
        foreach (var file in generatedFiles)
        {
            if (file.Contains("review", StringComparison.OrdinalIgnoreCase) || 
                file.Contains("report", StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = Path.Combine(workDir, file);
                if (File.Exists(fullPath))
                {
                    return await File.ReadAllTextAsync(fullPath);
                }
            }
        }

        return "未找到审查报告文件";
    }
}

/// <summary>
/// 重构类型
/// </summary>
public enum RefactorType
{
    /// <summary>
    /// 清理代码（提高可读性）
    /// </summary>
    CleanCode,

    /// <summary>
    /// 性能优化
    /// </summary>
    Performance,

    /// <summary>
    /// 安全加固
    /// </summary>
    Security,

    /// <summary>
    /// 现代化改造（使用新特性）
    /// </summary>
    Modernize
}

/// <summary>
/// 审查类型
/// </summary>
public enum ReviewType
{
    /// <summary>
    /// 全面审查
    /// </summary>
    Full,

    /// <summary>
    /// 安全检查
    /// </summary>
    Security,

    /// <summary>
    /// 性能检查
    /// </summary>
    Performance,

    /// <summary>
    /// 代码规范检查
    /// </summary>
    CodeStyle
}

/// <summary>
/// 重构结果
/// </summary>
public class RefactoringResult
{
    public string TaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public List<string> OriginalFiles { get; set; } = new();
    public List<string> RefactoredFiles { get; set; } = new();
    public string WorkDirectory { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 代码审查结果
/// </summary>
public class CodeReviewResult
{
    public string TaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Report { get; set; } = string.Empty;
    public string WorkDirectory { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
