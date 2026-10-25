namespace NetYamlForge.Services.AI.AgentStudio.Manifestation;

/// <summary>
/// 应用物理落盘与自举交付服务契约 (App Manifestation Engine)
/// </summary>
public interface IAppManifestationService
{
    /// <summary>
    /// 解析指定任务的智能体产物，并物理交付到 projects/{projectName} 目录、执行结构校验与热重载挂载
    /// </summary>
    Task<ManifestationResult> ManifestAppAsync(
        ManifestationRequest request,
        CancellationToken cancellationToken = default);
}
