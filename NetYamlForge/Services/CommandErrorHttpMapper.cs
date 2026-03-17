// ファイル概要: CommandError のコードを HTTP セマンティクスに変換するマッパーです。
// Controller 層で CommandResult の失敗種別を判定し、適切な HTTP ステータスコードを
// 返すために使用します。新しいエラーコードが追加されたときはここに判定メソッドを追加します。

namespace NetYamlForge.Services;

/// <summary>
/// CommandError のコード値を HTTP レスポンスのセマンティクスに変換するユーティリティ。
/// <para>
/// 使用例（Controller）:
/// <code>
/// if (_commandErrorHttpMapper.IsConflict(result.Error))
///     return Conflict(result.Error?.Message);
/// </code>
/// </para>
/// </summary>
public sealed class CommandErrorHttpMapper
{
    /// <summary>
    /// エラーが楽観的排他制御の競合（対象レコードが更新済み・削除済み）かどうかを判定します。
    /// true の場合、Controller は HTTP 409 Conflict を返すべきです。
    /// </summary>
    public bool IsConflict(CommandError? error)
    {
        return string.Equals(
            error?.Code,
            CommandErrorCodes.ConcurrencyConflictOrNotFound,
            StringComparison.Ordinal);
    }
}
