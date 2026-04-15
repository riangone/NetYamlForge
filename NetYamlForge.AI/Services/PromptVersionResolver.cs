using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Services;

/// <summary>
/// Prompt 版本解析器
/// 
/// 功能:
/// 1. 基于 SessionId 哈希分配 Prompt 版本
/// 2. 支持 AB 测试流量分配
/// 3. 会话级配置快照隔离
/// </summary>
public class PromptVersionResolver
{
    private readonly PromptHotReloadOptions _options;
    private readonly ILogger<PromptVersionResolver> _logger;

    // 会话级版本缓存(会话生命周期内不变)
    private readonly ConcurrentDictionary<string, string> _sessionVersions = new();

    public PromptVersionResolver(
        IOptions<PromptHotReloadOptions> options,
        ILogger<PromptVersionResolver> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 解析会话应使用的 Prompt 版本
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>Prompt 版本路径(如 "v1", "v2")</returns>
    public string ResolveVersion(string sessionId)
    {
        // 会话级缓存:确保同一会话始终使用同一版本
        if (_sessionVersions.TryGetValue(sessionId, out var version))
        {
            return version;
        }

        // AB 测试流量分配
        if (_options.AbTest.Enabled)
        {
            version = AssignAbTestVersion(sessionId);
        }
        else
        {
            // 默认使用当前版本
            version = _options.CurrentVersion;
        }

        // 缓存会话版本
        _sessionVersions.TryAdd(sessionId, version);

        _logger.LogInformation(
            "[PromptVersionResolver] 会话 {SessionId} 分配版本 {Version}",
            sessionId,
            version);

        return version;
    }

    /// <summary>
    /// AB 测试版本分配
    /// </summary>
    private string AssignAbTestVersion(string sessionId)
    {
        // 基于 SessionId 哈希分配
        var hash = ComputeSessionHash(sessionId);
        var ratio = hash % 100;

        return ratio < _options.AbTest.TrafficSplit
            ? _options.AbTest.VariantB
            : _options.AbTest.VariantA;
    }

    /// <summary>
    /// 计算会话哈希(0-99)
    /// </summary>
    private static uint ComputeSessionHash(string sessionId)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        return BitConverter.ToUInt32(hashBytes, 0) % 100;
    }

    /// <summary>
    /// 会话结束时清理版本缓存
    /// </summary>
    public void ClearSessionVersion(string sessionId)
    {
        _sessionVersions.TryRemove(sessionId, out _);
    }
}

/// <summary>
/// Prompt 热重载配置
/// </summary>
public class PromptHotReloadOptions
{
    public const string SectionName = "AI:Prompt";

    /// <summary>
    /// 当前 Prompt 版本
    /// </summary>
    public string CurrentVersion { get; set; } = "v1";

    /// <summary>
    /// 是否允许热重载
    /// </summary>
    public bool AllowHotReload { get; set; } = true;

    /// <summary>
    /// 防抖延迟(毫秒)
    /// </summary>
    public int ReloadDebounceMs { get; set; } = 500;

    /// <summary>
    /// AB 测试配置
    /// </summary>
    public AbTestOptions AbTest { get; set; } = new();
}

/// <summary>
/// AB 测试配置
/// </summary>
public class AbTestOptions
{
    /// <summary>
    /// 是否启用 AB 测试
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 变体 A 版本
    /// </summary>
    public string VariantA { get; set; } = "v1";

    /// <summary>
    /// 变体 B 版本
    /// </summary>
    public string VariantB { get; set; } = "v2";

    /// <summary>
    /// 变体 B 流量比例(0-100)
    /// </summary>
    public int TrafficSplit { get; set; } = 50;
}
