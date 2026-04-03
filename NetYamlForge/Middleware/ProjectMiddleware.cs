// ファイル概要: URLの {project} ルートパラメータからプロジェクトを特定し、ProjectScope を初期化します。
// {project} がない場合は ReturnUrl クエリパラメータからプロジェクトを推測します。
// UseRouting() の後、UseAuthentication() の前に配置してください。

using NetYamlForge.Services;
using NetYamlForge.Localization;

namespace NetYamlForge.Middleware;

public class ProjectMiddleware
{
    private readonly RequestDelegate _next;

    public ProjectMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ProjectManager pm, ProjectScope scope)
    {
        var previousProject = LocalizationProjectContext.CurrentProjectName;
        var projectName = context.GetRouteValue("project")?.ToString();
        var hasProjectInRoute = !string.IsNullOrWhiteSpace(projectName);
        try
        {
            if (hasProjectInRoute)
            {
                // {project} ルートパラメータが存在する場合は明示的に解決
                if (pm.TryGet(projectName!, out var info))
                {
                    scope.Set(info!);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync($"プロジェクト '{projectName}' が見つかりません。");
                    return;
                }
            }
            else
            {
                // {project} がない場合（例: /Account/Login?ReturnUrl=/chinook/...）
                // ReturnUrl の第1セグメントからプロジェクトを推測します。
                // POST /Account/Login の場合はフォーム値にも ReturnUrl が入るため両方を確認します。
                var returnUrl = context.Request.Query["ReturnUrl"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(returnUrl) && context.Request.HasFormContentType)
                {
                    var form = await context.Request.ReadFormAsync();
                    returnUrl = form["ReturnUrl"].FirstOrDefault();
                }
                if (!string.IsNullOrWhiteSpace(returnUrl))
                {
                    var firstSegment = returnUrl.TrimStart('/').Split('/').FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(firstSegment) && pm.TryGet(firstSegment, out var projFromReturn))
                    {
                        scope.Set(projFromReturn!);
                    }
                }

                // それでも未設定の場合は以下の優先度でプロジェクトを選択
                // 1. framework プロジェクト（フレームワーク管理用）
                // 2. 最初のプロジェクト（フォールバック）
                if (!scope.IsSet)
                {
                    var allProjects = pm.GetAll().ToList();
                    
                    // framework プロジェクトを優先的に選択（デフォルトプロジェクト）
                    var frameworkProject = allProjects.FirstOrDefault(p => p.Name == "framework");
                    if (frameworkProject != null)
                    {
                        scope.Set(frameworkProject);
                    }
                    else
                    {
                        // フォールバック：最初のプロジェクト
                        var firstProject = allProjects.FirstOrDefault();
                        if (firstProject != null)
                        {
                            scope.Set(firstProject);
                        }
                    }
                }
            }

            if (scope.IsSet)
            {
                LocalizationProjectContext.CurrentProjectName = scope.Current.Name;
            }

            await _next(context);
        }
        finally
        {
            LocalizationProjectContext.CurrentProjectName = previousProject;
        }
    }
}
