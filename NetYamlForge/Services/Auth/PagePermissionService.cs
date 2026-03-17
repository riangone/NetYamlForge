// ファイル概要: AppRolePermission テーブルを参照してカスタムページのアクセス権限を判定します。
// ロールは AppUserRole テーブルから取得し、ワイルドカード("*")と完全一致の両方を評価します。
// テーブルが存在しない場合（例: 小規模プロジェクト）は例外をキャッチして全許可にフォールバックします。

using System.Data;
using Dapper;

namespace NetYamlForge.Services.Auth;

/// <summary>
/// IPagePermissionService の DB 実装。
/// AppRolePermission テーブルのロールベース権限レコードで
/// read/write アクセスを判定します。
/// <para>
/// 評価順序: 完全一致（ResourceName = pageName） > ワイルドカード（ResourceName = '*'）
/// テーブル未設定または DB エラー時は全操作を許可（フォールバック）。
/// </para>
/// </summary>
public class PagePermissionService : IPagePermissionService
{
    private readonly IDbConnection _db;
    private readonly ILogger<PagePermissionService> _logger;

    public PagePermissionService(IDbConnection db, ILogger<PagePermissionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> CanReadPageAsync(string projectName, string pageName, string? userName, bool isAdmin)
    {
        return await CanAccessAsync(projectName, "page", pageName, userName, isAdmin, access: "read");
    }

    public async Task<bool> CanWritePageAsync(string projectName, string pageName, string? userName, bool isAdmin)
    {
        return await CanAccessAsync(projectName, "page", pageName, userName, isAdmin, access: "write");
    }

    public async Task<bool> CanWriteFieldAsync(string projectName, string pageName, string fieldName, string? userName, bool isAdmin)
    {
        var resourceName = $"{pageName}:{fieldName}";
        return await CanAccessAsync(projectName, "field", resourceName, userName, isAdmin, access: "write");
    }

    // 実際の権限評価ロジック。CanReadPage/CanWritePage/CanWriteField から呼ばれる共通処理。
    private async Task<bool> CanAccessAsync(
        string projectName,
        string resourceType,   // "page" or "field"
        string resourceName,   // ページ名 or "pageName:fieldName"
        string? userName,
        bool isAdmin,
        string access)         // "read" or "write"
    {
        // Admin は全リソースにアクセス可能
        if (isAdmin)
        {
            return true;
        }

        // 未認証ユーザーはアクセス不可
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        try
        {
            // ユーザーのロール一覧を取得（ロール未設定の場合は "User" として扱う）
            var roles = (await _db.QueryAsync<string>(
                @"SELECT RoleName
                  FROM AppUserRole
                  WHERE UserName = @UserName",
                new { UserName = userName })).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (roles.Count == 0)
            {
                roles.Add("User");
            }

            var rows = (await _db.QueryAsync<PermissionRow>(
                @"SELECT RoleName, ResourceName, CanRead, CanWrite
                  FROM AppRolePermission
                  WHERE ProjectName = @ProjectName
                    AND ResourceType = @ResourceType
                    AND RoleName IN @Roles
                    AND (ResourceName = @ResourceName OR ResourceName = '*')",
                new
                {
                    ProjectName = projectName,
                    ResourceType = resourceType,
                    Roles = roles,
                    ResourceName = resourceName
                })).ToList();

            return EvaluatePermission(rows, resourceName, access);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Permission table unavailable. fallback allow for {ResourceType}:{ResourceName}", resourceType, resourceName);
            return true;
        }
    }

    // 取得した権限行リストを評価して bool を返す。
    // 完全一致レコードが優先され、なければワイルドカード("*")レコードを使用する。
    internal static bool EvaluatePermission(List<PermissionRow> rows, string resourceName, string access)
    {
        // 権限レコードがない場合は許可（デフォルト開放ポリシー）
        if (rows.Count == 0)
        {
            return true;
        }

        // 完全一致を優先、なければワイルドカードを使用
        var exact = rows.Where(r => string.Equals(r.ResourceName, resourceName, StringComparison.OrdinalIgnoreCase)).ToList();
        var wildcard = rows.Where(r => string.Equals(r.ResourceName, "*", StringComparison.OrdinalIgnoreCase)).ToList();
        var target = exact.Count > 0 ? exact : wildcard;
        if (target.Count == 0)
        {
            return true;
        }

        if (string.Equals(access, "write", StringComparison.OrdinalIgnoreCase))
        {
            return target.Any(r => r.CanWrite == 1);
        }

        return target.Any(r => r.CanRead == 1);
    }

    // AppRolePermission テーブルのレコードを受け取る内部 DTO
    internal sealed class PermissionRow
    {
        public string RoleName { get; set; } = string.Empty;
        public string ResourceName { get; set; } = string.Empty;
        public int CanRead { get; set; }    // 1 = 読み取り可、0 = 不可
        public int CanWrite { get; set; }   // 1 = 書き込み可、0 = 不可
    }
}

