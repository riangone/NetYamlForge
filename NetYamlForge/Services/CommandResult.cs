// ファイル概要: CRUD コマンドの実行結果を表す統一型を定義します。
// DynamicEntityCommandService の Create/Update/Delete が返す戻り値型として使用します。
// エラー時は CommandError にコードとメッセージを持ち、Controller でHTTPステータスに変換します。

namespace NetYamlForge.Services;

/// <summary>
/// CRUD コマンド失敗時のエラー情報。
/// Code は CommandErrorCodes の定数、Message はユーザー向けの説明文。
/// </summary>
public sealed record CommandError(string Code, string Message);

/// <summary>
/// 戻り値なしの CRUD コマンド結果（Update / Delete 用）。
/// <para>Ok = true なら成功、false なら Error に失敗詳細が入る。</para>
/// </summary>
public sealed record CommandResult(bool Ok, CommandError? Error = null)
{
    /// <summary>操作成功を表す CommandResult を生成します。</summary>
    public static CommandResult Success() => new(true);

    /// <summary>指定エラーコードとメッセージで失敗結果を生成します。</summary>
    public static CommandResult Failure(string code, string message) =>
        new(false, new CommandError(code, message));
}

/// <summary>
/// 戻り値ありの CRUD コマンド結果（Create 用: 新規 ID を返す）。
/// <para>Ok = true のとき Value に結果値が入る。false のとき Error に失敗詳細が入る。</para>
/// </summary>
public sealed record CommandResult<T>(bool Ok, T? Value = default, CommandError? Error = null)
{
    /// <summary>指定値を持つ成功結果を生成します。</summary>
    public static CommandResult<T> Success(T value) => new(true, value);

    /// <summary>指定エラーコードとメッセージで失敗結果を生成します。Value は default になります。</summary>
    public static CommandResult<T> Failure(string code, string message) =>
        new(false, default, new CommandError(code, message));
}
