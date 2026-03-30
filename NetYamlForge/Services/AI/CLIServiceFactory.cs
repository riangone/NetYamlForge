using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

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
    
    public async Task<Dictionary<string, CliToolInfo>> GetAvailableToolsAsync()
    {
        var tools = new Dictionary<string, CliToolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in _services.Values)
        {
            try
            {
                var info = await service.GetToolInfoAsync();
                tools[service.ToolName] = info;
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
