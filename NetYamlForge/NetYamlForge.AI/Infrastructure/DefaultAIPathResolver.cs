// AI 模块内部适配器 - 默认实现（独立进程模式）

using Microsoft.Extensions.Configuration;

namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// 默认路径解析实现（独立 AI 进程使用）
/// </summary>
public class DefaultAIPathResolver : IAIPathResolver
{
    private readonly IConfiguration _configuration;
    private readonly string _contentRootPath;

    public DefaultAIPathResolver(IConfiguration configuration)
    {
        _configuration = configuration;
        _contentRootPath = _configuration["AI:ContentRootPath"]
            ?? AppDomain.CurrentDomain.BaseDirectory;
    }

    public string GetContentRootPath() => _contentRootPath;

    public string GetProjectPath(string projectName)
    {
        var projectsRoot = _configuration["AI:ProjectsRootPath"]
            ?? Path.Combine(_contentRootPath, "projects");
        return Path.Combine(projectsRoot, projectName);
    }

    public string GetSkillsPath(string projectName)
    {
        var skillsBase = _configuration["AI:SkillsRootPath"]
            ?? Path.Combine(_contentRootPath, "skills");
        return Path.Combine(skillsBase, projectName);
    }
}
