using System.Text.Json.Nodes;
using System.Linq;

namespace NetYamlForge.Services.AI.ToolValidation;

/// <summary>
/// Tool 注册表：管理可执行 Tool 的注册与查找（支持租户隔离）
/// </summary>
public interface IToolRegistry
{
    void Register(string projectId, ToolDefinition tool);
    ToolDefinition? Get(string projectId, string toolName);
    IReadOnlyCollection<ToolDefinition> GetAll(string projectId);
}

/// <summary>
/// 内存 Tool 注册表实现，按 projectId 进行隔离存储
/// </summary>
public class InMemoryToolRegistry : IToolRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, ToolDefinition>> _tenantTools = 
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string projectId, ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var resolvedProjectId = string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;
        
        var tools = _tenantTools.GetOrAdd(resolvedProjectId, _ => 
            new System.Collections.Concurrent.ConcurrentDictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase));
        
        tools[tool.Name] = tool;
    }

    public ToolDefinition? Get(string projectId, string toolName)
    {
        var resolvedProjectId = string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;
        
        if (_tenantTools.TryGetValue(resolvedProjectId, out var tools))
        {
            return tools.GetValueOrDefault(toolName);
        }
        return null;
    }

    public IReadOnlyCollection<ToolDefinition> GetAll(string projectId)
    {
        var resolvedProjectId = string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;
        
        if (_tenantTools.TryGetValue(resolvedProjectId, out var tools))
        {
            return tools.Values.ToList();
        }
        return Array.Empty<ToolDefinition>();
    }
}
