// ファイル概要: アプリケーションユーザーの永続化モデルを定義します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

namespace NetYamlForge.Models.Auth;

public class AppUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string UserType { get; set; } = "employee";
    public string? DefaultProjectName { get; set; }
    public string PreferredLanguage { get; set; } = "ja-JP";
    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ExternalId { get; set; }
    public string? ExternalSource { get; set; }
    public string? OwningProject { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    public string? UpdatedAt { get; set; }
    public string? LastLoginAt { get; set; }
    public string? ApiToken { get; set; }
}
