using System.Text.Json.Nodes;
using Json.Schema;

namespace NetYamlForge.Services.AI.ToolValidation;

/// <summary>
/// Tool 定义类
/// </summary>
public class ToolDefinition
{
    /// <summary>
    /// Tool 名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Tool 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 参数 JSON Schema
    /// </summary>
    public JsonSchema? ParameterSchema { get; set; }

    /// <summary>
    /// 允许的状态列表
    /// </summary>
    public HashSet<string> AllowedStates { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 执行函数
    /// </summary>
    public Func<JsonNode, Task<ToolCallResult>>? ExecuteAsync { get; set; }
}

/// <summary>
/// Tool 调用结果
/// </summary>
public class ToolCallResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Data { get; set; }

    public static ToolCallResult Success(object? data = null) =>
        new() { IsSuccess = true, Data = data };

    public static ToolCallResult Fail(string message) =>
        new() { IsSuccess = false, ErrorMessage = message };
}
