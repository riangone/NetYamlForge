// ファイル概要：ユーザー登録フォームの入力モデルです。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.ComponentModel.DataAnnotations;

namespace NetYamlForge.Models.Auth;

public class RegisterViewModel
{
    [Required(ErrorMessage = "请输入用户名")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "用户名长度必须在 3-50 个字符之间")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入密码")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "密码长度必须至少为 6 个字符")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "请确认密码")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "两次输入的密码不一致")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入显示名称")]
    [StringLength(100, ErrorMessage = "显示名称不能超过 100 个字符")]
    public string DisplayName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "请输入有效的邮箱地址")]
    public string? Email { get; set; }

    [Phone(ErrorMessage = "请输入有效的电话号码")]
    public string? Phone { get; set; }

    public string? PreferredLanguage { get; set; } = "ja-JP";

    public string? ReturnUrl { get; set; }
}
