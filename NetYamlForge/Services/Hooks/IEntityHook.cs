// ファイル概要: エンティティ CRUD フックのインターフェースを定義します。

using System.Data;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// エンティティの新規作成・更新時に前後処理を挟むフックインターフェース。
/// entities.yml の hooks.beforeCreate 等にフック名を指定して登録します。
/// </summary>
public interface IEntityHook
{
    /// <summary>
    /// YAML の hooks.beforeCreate / hooks.beforeUpdate で参照されるフック名。
    /// 大文字小文字を区別しません。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// DB 書き込み前に実行される前処理。
    /// HookResult.Abort() を返すと DB 操作がキャンセルされます。
    /// トランザクション内で呼ばれるため、tx を使って同一 Tx の DB 操作が可能です。
    /// </summary>
    Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx);

    /// <summary>
    /// DB 書き込み後（同一トランザクション内）に実行される後処理。
    /// 例外を投げるとトランザクションがロールバックされます。
    /// </summary>
    Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx);
}
