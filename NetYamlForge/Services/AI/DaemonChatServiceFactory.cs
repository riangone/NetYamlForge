using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI;

/// <summary>
/// DaemonChatService 工厂
/// 根据不同的 CLI provider 动态创建对应的 DaemonChatService 实例
/// </summary>
public class DaemonChatServiceFactory
{
    private readonly ProcessExecutor _executor;
    private readonly CliConfig _config;
    private readonly SkillLoader _skillLoader;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, DaemonChatService> _instances = new();
    private readonly object _lock = new();

    public DaemonChatServiceFactory(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILoggerFactory loggerFactory)
    {
        _executor = executor;
        _config = config.Value;
        _skillLoader = skillLoader;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 获取或创建指定 provider 的 DaemonChatService
    /// </summary>
    public DaemonChatService GetService(ICLIService cliService)
    {
        var provider = cliService.ToolName;

        lock (_lock)
        {
            if (_instances.TryGetValue(provider, out var service))
            {
                return service;
            }

            var logger = _loggerFactory.CreateLogger<DaemonChatService>();
            service = new DaemonChatService(
                cliService,
                Options.Create(_config),
                _executor,
                _skillLoader,
                logger);

            _instances[provider] = service;
            return service;
        }
    }

    /// <summary>
    /// 清空所有实例
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var kvp in _instances)
            {
                kvp.Value.Dispose();
            }
            _instances.Clear();
        }
    }
}
