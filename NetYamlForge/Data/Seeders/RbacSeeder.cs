// ファイル概要: RBAC（ロールベースアクセス制御）の初期シードデータを投入するシーダーです。
// admin ユーザーの AdminOps ロールを AppUserRole に登録します。
// 既にロールが存在する場合はスキップ（IGNORE INTO）します。

using System.Data;
using Dapper;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// RBAC（ロールベースアクセス制御）の初期シード。
/// admin ユーザーへの AdminOps ロール付与を行います。
/// </summary>
public class RbacSeeder
{
    /// <summary>
    /// 共通ロール定義（AppUserRole）をシードします。
    /// </summary>
    public async Task EnsureRbacRolesAsync(IDbConnection conn, ILogger logger)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            @"INSERT OR IGNORE INTO AppUserRole(UserName, RoleName, CreatedAt) VALUES(@UserName, @RoleName, @CreatedAt)",
            new[]
            {
                new { UserName = "admin", RoleName = "AdminOps", CreatedAt = now }
            });

        logger.LogInformation("RBAC ロール を設定済み");
    }
}
