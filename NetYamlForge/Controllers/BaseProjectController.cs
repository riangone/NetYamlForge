// ファイル概要: PageController / DynamicEntityController が共有するヘルパーを提供する基底コントローラー。
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace NetYamlForge.Controllers;

public abstract class BaseProjectController : Controller
{
    protected bool UserIsAdmin() => User?.IsInRole("Admin") ?? false;

    protected bool IsHtmxRequest() =>
        Request.Headers.TryGetValue("HX-Request", out var v) && v == "true";

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
