using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace NetYamlForge.Services.Api;

public static class RateLimitPartitioners
{
    private const string UnknownIp = "UnknownIP";
    private const string Anonymous = "anonymous";
    private const string DefaultTenant = "DefaultTenant";

    /// <summary>
    /// "IP,User" 形式の LimitBy から複合分区キーを組み立てます。
    /// 要素は CSV の出現順に、IP→"ip"、User→"user" として "|" 区切りで連結します。
    /// 重複要素（例: "IP,IP"）は一度しか数えません。空文字は "IP" として扱います。
    /// </summary>
    public static string BuildCompositeKey(string group, string limitByCsv, string ip, string? user)
        => BuildCompositeKey(group, limitByCsv, ip, user, null);

    /// <summary>
    /// Tenant 分区を含む複合分区キーの組み立て（Dynamic エンティティ向け）。
    /// 要素は CSV の出現順に IP→"ip"、User→"user"、Tenant→"tenant" として連結します。
    /// </summary>
    public static string BuildCompositeKey(string group, string limitByCsv, string ip, string? user, string? tenant)
    {
        var elements = string.IsNullOrWhiteSpace(limitByCsv)
            ? new[] { "IP" }
            : limitByCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var parts = new List<string> { group };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in elements)
        {
            if (!seen.Add(element))
            {
                continue;
            }

            if (string.Equals(element, "IP", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(string.IsNullOrWhiteSpace(ip) ? UnknownIp : ip);
            }
            else if (string.Equals(element, "User", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(string.IsNullOrWhiteSpace(user) ? Anonymous : user);
            }
            else if (string.Equals(element, "Tenant", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(string.IsNullOrWhiteSpace(tenant) ? DefaultTenant : tenant);
            }
        }

        return string.Join("|", parts);
    }

    /// <summary>
    /// LimitBy に指定の要素（IP / User / Tenant）が含まれるかを確認します。
    /// </summary>
    public static bool ContainsLimitByElement(string limitByCsv, string element)
    {
        if (string.IsNullOrWhiteSpace(limitByCsv))
        {
            return false;
        }

        return limitByCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, element, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 现在的プリンシパルが認証済みなら identifying name、それ以外はフォームのユーザー名（無ければ "anonymous"）を返します。
    /// </summary>
    public static string ResolveUser(HttpContext ctx, string? formUsername)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var name = ctx.User.Identity.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return string.IsNullOrWhiteSpace(formUsername) ? Anonymous : formUsername;
    }
}