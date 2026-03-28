// ファイル概要: auto-dealer-demo プロジェクト用デモユーザー・ロールのシーダーです。
// 各ロール（オペレーター、営業担当、営業管理、サービス部門、AI管理者、経営層）のデモユーザーを作成します。
// 既に存在するユーザーはスキップします。パスワードはすべて Demo@123 です。

using System.Data;
using Dapper;
using NetYamlForge.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// auto-dealer-demo ロール別デモユーザーのシード。
/// 6種類のロールに対応したデモアカウントを作成します。
/// </summary>
public class AutoDealerDemoSeeder
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    /// <summary>
    /// ロール別デモユーザーを作成します（存在しない場合のみ）。
    /// </summary>
    public async Task EnsureDemoUsersAsync(IDbConnection conn, ILogger logger)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // ロール別デモユーザー定義（userName, displayName, roleName）
        var demoUsers = new[]
        {
            ("operator1",  "田中オペレーター",  "operator"),
            ("sales1",     "鈴木営業担当",      "sales_rep"),
            ("manager1",   "佐藤営業部長",      "sales_manager"),
            ("service1",   "高橋サービス担当",  "service_staff"),
            ("exec1",      "山田部長",          "executive"),
            ("aiadmin1",   "伊藤AI管理者",      "ai_admin"),
        };

        foreach (var (userName, displayName, roleName) in demoUsers)
        {
            var existingId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT Id FROM AppUser WHERE UserName = @UserName",
                new { UserName = userName });

            if (!existingId.HasValue)
            {
                var user = new AppUser
                {
                    UserName = userName,
                    DisplayName = displayName,
                    PreferredLanguage = "ja-JP",
                    IsAdmin = false,
                    IsActive = true,
                    CreatedAt = now
                };
                user.PasswordHash = _hasher.HashPassword(user, "Demo@123");

                await conn.ExecuteAsync(@"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt)", user);

                logger.LogInformation("デモユーザー '{UserName}' ({Role}) を作成しました", userName, roleName);
            }

            // AppUserRole は常に確保（INSERT OR IGNORE）
            await conn.ExecuteAsync(
                @"INSERT OR IGNORE INTO AppUserRole (UserName, RoleName, CreatedAt) VALUES (@UserName, @RoleName, @CreatedAt)",
                new { UserName = userName, RoleName = roleName, CreatedAt = now });
        }

        logger.LogInformation("auto-dealer-demo デモユーザー ({Count} 名) のセットアップ完了", demoUsers.Length);
    }
}
