using System;

namespace NetYamlForge.Exceptions;

/// <summary>
/// 当请求未显式提供租户/项目上下文（projectId），且当前执行环境（ProjectScope）亦未设定租户上下文时抛出的强类型安全异常。
/// 遵循 Safe-by-Construction 与 Fail-Closed（默认拒绝）原则，防止跨租户上下文丢失与数据穿透。
/// </summary>
public class TenantContextMissingException : InvalidOperationException
{
    public TenantContextMissingException()
        : base("Tenant context is missing: projectId was not specified and ProjectScope is not set (fail-closed guardrail).")
    {
    }

    public TenantContextMissingException(string message)
        : base(message)
    {
    }

    public TenantContextMissingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
