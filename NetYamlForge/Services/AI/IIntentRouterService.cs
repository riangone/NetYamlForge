namespace NetYamlForge.Services.AI;

/// <summary>
/// 声明式意图路由服务契约。
/// 根据意图名称直接路由到对应的目标场景定义与工具执行策略。
/// </summary>
public interface IIntentRouterService
{
    /// <summary>
    /// 解析指定项目的意图定义
    /// </summary>
    IntentConfig? ResolveIntent(string intentName, string projectId);

    /// <summary>
    /// 获取指定项目的全部声明式意图集合
    /// </summary>
    IReadOnlyDictionary<string, IntentConfig> GetIntents(string projectId);

    /// <summary>
    /// 解析意图对应的目标 AI 场景
    /// </summary>
    ScenarioConfig? ResolveTargetScenario(string intentName, string projectId);

    /// <summary>
    /// 获取意图所允许调用的工具白名单
    /// </summary>
    IReadOnlyList<string> ResolveAllowedTools(string intentName, string projectId);
}
