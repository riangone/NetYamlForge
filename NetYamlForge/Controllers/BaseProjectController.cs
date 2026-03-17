// ファイル概要: 全コントローラーが共有するヘルパーを提供する基底コントローラー。
using NetYamlForge.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace NetYamlForge.Controllers;

public abstract class BaseProjectController : Controller
{
    protected bool UserIsAdmin() => User?.IsInRole("Admin") ?? false;

    protected bool IsHtmxRequest() =>
        Request.Headers.TryGetValue("HX-Request", out var v) && v == "true";

    /// <summary>エンティティが非公開かつ Admin でない場合に Forbid を返す。問題なければ null。</summary>
    protected IActionResult? RejectIfNotVisible(EntityDefinition meta) =>
        meta.IsPublic || UserIsAdmin() ? null : Forbid();

    /// <summary>クエリ重複時（例: entity=a&amp;entity=a → "a,a"）を吸収して先頭値を使う。</summary>
    protected static string? NormalizeSingleValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var first = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                       .FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? raw : first;
    }

    /// <summary>HTMX リクエストの HX-Current-URL ヘッダーから現在のフィルターパラメータを復元する。</summary>
    protected IDictionary<string, string> GetFiltersFromHtmxCurrentUrl()
    {
        var currentUrl = Request.Headers["HX-Current-URL"].FirstOrDefault();
        if (string.IsNullOrEmpty(currentUrl))
            return new Dictionary<string, string>();
        try
        {
            var uri = new Uri(currentUrl);
            var parsed = QueryHelpers.ParseQuery(uri.Query);
            return parsed.ToDictionary(k => k.Key, v => v.Value.ToString());
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
