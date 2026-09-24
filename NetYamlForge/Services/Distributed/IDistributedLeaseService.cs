namespace NetYamlForge.Services.Distributed;

/// <summary>
/// 分布式租约服务，提供跨实例/跨进程的高可用租约抢占、续期与互斥控制
/// </summary>
public interface IDistributedLeaseService
{
    /// <summary>
    /// 尝试抢占或获取指定名称的分布式租约
    /// </summary>
    Task<bool> TryAcquireLeaseAsync(string leaseName, string holderId, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 续期当前持有的租约
    /// </summary>
    Task<bool> RenewLeaseAsync(string leaseName, string holderId, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 主动释放持有的租约
    /// </summary>
    Task ReleaseLeaseAsync(string leaseName, string holderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前租约信息
    /// </summary>
    Task<DistributedLeaseInfo?> GetLeaseAsync(string leaseName, CancellationToken cancellationToken = default);
}

/// <summary>
/// 租约元数据描述
/// </summary>
public class DistributedLeaseInfo
{
    public string LeaseName { get; set; } = string.Empty;
    public string HolderId { get; set; } = string.Empty;
    public DateTime AcquiredAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}
