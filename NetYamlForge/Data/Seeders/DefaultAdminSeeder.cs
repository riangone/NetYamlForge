// ファイル概要: アプリ初回起動時のデフォルト管理者アカウント（admin/Admin@123）を作成するシーダーです。
// AppUser テーブルにレコードが 1 件も存在しない場合のみ実行します。
// PBKDF2-SHA256 でパスワードをハッシュ化して保存します。本番環境では速やかにパスワードを変更してください。

using System.Data;
using Dapper;
using NetYamlForge.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// デフォルト管理者アカウントの作成。
/// すべてのDB方言（SQLite、SQL Server、PostgreSQL、MySQL）で同一ロジック。
/// IDbConnection を通じて、DB方言に依存しない実装。
/// </summary>
public class DefaultAdminSeeder
{
    /// <summary>
    /// デフォルト管理者アカウント（admin/Admin@123）が存在しない場合、作成します。
    /// 既に AppUser レコードが存在する場合はスキップします。
    /// </summary>
    public async Task EnsureDefaultAdminAsync(IDbConnection conn, ILogger logger)
    {
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AppUser");
        if (count > 0)
        {
            return;
        }

        var admin = new AppUser
        {
            UserName = "admin",
            DisplayName = "System Admin",
            PreferredLanguage = "en-US",
            IsAdmin = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        };

        var hasher = new PasswordHasher<AppUser>();
        admin.PasswordHash = hasher.HashPassword(admin, "Admin123!");

        await conn.ExecuteAsync(@"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt)", admin);

        logger.LogWarning("デフォルト管理者を作成しました。user=admin password=Admin123! — 直ちに変更してください。");
    }
}
