using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models.Auth;

namespace NetYamlForge.Services.Auth;

public partial class UserAuthService
{
    public async Task<int> RegisterAsync(RegisterViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var ownConnection = connection == null;
        var conn = connection ?? await GetConnectionAsync();
        try
        {
            // ユーザー名チェック
            var existing = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM app_user WHERE user_name = @UserName",
                new { input.UserName }, transaction);
            if (existing > 0)
            {
                throw new InvalidOperationException($"User '{input.UserName}' already exists.");
            }

            var user = new AppUser
            {
                UserName = input.UserName,
                DisplayName = input.DisplayName,
                PreferredLanguage = input.PreferredLanguage ?? "ja-JP",
                IsAdmin = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var password = string.IsNullOrWhiteSpace(input.Password) ? GenerateRandomPassword() : input.Password;
            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            var id = await InsertUserAsync(user, conn, transaction);

            // カスタムロール割り当て（customer ロール）
            await conn.ExecuteAsync(
                "INSERT INTO app_user_role (user_name, role_name, created_at) VALUES (@UserName, @RoleName, @Now)",
                new { input.UserName, RoleName = "customer", Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }, transaction);

            _logger.LogInformation("Registered user '{UserName}' with id {UserId}", user.UserName, id);
            return (int)id;
        }
        finally
        {
            if (ownConnection && conn != null)
            {
                conn.Close();
                conn.Dispose();
            }
        }
    }

    public async Task<int> RegisterCustomerAsync(CustomerRegisterViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var ownConnection = connection == null;
        var conn = connection ?? await GetConnectionAsync();
        try
        {
            // ユーザー名チェック
            var existing = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM app_user WHERE user_name = @UserName",
                new { input.UserName }, transaction);
            if (existing > 0)
            {
                throw new InvalidOperationException($"User '{input.UserName}' already exists.");
            }

            // トランザクション内でユーザーと顧客の両方を登録
            var user = new AppUser
            {
                UserName = input.UserName,
                DisplayName = input.Name,
                PreferredLanguage = input.PreferredLanguage ?? "ja-JP",
                IsAdmin = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var password = string.IsNullOrWhiteSpace(input.Password) ? GenerateRandomPassword() : input.Password;
            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            var userId = await InsertUserAsync(user, conn, transaction);

            // customer ロールを付与
            await conn.ExecuteAsync(
                "INSERT INTO app_user_role (user_name, role_name, created_at) VALUES (@UserName, @RoleName, @Now)",
                new { input.UserName, RoleName = "customer", Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }, transaction);

            // customers テーブルにもレコードを作成（auto-dealer-demo など）
            // 既存の customers レコードがある場合は user_name を更新する
            var customerId = await SyncCustomerRecordAsync(conn, input, userId, transaction);

            _logger.LogInformation("Registered customer '{UserName}' with user id {UserId}", user.UserName, userId);
            return (int)userId;
        }
        finally
        {
            if (ownConnection && conn != null)
            {
                conn.Close();
                conn.Dispose();
            }
        }
    }

    /// <summary>
    /// 顧客登録時に Customer/Customers テーブルにもレコードを作成/更新する。
    /// user_name/UserName 列を持つテーブルのみが対象（auto-dealer-demo など）。
    /// 取引先マスタ（biz-docs）など、ユーザー認証と関係ない Customer 表は対象外。
    /// </summary>
    private async Task<string?> SyncCustomerRecordAsync(IDbConnection conn, CustomerRegisterViewModel input, long userId, IDbTransaction? transaction)
    {
        // 対象テーブルを検索（customers, Customer, customer の順でチェック）
        var targetTable = await FindCustomerTableAsync(conn, transaction);
        if (targetTable == null)
        {
            _logger.LogDebug("Customer table with user_name column does not exist, skipping sync");
            return null;
        }

        // テーブル構造情報を取得
        var tableInfo = await GetCustomerTableInfoAsync(conn, targetTable, transaction);
        if (tableInfo == null)
        {
            _logger.LogDebug("Failed to get table info for {Table}, skipping sync", targetTable);
            return null;
        }

        // 既に同じ user_name の顧客レコードが存在するかチェック
        // 表名・列名は DB 元情報から取得されるため安全
        var selectSql = $"SELECT {tableInfo.PrimaryKeyColumn} FROM {tableInfo.TableName} WHERE {tableInfo.UserNameColumn} = @UserName";
        var existingCustomerId = await conn.ExecuteScalarAsync<string>(
            selectSql,
            new { UserName = input.UserName }, transaction);

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var customerId = tableInfo.PrimaryKeyIsIdentity
            ? existingCustomerId ?? $"CUST-{DateTime.UtcNow:yyyyMMddHHmmss}-{userId:D6}"
            : $"CUST-{DateTime.UtcNow:yyyyMMddHHmmss}-{userId:D6}";

        if (string.IsNullOrEmpty(existingCustomerId))
        {
            // 新規作成
            var insertSql = BuildCustomerInsertSql(tableInfo, customerId, now);
            var insertParams = new
            {
                CustomerId = customerId,
                UserName = input.UserName,
                CustomerType = "individual",
                Name = input.Name,
                NameKana = input.NameKana ?? "",
                Phone = input.Phone,
                Mobile = input.Mobile ?? "",
                Email = input.Email ?? "",
                TierLevel = "regular",
                PreferredContact = input.PreferredContact ?? "phone",
                CreatedAt = now,
                UpdatedAt = now
            };

            await conn.ExecuteAsync(insertSql, insertParams, transaction);

            _logger.LogDebug("Created customer record '{CustomerId}' for user '{UserName}' in table {Table}",
                customerId, input.UserName, tableInfo.TableName);
        }
        else
        {
            customerId = existingCustomerId;
            // 既存レコードの user_name を更新（既に顧客レコードがある場合）
            var updateSql = $"UPDATE {tableInfo.TableName} SET {tableInfo.UserNameColumn} = @UserName, {tableInfo.UpdatedAtColumn} = @UpdatedAt WHERE {tableInfo.PrimaryKeyColumn} = @CustomerId";
            await conn.ExecuteAsync(updateSql,
                new { UserName = input.UserName, UpdatedAt = now, CustomerId = existingCustomerId },
                transaction);

            _logger.LogDebug("Updated customer record '{CustomerId}' with user_name '{UserName}' in table {Table}",
                existingCustomerId, input.UserName, tableInfo.TableName);
        }

        return customerId;
    }

    private class CustomerTableInfo
    {
        public string TableName { get; set; } = string.Empty;
        public string PrimaryKeyColumn { get; set; } = string.Empty;
        public bool PrimaryKeyIsIdentity { get; set; }
        public string UserNameColumn { get; set; } = string.Empty;
        public string UpdatedAtColumn { get; set; } = string.Empty;
    }
}
