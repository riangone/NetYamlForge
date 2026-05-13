// ファイル概要：プロジェクト固有のビューを projects/{project}/views/ から検索する
// IViewLocationExpander 実装です。

using Microsoft.AspNetCore.Mvc.Razor;

namespace NetYamlForge.Services;

/// <summary>
/// プロジェクト固有ビューを projects/{project}/views/{view}.cshtml から解決します。
/// 標準ビューロケーション（Views/{controller}/{view}.cshtml など）より優先されます。
/// Layout ビュー（_Layout.cshtml など）もプロジェクト固有のものを使用できます。
/// </summary>
public class ProjectViewLocationExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context)
    {
        var project = context.ActionContext.RouteData.Values["project"]?.ToString();
        if (!string.IsNullOrEmpty(project))
            context.Values["project"] = project;
    }

    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        if (context.Values.TryGetValue("project", out var project) && !string.IsNullOrEmpty(project))
        {
            var controller = context.ActionContext.RouteData.Values["controller"]?.ToString();

            // プロジェクト固有のビューディレクトリ
            // 1) コントローラ単位のビュー（例: views/Dashboard/Index.cshtml）
            yield return $"/projects/{project}/views/{{1}}/{{0}}.cshtml";

            // 2) 直下ビュー（レイアウト、パーシャル、ルートビュー）
            //    コントローラ固有ビュー(1)より優先度が低いので Index 衝突は実質発生しない
            yield return $"/projects/{project}/views/{{0}}.cshtml";

            // Layout ビュー用のプロジェクト固有ディレクトリ
            yield return $"/projects/{project}/views/Shared/{{0}}.cshtml";
        }

        foreach (var loc in viewLocations)
            yield return loc;
    }
}
