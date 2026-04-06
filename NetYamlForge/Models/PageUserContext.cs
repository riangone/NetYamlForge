// ファイル概要: ページSQLに注入するログインユーザーのコンテキスト情報。

using System;
using System.Collections.Generic;
using System.Linq;

namespace NetYamlForge.Models;

/// <summary>
/// ページSQLクエリに注入するログインユーザーのコンテキスト情報。
/// PageController で ClaimsPrincipal から生成し、PageDataQueryService に渡す。
/// </summary>
public record PageUserContext(
    /// <summary>ログインユーザー名 (ClaimTypes.Name = app_user.user_name)</summary>
    string UserName,
    /// <summary>表示名 (ClaimTypes.GivenName = app_user.display_name)</summary>
    string DisplayName,
    /// <summary>ユーザー ID (ClaimTypes.NameIdentifier = app_user.id の文字列表現)</summary>
    string UserId,
    /// <summary>所持ロール一覧 (ClaimTypes.Role の全値)</summary>
    IReadOnlyList<string> Roles,
    /// <summary>管理者フラグ</summary>
    bool IsAdmin,
    /// <summary>認証済みフラグ</summary>
    bool IsAuthenticated
)
{
    /// <summary>未認証ユーザー向けの空コンテキスト</summary>
    public static readonly PageUserContext Anonymous = new(
        UserName: "",
        DisplayName: "",
        UserId: "",
        Roles: Array.Empty<string>(),
        IsAdmin: false,
        IsAuthenticated: false
    );

    /// <summary>指定ロールを所持しているか（大文字小文字を無視）</summary>
    public bool HasRole(string role) =>
        Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    /// <summary>いずれかのロールを所持しているか</summary>
    public bool HasAnyRole(IEnumerable<string> roles) =>
        roles.Any(HasRole);
}
