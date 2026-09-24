using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 声明式意图路由服务实现。
/// 基于 IAiScenarioYamlLoader 提供的 YAML 元数据，进行意图到场景与工具集的动态路由。
/// </summary>
public class IntentRouterService : IIntentRouterService
{
    private readonly IAiScenarioYamlLoader _scenarioLoader;
    private readonly ILogger<IntentRouterService> _logger;

    public IntentRouterService(
        IAiScenarioYamlLoader scenarioLoader,
        ILogger<IntentRouterService>? logger = null)
    {
        _scenarioLoader = scenarioLoader;
        _logger = logger ?? NullLogger<IntentRouterService>.Instance;
    }

    public IntentConfig? ResolveIntent(string intentName, string projectId)
    {
        if (string.IsNullOrWhiteSpace(intentName) || string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var config = _scenarioLoader.GetConfig(projectId);
        if (config?.Intents != null && config.Intents.TryGetValue(intentName, out var intent))
        {
            return intent;
        }

        _logger.LogDebug("[IntentRouterService] Intent '{Intent}' not found in project '{Project}'", intentName, projectId);
        return null;
    }

    public IReadOnlyDictionary<string, IntentConfig> GetIntents(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return new Dictionary<string, IntentConfig>();
        }

        var config = _scenarioLoader.GetConfig(projectId);
        return config?.Intents ?? new Dictionary<string, IntentConfig>();
    }

    public ScenarioConfig? ResolveTargetScenario(string intentName, string projectId)
    {
        var intent = ResolveIntent(intentName, projectId);
        if (intent == null || string.IsNullOrWhiteSpace(intent.TargetScenario))
        {
            return null;
        }

        var config = _scenarioLoader.GetConfig(projectId);
        if (config?.Scenarios != null && config.Scenarios.TryGetValue(intent.TargetScenario, out var scenario))
        {
            return scenario;
        }

        return null;
    }

    public IReadOnlyList<string> ResolveAllowedTools(string intentName, string projectId)
    {
        var intent = ResolveIntent(intentName, projectId);
        if (intent != null && intent.AllowedTools.Count > 0)
        {
            return intent.AllowedTools;
        }

        // 若意图本身未单独限定工具，则尝试从目标场景的初始状态中提取工具集
        var scenario = ResolveTargetScenario(intentName, projectId);
        if (scenario != null && !string.IsNullOrWhiteSpace(scenario.InitialState) &&
            scenario.Tools.TryGetValue(scenario.InitialState, out var scenarioTools))
        {
            return scenarioTools;
        }

        return Array.Empty<string>();
    }
}
