// ファイル概要: ユーザー認証・ユーザー管理機能のサービス契約を定義します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Data;
using NetYamlForge.Models.Auth;

namespace NetYamlForge.Services.Auth;

public interface IUserAuthService
{
    Task<AppUser?> ValidateCredentialsAsync(string userName, string password);
    Task<IReadOnlyList<string>> GetUserRolesAsync(string userName);
    Task UpdateLastLoginAsync(int userId);
    Task<IReadOnlyList<AppUser>> GetAllAsync();
    Task<AppUser?> GetByIdAsync(int id);
    Task<int> CreateAsync(UserEditViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null);
    Task UpdateAsync(UserEditViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null);
    Task DeleteAsync(int id, IDbConnection? connection = null, IDbTransaction? transaction = null);
    
    /// <summary>
    /// ユーザー登録（一般向け）
    /// </summary>
    Task<int> RegisterAsync(RegisterViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null);
    
    /// <summary>
    /// 顧客登録（顧客向けセルフ登録）
    /// </summary>
    Task<int> RegisterCustomerAsync(CustomerRegisterViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null);
    
    /// <summary>
    /// ユーザー名の一意性を検証
    /// </summary>
    Task<bool> IsUserNameTakenAsync(string userName);
}
