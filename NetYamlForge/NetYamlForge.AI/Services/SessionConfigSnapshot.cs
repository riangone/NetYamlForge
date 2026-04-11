namespace NetYamlForge.AI.Services;

/// <summary>
/// 会话级配置快照
/// 
/// 功能:
/// 1. 会话创建时捕获当前 Prompt 版本、Tool 定义、系统提示词
/// 2. 会话生命周期内配置不变,避免热重载中断
/// 3. 会话结束时快照自动释放
/// </summary>
public class SessionConfigSnapshot : IDisposable
{
    private readonly string _sessionId;
    private readonly string _promptVersion;
    private readonly string _systemPrompt;
    private readonly Dictionary<string, object> _toolDefinitions;
    private readonly DateTime _capturedAt;
    private bool _disposed;

    public SessionConfigSnapshot(
        string sessionId,
        string promptVersion,
        string systemPrompt,
        Dictionary<string, object> toolDefinitions)
    {
        _sessionId = sessionId;
        _promptVersion = promptVersion;
        _systemPrompt = systemPrompt;
        _toolDefinitions = toolDefinitions;
        _capturedAt = DateTime.UtcNow;
    }

    public string SessionId => _sessionId;
    public string PromptVersion => _promptVersion;
    public string SystemPrompt => _systemPrompt;
    public IReadOnlyDictionary<string, object> ToolDefinitions => _toolDefinitions;
    public DateTime CapturedAt => _capturedAt;

    /// <summary>
    /// 获取系统提示词(会话生命周期内不变)
    /// </summary>
    public string GetSystemPrompt() => _systemPrompt;

    /// <summary>
    /// 获取 Tool 定义(会话生命周期内不变)
    /// </summary>
    public object? GetToolDefinition(string toolName)
    {
        return _toolDefinitions.TryGetValue(toolName, out var def) ? def : null;
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 清理资源
        _toolDefinitions.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
