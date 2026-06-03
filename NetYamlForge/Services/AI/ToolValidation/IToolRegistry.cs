using System.Text.Json.Nodes;

namespace NetYamlForge.Services.AI.ToolValidation;

/// <summary>
/// Tool 注册表：管理可执行 Tool 的注册与查找
/// </summary>
public interface IToolRegistry
{
    void Register(ToolDefinition tool);
    ToolDefinition? Get(string toolName);
    IReadOnlyCollection<ToolDefinition> GetAll();
}

/// <summary>
/// 内存 Tool 注册表实现
/// </summary>
public class InMemoryToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Name] = tool;
    }

    public ToolDefinition? Get(string toolName) =>
        _tools.GetValueOrDefault(toolName);

    public IReadOnlyCollection<ToolDefinition> GetAll() =>
        _tools.Values;
}
