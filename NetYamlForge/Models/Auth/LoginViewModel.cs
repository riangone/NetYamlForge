// ファイル概要：ログインフォームの入力モデルです。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.ComponentModel.DataAnnotations;

namespace NetYamlForge.Models.Auth;

public class LoginViewModel
{
    [Required(ErrorMessage = "请输入用户名")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入密码")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
