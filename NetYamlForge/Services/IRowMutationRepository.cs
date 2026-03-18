// ファイル概要: 行レベルの INSERT / UPDATE / DELETE SQL 生成・実行の共通インターフェース。
// DynamicCrudRepository（Entity側）と PageRowMutationService（Section側）が持つ
// 同一構造の SQL 生成ロジックをアダプター構造で統一します。
// 実装クラス: RowMutationRepository

using System.Data;

namespace NetYamlForge.Services;

/// <summary>
/// 動的テーブルへの行単位ミューテーション（INSERT / UPDATE / DELETE）を抽象化するインターフェース。
/// テーブル名・主キー・フィールドリストをパラメータとして受け取り、安全な SQL を生成・実行します。
/// すべての識別子は呼び出し前に SqlSafetyGuard.IdentifierRegex で検証済みであること。
/// </summary>
public interface IRowMutationRepository
{
    /// <summary>
    /// 指定テーブルに行を挿入します。
    /// </summary>
    /// <param name="table">挿入先テーブル名（検証済み識別子）</param>
    /// <param name="fields">列名→値のペアリスト（列名は検証済み識別子）</param>
    /// <param name="tx">外部トランザクション（省略時は自動コミット）</param>
    /// <returns>影響行数</returns>
    Task<int> InsertAsync(
        string table,
        IReadOnlyList<KeyValuePair<string, object?>> fields,
        IDbTransaction? tx = null);

    /// <summary>
    /// 指定テーブルの単一行を主キーで更新します。
    /// </summary>
    /// <param name="table">更新対象テーブル名（検証済み識別子）</param>
    /// <param name="primaryKeyColumn">主キー列名（検証済み識別子）</param>
    /// <param name="primaryKeyValue">主キー値</param>
    /// <param name="fields">更新する列名→値のペアリスト（列名は検証済み識別子、主キー列を除く）</param>
    /// <param name="tx">外部トランザクション（省略時は自動コミット）</param>
    /// <returns>影響行数</returns>
    Task<int> UpdateAsync(
        string table,
        string primaryKeyColumn,
        object primaryKeyValue,
        IReadOnlyList<KeyValuePair<string, object?>> fields,
        IDbTransaction? tx = null);

    /// <summary>
    /// 指定テーブルの単一行を主キーで削除します。
    /// </summary>
    /// <param name="table">削除対象テーブル名（検証済み識別子）</param>
    /// <param name="primaryKeyColumn">主キー列名（検証済み識別子）</param>
    /// <param name="primaryKeyValue">主キー値</param>
    /// <param name="tx">外部トランザクション（省略時は自動コミット）</param>
    /// <returns>影響行数</returns>
    Task<int> DeleteAsync(
        string table,
        string primaryKeyColumn,
        object primaryKeyValue,
        IDbTransaction? tx = null);
}
