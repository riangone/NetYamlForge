namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// 路径解析接口（适配层）
/// 用于解耦 AI 服务对主框架 IWebHostEnvironment 的依赖
/// </summary>
public interface IAIPathResolver
{
    /// <summary>
    /// 获取内容根路径
    /// </summary>
    string GetContentRootPath();

    /// <summary>
    /// 获取项目目录路径
    /// </summary>
    string GetProjectPath(string projectName);

    /// <summary>
    /// 获取 Skills 目录路径
    /// </summary>
    string GetSkillsPath(string projectName);
}
