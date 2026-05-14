// ファイル概要: ユーザー作成・編集フォーム用の入力モデルです。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.ComponentModel.DataAnnotations;

namespace NetYamlForge.Models.Auth;

public class UserEditViewModel
{
    public int? Id { get; set; }

    [Required]
    public string UserName { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Required]
    public string DisplayName { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    public string PreferredLanguage { get; set; } = "en-US";

    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; } = true;

    public string? ExternalId { get; set; }
    public string? ExternalSource { get; set; }
    public string? OwningProject { get; set; }
}
