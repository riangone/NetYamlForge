using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;
using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 进程池装饰器 CLI 服务
/// 通过装饰器模式为现有 CLI 服务添加进程池支持
/// </summary>
public class PooledCLIService : ICLIService
{
    private readonly ICLIService _innerService;
    private readonly AIProcessPoolManager _poolManager;
    private readonly ProcessExecutor _executor;
    private readonly CliProcessPoolConfig _poolConfig;
    private readonly DaemonChatServiceFactory _daemonFactory;
    private readonly ILogger _logger;

    public PooledCLIService(
        ICLIService innerService,
        AIProcessPoolManager poolManager,
        ProcessExecutor executor,
        CliProcessPoolConfig poolConfig,
        DaemonChatServiceFactory daemonFactory,
        ILogger logger)
    {
        _innerService = innerService;
        _poolManager = poolManager;
        _executor = executor;
        _poolConfig = poolConfig;
        _daemonFactory = daemonFactory;
        _logger = logger;
    }

    public virtual string ToolName => _innerService.ToolName;

    public async Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        return await _innerService.GetToolInfoAsync(ct);
    }

    public async IAsyncEnumerable<ProgressUpdate> ExecuteStreamingAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 流式执行使用 DaemonChatService（支持常驻进程）
        var daemonService = _daemonFactory.GetService(this);
        await foreach (var update in daemonService.ChatStreamingAsync(
            message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct))
        {
            yield return update;
        }
    }

    public async Task<string> ExecuteAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default)
    {
        // 如果未启用进程池模式，直接调用内部服务
        if (!_poolConfig.EnableDaemonMode)
        {
            return await _innerService.ExecuteAsync(
                message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct);
        }

        PersistentAIProcess? process = null;
        bool shouldReturnToPool = false;

        try
        {
            // 从进程池获取进程
            process = await _poolManager.AcquireProcessAsync(
                ToolName,
                async () => await CreateNewProcessAsync(
                    message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct),
                ct);

            shouldReturnToPool = true;

            _logger.LogDebug(
                "[进程池] 执行请求: Provider={Provider}, Message={Message}",
                ToolName, message.Substring(0, Math.Min(50, message.Length)));

            // 使用 DaemonChatService 执行（支持常驻进程复用）
            var daemonService = _daemonFactory.GetService(this);
            var result = await daemonService.ChatAsync(
                message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[进程池] 执行异常: Provider={Provider}, Error={Error}",
                ToolName, ex.Message);

            // 异常时不归还进程到池
            shouldReturnToPool = false;
            process?.Dispose();

            throw;
        }
        finally
        {
            // 将进程归还到池中
            if (shouldReturnToPool && process != null)
            {
                _poolManager.ReturnProcess(ToolName, process);
            }
        }
    }

    public async Task CancelAsync(int processId, CancellationToken ct = default)
    {
        await _innerService.CancelAsync(processId, ct);
    }

    /// <summary>
    /// 创建新的持久化进程
    /// </summary>
    private async Task<PersistentAIProcess> CreateNewProcessAsync(
        string message,
        string? workingDirectory,
        string? sessionId,
        List<string>? allowedTools,
        string? systemPromptOverride,
        CancellationToken ct)
    {
        var process = new PersistentAIProcess(ToolName, _poolConfig, _logger);

        // 构建守护进程参数（需要 CLI 支持 --daemon 模式）
        var arguments = BuildDaemonArguments(message, sessionId, allowedTools, systemPromptOverride);

        // 这里需要获取实际 CLI 服务的信息来启动进程
        // 简化处理：直接返回进程对象，实际启动由调用者处理
        _logger.LogInformation(
            "[进程池] 准备创建进程: Provider={Provider}, Arguments={Args}",
            ToolName, string.Join(" ", arguments));

        return process;
    }

    /// <summary>
    /// 构建守护进程参数
    /// </summary>
    private List<string> BuildDaemonArguments(
        string message,
        string? sessionId,
        List<string>? allowedTools,
        string? systemPromptOverride)
    {
        var args = new List<string>();

        // 不同 CLI 工具的守护进程参数不同
        // Qwen Code: --daemon --output-format json
        // Claude Code: --daemon --output-format json

        switch (ToolName.ToLowerInvariant())
        {
            case "qwen":
            case "qwen-code":
                args.Add("--daemon");
                args.Add("--output-format");
                args.Add("json");
                if (!string.IsNullOrEmpty(sessionId))
                {
                    args.Add("--session-id");
                    args.Add(sessionId);
                }
                break;

            case "claude":
            case "claude-code":
                args.Add("--daemon");
                args.Add("--output-format");
                args.Add("json");
                if (!string.IsNullOrEmpty(sessionId))
                {
                    args.Add("--session-id");
                    args.Add(sessionId);
                }
                break;

            default:
                _logger.LogWarning(
                    "[进程池] 未知的 CLI 工具，不支持守护进程模式: {ToolName}",
                    ToolName);
                break;
        }

        // 添加允许的工具
        if (allowedTools != null && allowedTools.Count > 0)
        {
            args.Add("--allowed-tools");
            args.Add(string.Join(",", allowedTools));
        }

        // 添加系统提示覆盖
        if (!string.IsNullOrEmpty(systemPromptOverride))
        {
            args.Add("--system-prompt");
            args.Add(systemPromptOverride);
        }

        return args;
    }
}
