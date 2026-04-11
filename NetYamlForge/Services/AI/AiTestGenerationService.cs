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
/// AI 自动化测试生成服务
/// 使用 Harness AI Pipeline 为现有代码生成单元测试、集成测试等
/// </summary>
public class AiTestGenerationService
{
    private readonly AiPipelineService _pipelineService;
    private readonly ILogger<AiTestGenerationService> _logger;

    public AiTestGenerationService(
        AiPipelineService pipelineService,
        ILogger<AiTestGenerationService> logger)
    {
        _pipelineService = pipelineService;
        _logger = logger;
    }

    /// <summary>
    /// 为指定代码文件生成测试
    /// </summary>
    /// <param name="sourceFiles">源代码文件路径列表</param>
    /// <param name="testType">测试类型</param>
    /// <param name="testFramework">测试框架</param>
    /// <param name="outputDir">测试输出目录</param>
    /// <param name="timeout">超时秒数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>测试生成结果</returns>
    public async Task<TestGenerationResult> GenerateTestsAsync(
        List<string> sourceFiles,
        TestType testType = TestType.Unit,
        TestFramework testFramework = TestFramework.XUnit,
        string? outputDir = null,
        int? timeout = null,
        CancellationToken ct = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var workDir = Path.Combine("/tmp/nyf-harness", $"test-gen-{taskId}");
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("[AI-TestGen] 开始生成测试 - TaskId={TaskId}, Files={Count}, Type={Type}",
            taskId, sourceFiles.Count, testType);

        try
        {
            // 构建测试生成上下文
            var context = await BuildTestContextAsync(sourceFiles, testType, testFramework);
            var prompt = BuildTestPrompt(context, testType, testFramework);

            // 执行 AI Pipeline
            var result = await _pipelineService.ExecutePipelineAsync(
                prompt: prompt,
                projectName: "test-generation",
                targetProjectDir: outputDir ?? workDir,
                timeout: timeout,
                ct: ct);

            // 验证生成的测试
            var validation = await ValidateGeneratedTestsAsync(workDir, testFramework);

            return new TestGenerationResult
            {
                TaskId = taskId,
                Success = result.Success,
                GeneratedTests = result.GeneratedFiles,
                WorkDirectory = workDir,
                ValidationReport = validation,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-TestGen] 测试生成失败 - TaskId={TaskId}", taskId);
            return new TestGenerationResult
            {
                TaskId = taskId,
                Success = false,
                WorkDirectory = workDir,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 为实体生成 CRUD 测试
    /// </summary>
    public async Task<TestGenerationResult> GenerateEntityTestsAsync(
        string projectName,
        string entityName,
        TestFramework testFramework = TestFramework.XUnit,
        int? timeout = null,
        CancellationToken ct = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var workDir = Path.Combine("/tmp/nyf-harness", $"entity-test-{taskId}");
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("[AI-TestGen] 开始生成实体测试 - TaskId={TaskId}, Entity={Entity}",
            taskId, entityName);

        var prompt = $@"为 NetYamlForge 项目的实体 '{entityName}' 生成完整的测试。

## 要求
- 测试框架: {testFramework}
- 测试类型: 单元测试 + 集成测试
- 覆盖所有 CRUD 操作
- 包含边界条件测试
- 包含错误处理测试

## 实体信息
项目名称: {projectName}
实体名称: {entityName}

请生成测试文件到当前工作目录。";

        var result = await _pipelineService.ExecutePipelineAsync(
            prompt: prompt,
            projectName: projectName,
            targetProjectDir: workDir,
            timeout: timeout,
            ct: ct);

        return new TestGenerationResult
        {
            TaskId = taskId,
            Success = result.Success,
            GeneratedTests = result.GeneratedFiles,
            WorkDirectory = workDir,
            ErrorMessage = result.ErrorMessage
        };
    }

    /// <summary>
    /// 构建测试上下文
    /// </summary>
    private async Task<string> BuildTestContextAsync(
        List<string> sourceFiles, 
        TestType testType,
        TestFramework testFramework)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 测试生成上下文");
        sb.AppendLine($"测试类型: {testType}");
        sb.AppendLine($"测试框架: {testFramework}");
        sb.AppendLine($"源代码文件数: {sourceFiles.Count}");
        sb.AppendLine();

        foreach (var file in sourceFiles.Take(10)) // 限制文件数量
        {
            if (File.Exists(file))
            {
                sb.AppendLine($"## 源文件: {file}");
                var content = await File.ReadAllTextAsync(file);
                sb.AppendLine("```csharp");
                sb.AppendLine(content.Length > 2000 ? content.Substring(0, 2000) + "\n// ... (已截断)" : content);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## 测试要求");
        switch (testType)
        {
            case TestType.Unit:
                sb.AppendLine("- 单元测试：测试单个类/方法的逻辑");
                sb.AppendLine("- 使用 Mock 隔离外部依赖");
                sb.AppendLine("- 覆盖正常和异常路径");
                break;
            case TestType.Integration:
                sb.AppendLine("- 集成测试：测试多个组件的协作");
                sb.AppendLine("- 使用真实的数据库连接");
                sb.AppendLine("- 验证端到端流程");
                break;
            case TestType.E2E:
                sb.AppendLine("- 端到端测试：模拟用户操作");
                sb.AppendLine("- 测试完整的 HTTP 请求/响应循环");
                sb.AppendLine("- 验证 UI 和业务逻辑");
                break;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建测试生成 Prompt
    /// </summary>
    private string BuildTestPrompt(string context, TestType testType, TestFramework testFramework)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("请根据以下上下文生成高质量的测试代码。");
        sb.AppendLine();
        sb.AppendLine(context);
        sb.AppendLine();

        sb.AppendLine("## 测试框架");
        sb.AppendLine($"使用 {testFramework} 测试框架");
        sb.AppendLine();

        sb.AppendLine("## 命名约定");
        sb.AppendLine("- 测试类命名: `被测类名Tests` (例如: UserServiceTests)");
        sb.AppendLine("- 测试方法命名: `MethodName_Scenario_ExpectedBehavior` (例如: GetUser_WithValidId_ReturnsUser)");
        sb.AppendLine();

        sb.AppendLine("## 测试覆盖要求");
        sb.AppendLine("- 至少覆盖所有公共方法");
        sb.AppendLine("- 包含正常路径测试");
        sb.AppendLine("- 包含异常路径测试（空值、无效输入等）");
        sb.AppendLine("- 包含边界条件测试");
        sb.AppendLine();

        sb.AppendLine("## 输出要求");
        sb.AppendLine("- 每个测试文件对应一个被测类");
        sb.AppendLine("- 包含必要的 using 语句");
        sb.AppendLine("- 添加测试说明注释");
        sb.AppendLine("- 确保测试可以编译和运行");

        return sb.ToString();
    }

    /// <summary>
    /// 验证生成的测试
    /// </summary>
    private async Task<string> ValidateGeneratedTestsAsync(string workDir, TestFramework testFramework)
    {
        var testFiles = Directory.GetFiles(workDir, "*Tests.cs", SearchOption.AllDirectories);
        
        if (testFiles.Length == 0)
        {
            return "警告: 未找到测试文件（匹配 *Tests.cs 模式）";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# 测试验证报告");
        sb.AppendLine();
        sb.AppendLine($"## 生成的测试文件: {testFiles.Length} 个");
        sb.AppendLine();

        foreach (var testFile in testFiles)
        {
            var relativePath = Path.GetRelativePath(workDir, testFile);
            var content = await File.ReadAllTextAsync(testFile);
            var lineCount = content.Split('\n').Length;
            var testMethods = CountTestMethods(content);

            sb.AppendLine($"- {relativePath}: {lineCount} 行, {testMethods} 个测试方法");
        }

        sb.AppendLine();
        sb.AppendLine("## 建议");
        sb.AppendLine("1. 运行 `dotnet test` 验证测试是否通过");
        sb.AppendLine("2. 检查测试覆盖率报告");
        sb.AppendLine("3. 根据实际需要调整测试用例");

        return sb.ToString();
    }

    /// <summary>
    /// 计算测试方法数量
    /// </summary>
    private int CountTestMethods(string content)
    {
        var count = 0;
        var lines = content.Split('\n');
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("[Fact]") || trimmed.Contains("[Theory") || 
                trimmed.Contains("[TestMethod]"))
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>
/// 测试类型
/// </summary>
public enum TestType
{
    /// <summary>
    /// 单元测试
    /// </summary>
    Unit,

    /// <summary>
    /// 集成测试
    /// </summary>
    Integration,

    /// <summary>
    /// 端到端测试
    /// </summary>
    E2E
}

/// <summary>
/// 测试框架
/// </summary>
public enum TestFramework
{
    /// <summary>
    /// xUnit
    /// </summary>
    XUnit,

    /// <summary>
    /// NUnit
    /// </summary>
    NUnit,

    /// <summary>
    /// MSTest
    /// </summary>
    MSTest
}

/// <summary>
/// 测试生成结果
/// </summary>
public class TestGenerationResult
{
    public string TaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public List<string> GeneratedTests { get; set; } = new();
    public string WorkDirectory { get; set; } = string.Empty;
    public string? ValidationReport { get; set; }
    public string? ErrorMessage { get; set; }
}
