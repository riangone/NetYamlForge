// ファイル概要: 監査ログ書き込みサービスの契約を定義します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Data;

namespace NetYamlForge.Services.Auth;

public interface IAuditLogService
{
    Task WriteAsync(
        string action,
        string? entity = null,
        string? detail = null,
        string? userName = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null);
}
