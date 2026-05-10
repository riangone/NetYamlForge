using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

/// <summary>
/// CLI 服务工厂
/// </summary>
public class CLIServiceFactory
{
    private readonly Dictionary<string, ICLIService> _services;
    private readonly ILogger<CLIServiceFactory>? _logger;
    
    public CLIServiceFactory(IEnumerable<ICLIService> services, ILogger<CLIServiceFactory>? logger = null)
    {
        _services = services.ToDictionary(s => s.ToolName, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }
    
    public ICLIService GetService(string toolName)
    {
        if (!_services.TryGetValue(toolName, out var service))
        {
            throw new InvalidOperationException($"Unknown CLI tool: {toolName}");
        }
        return service;
    }

    public ICLIService? TryGetService(string toolName)
    {
        _services.TryGetValue(toolName, out var service);
        return service;
    }
    
    public async Task<Dictionary<string, CliToolInfo>> GetAvailableToolsAsync(string? selectedTool = null)
    {
        var tools = new Dictionary<string, CliToolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in _services.Values)
        {
            try
            {
                // 如果是选中的工具，或者是 gemini（默认），则进行真实检查。
                // 否则，为了加速初始加载，返回假设已安装的状态（后续在发送请求时会再次校验）。
                if (selectedTool == null || service.ToolName.Equals(selectedTool, StringComparison.OrdinalIgnoreCase))
                {
                    var info = await service.GetToolInfoAsync();
                    tools[service.ToolName] = info;
                }
                else
                {
                    tools[service.ToolName] = new CliToolInfo
                    {
                        Name = service.ToolName,
                        DisplayName = service.ToolName,
                        Installed = true,      // 预设为 true，加速显示
                        Authenticated = true,
                        Capabilities = new() { "Chat" }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to get tool info for {ToolName}", service.ToolName);
                tools[service.ToolName] = new CliToolInfo
                {
                    Name = service.ToolName,
                    DisplayName = service.ToolName,
                    Installed = false,
                    Authenticated = false
                };
            }
        }
        return tools;
    }
}
