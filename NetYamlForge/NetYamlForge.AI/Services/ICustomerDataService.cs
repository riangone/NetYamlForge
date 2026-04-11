using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

/// <summary>
/// 顧客データサービスインターフェース
/// </summary>
public interface ICustomerDataService
{
    /// <summary>
    /// 電話番号またはメールアドレスで顧客を検索
    /// </summary>
    Task<CustomerInfo?> GetCustomerByIdentifierAsync(string identifier, string? projectId = null);

    /// <summary>
    /// 顧客 ID で顧客情報を取得
    /// </summary>
    Task<CustomerInfo?> GetCustomerByIdAsync(string customerId, string? projectId = null);

    /// <summary>
    /// 顧客の契約情報を取得
    /// </summary>
    Task<object?> GetCustomerContractsAsync(string customerId, string? projectId = null);

    /// <summary>
    /// 顧客のサービス履歴を取得
    /// </summary>
    Task<object?> GetCustomerServiceHistoryAsync(string customerId, string? projectId = null);

    /// <summary>
    /// 顧客認証（電話番号 + 認証コード）
    /// </summary>
    Task<VerifyCustomerResponse> VerifyCustomerAsync(string identifier, string verificationCode, string? projectId = null);
}
