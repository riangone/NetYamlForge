// Chinook プロジェクト固有のビジネスロジック実装
// 音楽商店固有の業務ルールを定義します。

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Chinook.Hooks;

/// <summary>
/// Chinook 音楽商店のビジネスロジック実装。
/// 売上計算の検証、在庫管理、顧客管理などの固有処理を提供します。
/// </summary>
public class ChinookBusinessLogic : IProjectBusinessLogic
{
    private readonly ILogger<ChinookBusinessLogic> _logger;

    public string ProjectName => "chinook";

    public ChinookBusinessLogic(ILogger<ChinookBusinessLogic> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        _logger.LogInformation("Chinook ビジネスロジックを初期化しました");
        return Task.CompletedTask;
    }

    public Task BeforeEntityOperationAsync(string entity, CrudOperation operation, IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx)
    {
        // エンティティ固有の前処理
        return entity.ToLowerInvariant() switch
        {
            "invoice" => ValidateInvoiceAsync(operation, values, db, tx),
            "customer" => ValidateCustomerAsync(operation, values, db, tx),
            "track" => ValidateTrackAsync(operation, values, db, tx),
            _ => Task.CompletedTask
        };
    }

    public Task AfterEntityOperationAsync(string entity, CrudOperation operation, object? id, IDbConnection db, IDbTransaction? tx)
    {
        // エンティティ固有の後処理
        return entity.ToLowerInvariant() switch
        {
            "invoice" => AfterInvoiceOperationAsync(operation, id, db, tx),
            "customer" => AfterCustomerOperationAsync(operation, id, db, tx),
            _ => Task.CompletedTask
        };
    }

    /// <summary>
    /// 請求書の検証：合計金額が明細の合計と一致するか確認
    /// </summary>
    private async Task ValidateInvoiceAsync(CrudOperation operation, IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx)
    {
        if (operation != CrudOperation.Create && operation != CrudOperation.Update)
            return;

        if (!values.TryGetValue("Total", out var totalObj) || totalObj is not decimal total)
            return;

        // 明細テーブルから合計を計算
        if (values.TryGetValue("InvoiceId", out var invoiceIdObj) && invoiceIdObj is int invoiceId)
        {
            var sql = "SELECT COALESCE(SUM(UnitPrice * Quantity), 0) FROM InvoiceLine WHERE InvoiceId = @InvoiceId";
            var calculatedTotal = await db.ExecuteScalarAsync<decimal>(sql, new { InvoiceId = invoiceId }, tx);

            if (Math.Abs(total - calculatedTotal) > 0.01m)
            {
                _logger.LogWarning("請求書合計が明細と一致しません：InvoiceId={Id}, Total={Total}, Calculated={Calculated}",
                    invoiceId, total, calculatedTotal);
                // 警告のみログ出力（処理は継続）
            }
        }
    }

    /// <summary>
    /// 顧客情報の検証：メールアドレスの重複チェック
    /// </summary>
    private async Task ValidateCustomerAsync(CrudOperation operation, IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx)
    {
        if (operation != CrudOperation.Create && operation != CrudOperation.Update)
            return;

        if (!values.TryGetValue("Email", out var emailObj) || emailObj is not string email)
            return;

        var sql = "SELECT COUNT(*) FROM Customer WHERE Email = @Email";
        
        // 更新時は自レコードを除外
        if (operation == CrudOperation.Update && values.TryGetValue("CustomerId", out var idObj) && idObj is int id)
        {
            sql += " AND CustomerId <> @CustomerId";
            var count = await db.ExecuteScalarAsync<int>(sql, new { Email = email, CustomerId = id }, tx);

            if (count > 0)
            {
                _logger.LogWarning("重複したメールアドレス：{Email}", email);
            }
        }
        else if (operation == CrudOperation.Create)
        {
            var count = await db.ExecuteScalarAsync<int>(sql, new { Email = email }, tx);

            if (count > 0)
            {
                _logger.LogWarning("重複したメールアドレス：{Email}", email);
            }
        }
    }

    /// <summary>
    /// トラックの検証：再生時間が 0 以上か確認
    /// </summary>
    private Task ValidateTrackAsync(CrudOperation operation, IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx)
    {
        if (operation != CrudOperation.Create && operation != CrudOperation.Update)
            return Task.CompletedTask;

        if (values.TryGetValue("Milliseconds", out var msObj) && msObj is int ms && ms <= 0)
        {
            _logger.LogWarning("トラックの再生時間が不正：{Milliseconds}ms", ms);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 請求書作成後の処理：売上合計を更新
    /// </summary>
    private async Task AfterInvoiceOperationAsync(CrudOperation operation, object? id, IDbConnection db, IDbTransaction? tx)
    {
        if (operation == CrudOperation.Create && id is int invoiceId)
        {
            // 顧客の合計購入額を更新
            var sql = @"
                UPDATE Customer 
                SET TotalSpent = (
                    SELECT COALESCE(SUM(Total), 0) 
                    FROM Invoice 
                    WHERE CustomerId = (
                        SELECT CustomerId FROM Invoice WHERE InvoiceId = @InvoiceId
                    )
                )
                WHERE CustomerId = (
                    SELECT CustomerId FROM Invoice WHERE InvoiceId = @InvoiceId
                )";
            
            await db.ExecuteAsync(sql, new { InvoiceId = invoiceId }, tx);
            _logger.LogDebug("顧客の合計購入額を更新しました：InvoiceId={InvoiceId}", invoiceId);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 顧客情報更新後の処理：関連する請求書の情報を更新
    /// </summary>
    private Task AfterCustomerOperationAsync(CrudOperation operation, object? id, IDbConnection db, IDbTransaction? tx)
    {
        // 必要に応じて実装
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chinook プロジェクト固有のバリデーション実装。
/// 音楽商店固有の検証ルールを提供します。
/// </summary>
public class ChinookValidator : IProjectValidator
{
    private readonly ILogger<ChinookValidator> _logger;

    public string ProjectName => "chinook";

    public ChinookValidator(ILogger<ChinookValidator> logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<string>> ValidateAsync(string entity, IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx)
    {
        var errors = new List<string>();

        return entity.ToLowerInvariant() switch
        {
            "invoice" => await ValidateInvoiceAsync(values, db, tx),
            "employee" => ValidateEmployeeAsync(values),
            _ => (IEnumerable<string>)errors
        };
    }

    private async Task<IEnumerable<string>> ValidateInvoiceAsync(IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx)
    {
        var errors = new List<string>();

        // 請求書日の検証
        if (values.TryGetValue("InvoiceDate", out var dateObj) && dateObj is string dateStr)
        {
            if (!DateTime.TryParse(dateStr, out _))
            {
                errors.Add("請求書日の形式が正しくありません。");
            }
        }

        // 請求先国の検証
        if (values.TryGetValue("BillingCountry", out var countryObj) && countryObj is string country)
        {
            if (string.IsNullOrWhiteSpace(country))
            {
                errors.Add("請求先国は必須です。");
            }
        }

        return errors;
    }

    private IEnumerable<string> ValidateEmployeeAsync(IDictionary<string, object?> values)
    {
        var errors = new List<string>();

        // 従業員メールの形式検証
        if (values.TryGetValue("Email", out var emailObj) && emailObj is string email)
        {
            if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                errors.Add("従業員メールアドレスの形式が正しくありません。");
            }
        }

        return errors;
    }
}

/// <summary>
/// Chinook プロジェクト固有のデータ変換実装。
/// 音楽商店固有のデータ変換ルールを提供します。
/// </summary>
public class ChinookDataTransformer : IProjectDataTransformer
{
    private readonly ILogger<ChinookDataTransformer> _logger;

    public string ProjectName => "chinook";

    public ChinookDataTransformer(ILogger<ChinookDataTransformer> logger)
    {
        _logger = logger;
    }

    public Task TransformAsync(string entity, IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx)
    {
        // トラック名のトリム処理
        if (entity.ToLowerInvariant() == "track" && values.TryGetValue("Name", out var nameObj) && nameObj is string name)
        {
            values["Name"] = name.Trim();
        }

        // 顧客名のトリム処理
        if (entity.ToLowerInvariant() == "customer")
        {
            if (values.TryGetValue("FirstName", out var firstNameObj) && firstNameObj is string firstName)
            {
                values["FirstName"] = firstName.Trim();
            }
            if (values.TryGetValue("LastName", out var lastNameObj) && lastNameObj is string lastName)
            {
                values["LastName"] = lastName.Trim();
            }
        }

        return Task.CompletedTask;
    }
}
