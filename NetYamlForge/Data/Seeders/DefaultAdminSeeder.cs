// ファイル概要: アプリ初回起動時のデフォルト管理者アカウントを作成するシーダーです。
// AppUser テーブルにレコードが 1 件も存在しない場合のみ実行します。
// パスワードの優先順位: 環境変数 NYF_ADMIN_PASSWORD > 設定 Auth:DefaultAdminPassword > ランダム生成

using System.Data;
using System.Security.Cryptography;
using Dapper;
using NetYamlForge.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// デフォルト管理者アカウントの作成。
/// パスワードは以下の優先順位で決定されます：
/// 1. 環境変数 NYF_ADMIN_PASSWORD
/// 2. 設定 Auth:DefaultAdminPassword
/// 3. ランダム生成（16文字）— 起動ログにのみ出力
/// </summary>
public class DefaultAdminSeeder
{
    /// <summary>
    /// デフォルト管理者アカウントが存在しない場合、作成します。
    /// 既に AppUser レコードが存在する場合はスキップします。
    /// </summary>
    public async Task EnsureDefaultAdminAsync(IDbConnection conn, ILogger logger, IConfiguration? configuration = null)
    {
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AppUser");
        if (count > 0)
        {
            return;
        }

        var password = ResolvePassword(configuration, logger);

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
        admin.PasswordHash = hasher.HashPassword(admin, password);

        await conn.ExecuteAsync(@"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt)", admin);

        logger.LogWarning(
            "================================================================================\n" +
            "デフォルト管理者を作成しました\n" +
            "  ユーザー名: admin\n" +
            "  パスワード: {Password}\n" +
            "  ※ 直ちにパスワードを変更してください\n" +
            "================================================================================",
            password);
    }

    private static string ResolvePassword(IConfiguration? configuration, ILogger logger)
    {
        // 1. 環境変数
        var envPassword = Environment.GetEnvironmentVariable("NYF_ADMIN_PASSWORD");
        if (!string.IsNullOrWhiteSpace(envPassword))
        {
            logger.LogInformation("管理者パスワード: 環境変数 NYF_ADMIN_PASSWORD を使用");
            return envPassword;
        }

        // 2. 設定ファイル
        var configPassword = configuration?["Auth:DefaultAdminPassword"];
        if (!string.IsNullOrWhiteSpace(configPassword))
        {
            logger.LogInformation("管理者パスワード: 設定 Auth:DefaultAdminPassword を使用");
            return configPassword;
        }

        // 3. ランダム生成
        return GenerateRandomPassword(16);
    }

    private static string GenerateRandomPassword(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var bytes = RandomNumberGenerator.GetBytes(length);
        return string.Create(length, bytes, (span, data) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = chars[data[i] % chars.Length];
            }
        });
    }
}
