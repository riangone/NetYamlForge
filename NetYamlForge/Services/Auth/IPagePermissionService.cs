// ファイル概要: カスタムページのアクセス制御インターフェースを定義します。
// 実装クラス PagePermissionService は AppRolePermission テーブルを参照して
// ロールベースの読み書き権限を判定します。
// テーブルが存在しない場合はフォールバックとして全許可になります。

using System.Threading.Tasks;

namespace NetYamlForge.Services.Auth;

/// <summary>
/// カスタムページ・フィールドへのアクセス権限チェックインターフェース。
/// PageController がページ表示前に権限を確認するために使用します。
/// <para>
/// 権限テーブル: AppRolePermission (ProjectName, ResourceType, RoleName, ResourceName, CanRead, CanWrite)
/// </para>
/// </summary>
public interface IPagePermissionService
{
    /// <summary>
    /// 指定ユーザーが対象ページを読み取れるか判定します。
    /// Admin は常に true を返します。テーブル未設定の場合も true を返します。
    /// </summary>
    Task<bool> CanReadPageAsync(string projectName, string pageName, string? userName, bool isAdmin);

    /// <summary>
    /// 指定ユーザーが対象ページに書き込めるか判定します。
    /// Admin は常に true を返します。
    /// </summary>
    Task<bool> CanWritePageAsync(string projectName, string pageName, string? userName, bool isAdmin);

    /// <summary>
    /// 指定ユーザーが特定フィールドに書き込めるか判定します。
    /// ResourceName は "{pageName}:{fieldName}" 形式で評価されます。
    /// </summary>
    Task<bool> CanWriteFieldAsync(string projectName, string pageName, string fieldName, string? userName, bool isAdmin);
}

