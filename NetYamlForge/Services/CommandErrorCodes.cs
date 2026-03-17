// ファイル概要: CRUD コマンド失敗時のエラーコード定数を定義します。
// CommandResult.Failure(code, message) の code 引数に使用し、エラー種別を文字列で統一管理します。
// HTTP ステータスへのマッピングは CommandErrorHttpMapper が担います。

namespace NetYamlForge.Services;

/// <summary>
/// CRUD 操作失敗時の標準エラーコード定数クラス。
/// Controller 層では CommandErrorHttpMapper を通じて HTTP ステータスに変換されます。
/// </summary>
public static class CommandErrorCodes
{
    /// <summary>新規作成前フック（BeforeCreate）が HookResult.Abort() を返した場合のコード。HTTP 422 に対応。</summary>
    public const string HookRejectedBeforeCreate = "hook_rejected_before_create";

    /// <summary>更新前フック（BeforeUpdate）が HookResult.Abort() を返した場合のコード。HTTP 422 に対応。</summary>
    public const string HookRejectedBeforeUpdate = "hook_rejected_before_update";

    /// <summary>削除前フック（BeforeDelete）が HookResult.Abort() を返した場合のコード。HTTP 422 に対応。</summary>
    public const string HookRejectedBeforeDelete = "hook_rejected_before_delete";

    /// <summary>
    /// UPDATE/DELETE 後に影響行数が 0 だった場合のコード。
    /// 楽観的排他制御の競合、またはレコードが既に削除済みであることを示す。
    /// IsConflict() により HTTP 409 Conflict にマッピングされる。
    /// </summary>
    public const string ConcurrencyConflictOrNotFound = "concurrency_conflict_or_not_found";
}
