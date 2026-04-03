// ファイル概要：顧客向け登録フォームの入力モデルです。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.ComponentModel.DataAnnotations;

namespace NetYamlForge.Models.Auth;

public class CustomerRegisterViewModel
{
    // アカウント情報
    [Required(ErrorMessage = "ユーザー名は必須です")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "ユーザー名は 3〜50 文字で入力してください")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "パスワードは必須です")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "パスワードは 6 文字以上で入力してください")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "パスワードの確認は必須です")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "パスワードが一致しません")]
    public string ConfirmPassword { get; set; } = string.Empty;

    // 顧客情報
    [Required(ErrorMessage = "お名前は必須です")]
    [StringLength(100, ErrorMessage = "お名前は 100 文字以内で入力してください")]
    public string Name { get; set; } = string.Empty;

    public string? NameKana { get; set; }

    [StringLength(20, ErrorMessage = "電話番号は 20 文字以内で入力してください")]
    [Required(ErrorMessage = "電話番号は必須です")]
    public string Phone { get; set; } = string.Empty;

    public string? Mobile { get; set; }

    [EmailAddress(ErrorMessage = "有効なメールアドレスを入力してください")]
    public string? Email { get; set; }

    public string? PostalCode { get; set; }

    public string? Address { get; set; }

    [StringLength(20)]
    public string? PreferredContact { get; set; } = "phone";

    public string? PreferredLanguage { get; set; } = "ja-JP";

    public string? ReturnUrl { get; set; }
}
