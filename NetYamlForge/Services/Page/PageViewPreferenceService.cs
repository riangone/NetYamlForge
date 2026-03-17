// ファイル概要: ユーザーごとのページビュー保存設定（SavedPageView）を管理するサービスです。
// AppUserSavedView テーブルを参照してフィルター条件の保存・読み込み・デフォルト指定を行います。
// DynamicEntityController の SaveView / LoadView アクションから呼ばれます。

using System.Data;
using System.Text.Json;
using Dapper;
using NetYamlForge.Services.Auth;

namespace NetYamlForge.Services;

public sealed record SavedPageView(string ViewName, Dictionary<string, string> Filters, bool IsDefault);

public sealed class PageViewPreferenceService
{
    private readonly IDbConnection _db;
    private readonly IAuditLogService _audit;
    private readonly ILogger<PageViewPreferenceService> _logger;

    public PageViewPreferenceService(
        IDbConnection db,
        IAuditLogService audit,
        ILogger<PageViewPreferenceService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<List<SavedPageView>> LoadSavedViewsAsync(string projectName, string pageName, string userName)
    {
        var rows = await _db.QueryAsync<(string ViewName, string? FiltersJson, int IsDefault)>(
            @"SELECT ViewName, FiltersJson, IsDefault
              FROM AppUserSavedView
              WHERE ProjectName = @ProjectName
                AND PageName = @PageName
                AND UserName = @UserName
              ORDER BY IsDefault DESC, ViewName ASC",
            new
            {
                ProjectName = projectName,
                PageName = pageName,
                UserName = userName
            });

        return rows.Select(row =>
            new SavedPageView(
                row.ViewName,
                ParseFilters(row.FiltersJson),
                row.IsDefault == 1))
            .ToList();
    }

    public async Task<(bool ok, string? message)> SaveViewAsync(
        string projectName,
        string pageName,
        string userName,
        string viewName,
        Dictionary<string, string> filters,
        bool makeDefault)
    {
        var normalizedViewName = viewName.Trim();
        var filtersJson = JsonSerializer.Serialize(filters);
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        using var tx = _db.BeginTransaction();
        try
        {
            if (makeDefault)
            {
                await _db.ExecuteAsync(
                    @"UPDATE AppUserSavedView
                      SET IsDefault = 0, UpdatedAt = @Now
                      WHERE ProjectName = @ProjectName
                        AND PageName = @PageName
                        AND UserName = @UserName",
                    new
                    {
                        ProjectName = projectName,
                        PageName = pageName,
                        UserName = userName,
                        Now = now
                    }, tx);
            }

            await _db.ExecuteAsync(
                @"DELETE FROM AppUserSavedView
                  WHERE ProjectName = @ProjectName
                    AND PageName = @PageName
                    AND UserName = @UserName
                    AND ViewName = @ViewName",
                new
                {
                    ProjectName = projectName,
                    PageName = pageName,
                    UserName = userName,
                    ViewName = normalizedViewName
                }, tx);

            await _db.ExecuteAsync(
                @"INSERT INTO AppUserSavedView
                  (ProjectName, PageName, UserName, ViewName, FiltersJson, IsDefault, CreatedAt, UpdatedAt)
                  VALUES(@ProjectName, @PageName, @UserName, @ViewName, @FiltersJson, @IsDefault, @Now, @Now)",
                new
                {
                    ProjectName = projectName,
                    PageName = pageName,
                    UserName = userName,
                    ViewName = normalizedViewName,
                    FiltersJson = filtersJson,
                    IsDefault = makeDefault ? 1 : 0,
                    Now = now
                }, tx);

            await _audit.WriteAsync(
                "page_view_save",
                pageName,
                $"Saved page view. page={pageName}, view={normalizedViewName}, default={(makeDefault ? 1 : 0)}",
                userName,
                _db,
                tx);

            tx.Commit();
            return (true, null);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogWarning(ex, "SaveView failed. page={Page}, view={View}", pageName, normalizedViewName);
            return (false, "ビュー保存に失敗しました。");
        }
    }

    public async Task DeleteViewAsync(string projectName, string pageName, string userName, string viewName)
    {
        await _db.ExecuteAsync(
            @"DELETE FROM AppUserSavedView
              WHERE ProjectName = @ProjectName
                AND PageName = @PageName
                AND UserName = @UserName
                AND ViewName = @ViewName",
            new
            {
                ProjectName = projectName,
                PageName = pageName,
                UserName = userName,
                ViewName = viewName.Trim()
            });
    }

    private static Dictionary<string, string> ParseFilters(string? filtersJson)
    {
        if (string.IsNullOrWhiteSpace(filtersJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(filtersJson) ??
                   new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

