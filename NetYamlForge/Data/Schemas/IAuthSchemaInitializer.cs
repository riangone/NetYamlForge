// ファイル概要: DB方言別の認証テーブル初期化インターフェースを定義します。
// SQLite / SQL Server / PostgreSQL / MySQL の各 Initializer がこのインターフェースを実装します。
// DbInitializer から DI 経由で呼び出され、アプリ起動時に AppUser 等のテーブルを作成します。

using System.Data;

namespace NetYamlForge.Data.Schemas;

/// <summary>
/// データベース方言別の認証テーブル初期化インターフェース。
/// </summary>
public interface IAuthSchemaInitializer
{
    Task InitializeAsync(IDbConnection conn, ILogger logger);
}
