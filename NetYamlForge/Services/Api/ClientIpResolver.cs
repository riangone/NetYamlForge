using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.Config;

namespace NetYamlForge.Services.Api;

public interface IClientIpResolver
{
    string GetClientIp(HttpContext context);
}

/// <summary>
/// リバースプロキシ（Caddy など）配下でもクライアント実 IP を解決します。
/// ClientIpHeader（既定 X-Forwarded-For）を読み、左端の非ローカル送信元を採用し、
/// TrustedProxies が設定されている場合は中間プロキシ（信頼網内の hop）をスキップします。
/// ヘッダーが無い・空・送信元（RemoteIpAddress）が信頼網外の場合は RemoteIpAddress にフォールバックします。
/// </summary>
public class ClientIpResolver : IClientIpResolver
{
    private const string UnknownIp = "UnknownIP";

    private readonly RateLimitingOptions _options;
    private readonly List<IPNetwork> _trustedProxies;

    public ClientIpResolver(IOptions<RateLimitingOptions> options)
    {
        _options = options.Value;
        _trustedProxies = new List<IPNetwork>();

        foreach (var cidr in _options.TrustedProxies ?? [])
        {
            if (IPNetwork.TryParse(cidr, out var network))
            {
                _trustedProxies.Add(network);
            }
        }
    }

    public string GetClientIp(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        string remoteIp = remote?.ToString() ?? UnknownIp;

        var headerName = _options.ClientIpHeader;
        if (string.IsNullOrWhiteSpace(headerName))
        {
            return remoteIp;
        }

        if (!context.Request.Headers.TryGetValue(headerName, out var headerValues))
        {
            return remoteIp;
        }

        var raw = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return remoteIp;
        }

        // 信頼網が設定されている場合、直接の接続元が信頼網の外であればヘッダーは詐称の恐れがあるので破棄。
        if (_trustedProxies.Count > 0 && (remote == null || !_trustedProxies.Any(p => p.Contains(remote))))
        {
            return remoteIp;
        }

        var hops = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var hop in hops)
        {
            if (!IPAddress.TryParse(hop, out var ip))
            {
                continue;
            }

            // 中間プロキシ（信頼網内の hop）はスキップ。
            if (_trustedProxies.Any(p => p.Contains(ip)))
            {
                continue;
            }

            // ループバック・未指定などのローカルアドレスもスキップし、真の送信元を探す。
            if (IsLocalOrUnspecified(ip))
            {
                continue;
            }

            return ip.ToString();
        }

        return remoteIp;
    }

    private static bool IsLocalOrUnspecified(IPAddress ip)
    {
        return IPAddress.IsLoopback(ip)
            || ip.IsIPv6LinkLocal
            || ip.Equals(IPAddress.Any)
            || ip.Equals(IPAddress.IPv6Any);
    }
}