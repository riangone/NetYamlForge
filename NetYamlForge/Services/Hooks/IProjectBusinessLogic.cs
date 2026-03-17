// ファイル概要：プロジェクト固有のビジネスロジックを定義するインターフェース群です。
// 各プロジェクトは必要に応じて実装し、Hooks/ ディレクトリに配置します。

using System.Data;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// プロジェクト固有のビジネスロジックを実装するためのインターフェース。
/// 各プロジェクトは必要に応じて実装し、Hooks/ ディレクトリに配置します。
/// </summary>
public interface IProjectBusinessLogic
{
    /// <summary>
    /// プロジェクト名。
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// 初期化処理。プロジェクト起動時に 1 回だけ呼び出されます。
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// エンティティ操作前の横断的処理。
    /// </summary>
    Task BeforeEntityOperationAsync(string entity, CrudOperation operation, IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx);

    /// <summary>
    /// エンティティ操作後の横断的処理。
    /// </summary>
    Task AfterEntityOperationAsync(string entity, CrudOperation operation, object? id, IDbConnection db, IDbTransaction? tx);
}

/// <summary>
/// プロジェクト固有のバリデーションロジックを実装するためのインターフェース。
/// </summary>
public interface IProjectValidator
{
    /// <summary>
    /// プロジェクト名。
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// 指定エンティティのバリデーションを実行します。
    /// エラーメッセージのリストを返し、空の場合はバリデーション成功です。
    /// </summary>
    Task<IEnumerable<string>> ValidateAsync(string entity, IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx);
}

/// <summary>
/// プロジェクト固有のデータ変換ロジックを実装するためのインターフェース。
/// </summary>
public interface IProjectDataTransformer
{
    /// <summary>
    /// プロジェクト名。
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// 指定エンティティのデータを変換します。
    /// </summary>
    Task TransformAsync(string entity, IDictionary<string, object?> values, IDbConnection db, IDbTransaction? tx);
}

/// <summary>
/// CustomPages のページ行更新・削除に対するプロジェクト固有の検証・副作用処理。
/// ValidatePageUpdateAsync 内で追加フィールドの更新（スタンプ等）も実行できます。
/// その場合 PageRowMutationService は主フィールドのみを更新します。
/// </summary>
public interface IProjectPageMutationValidator
{
    /// <summary>プロジェクト名。</summary>
    string ProjectName { get; }

    /// <summary>
    /// ページ行更新時の検証・副作用処理。
    /// (ok: true, message: null) で検証成功。
    /// 追加フィールドのスタンプ等はここで db に直接実行してよい。
    /// </summary>
    Task<(bool ok, string? message)> ValidatePageUpdateAsync(
        string targetTable, int rowId, string field, string value,
        IDbConnection db, IDbTransaction? tx);

    /// <summary>
    /// ページ行削除時の検証。
    /// (ok: true, message: null) で検証成功。
    /// </summary>
    Task<(bool ok, string? message)> ValidatePageDeleteAsync(
        string targetTable, int rowId,
        IDbConnection db, IDbTransaction? tx);
}
