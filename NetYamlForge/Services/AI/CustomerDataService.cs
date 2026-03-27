using System.Data;
using Dapper;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 顧客データサービス実装
/// </summary>
public class CustomerDataService : ICustomerDataService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ILogger<CustomerDataService> _logger;
    private const string DefaultProjectId = "auto-dealer-demo";

    public CustomerDataService(
        IDbConnectionFactory dbConnectionFactory,
        ILogger<CustomerDataService> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CustomerInfo?> GetCustomerByIdentifierAsync(string identifier, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            // 電話番号またはメールアドレスで検索
            var sql = @"
                SELECT
                    customer_id,
                    name,
                    phone,
                    email,
                    tier_level
                FROM customers
                WHERE phone = @Identifier OR email = @Identifier
                LIMIT 1";

            var result = await db.QueryFirstOrDefaultAsync(sql, new { Identifier = identifier });

            if (result == null)
                return null;

            return new CustomerInfo
            {
                CustomerId = result.customer_id,
                Name = result.name,
                Phone = result.phone,
                Email = result.email,
                TierLevel = result.tier_level ?? "regular"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顧客検索に失敗：{Identifier}", identifier);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<CustomerInfo?> GetCustomerByIdAsync(string customerId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var sql = @"
                SELECT
                    customer_id,
                    name,
                    phone,
                    email,
                    tier_level
                FROM customers
                WHERE customer_id = @CustomerId";

            var result = await db.QueryFirstOrDefaultAsync(sql, new { CustomerId = customerId });

            if (result == null)
                return null;

            return new CustomerInfo
            {
                CustomerId = result.customer_id,
                Name = result.name,
                Phone = result.phone,
                Email = result.email,
                TierLevel = result.tier_level ?? "regular"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顧客取得に失敗：{CustomerId}", customerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<object?> GetCustomerContractsAsync(string customerId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            // 車両購入契約履歴
            var contractsSql = @"
                SELECT
                    v.vehicle_id,
                    v.maker,
                    v.model,
                    v.year,
                    'purchased' as status,
                    '2023-01-15' as contract_date
                FROM vehicles v
                INNER JOIN customer_vehicles cv ON v.vehicle_id = cv.vehicle_id
                WHERE cv.customer_id = @CustomerId
                ORDER BY cv.purchase_date DESC
                LIMIT 5";

            var contracts = await db.QueryAsync(contractsSql, new { CustomerId = customerId });

            return new
            {
                customerId = customerId,
                contracts = contracts.ToList(),
                totalContracts = contracts.Count()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "契約情報取得に失敗：{CustomerId}", customerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<object?> GetCustomerServiceHistoryAsync(string customerId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            // サービス予約履歴
            var historySql = @"
                SELECT
                    appointment_id,
                    appointment_type,
                    preferred_date,
                    status,
                    created_at
                FROM service_appointments
                WHERE customer_id = @CustomerId
                ORDER BY preferred_date DESC
                LIMIT 10";

            var history = await db.QueryAsync(historySql, new { CustomerId = customerId });

            // サービス依頼履歴
            var requestsSql = @"
                SELECT
                    request_id,
                    request_type,
                    subject,
                    status,
                    created_at
                FROM service_requests
                WHERE customer_id = @CustomerId
                ORDER BY created_at DESC
                LIMIT 10";

            var requests = await db.QueryAsync(requestsSql, new { CustomerId = customerId });

            return new
            {
                customerId = customerId,
                appointments = history.ToList(),
                serviceRequests = requests.ToList(),
                totalAppointments = history.Count(),
                totalRequests = requests.Count()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サービス履歴取得に失敗：{CustomerId}", customerId);
            return null;
        }
    }

    /// <summary>
    /// 顧客認証（電話番号 + 認証コード）
    /// </summary>
    public async Task<VerifyCustomerResponse> VerifyCustomerAsync(
        string identifier,
        string verificationCode,
        string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            // 簡易認証（実際には SMS/Email で送信したコードを検証）
            // ここではデモ用に任意の 6 桁コードを許可
            if (string.IsNullOrEmpty(verificationCode) || verificationCode.Length != 6)
            {
                return new VerifyCustomerResponse
                {
                    Success = false,
                    ErrorMessage = "認証コードは 6 桁の数字を入力してください"
                };
            }

            // 顧客検索
            var customer = await GetCustomerByIdentifierAsync(identifier, project);

            if (customer == null)
            {
                return new VerifyCustomerResponse
                {
                    Success = false,
                    ErrorMessage = "該当する顧客が見つかりません"
                };
            }

            return new VerifyCustomerResponse
            {
                Success = true,
                CustomerId = customer.CustomerId,
                CustomerName = customer.Name,
                TierLevel = customer.TierLevel
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顧客認証に失敗：{Identifier}", identifier);
            return new VerifyCustomerResponse
            {
                Success = false,
                ErrorMessage = "認証処理中にエラーが発生しました"
            };
        }
    }
}
