// ファイル概要: フック名からフック実装を検索するレジストリインターフェースを定義します。

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// フック名（文字列）から IEntityHook 実装を取得するレジストリ。
/// DI に登録された全 IEntityHook を管理します。
/// フック名は "hookName:param1,param2" の形式もサポートします。
/// </summary>
public interface IEntityHookRegistry
{
    /// <summary>
    /// フック名に対応するフックを返します。
    /// 見つからない場合は null を返します（登録漏れ時はログ警告を出すこと）。
    /// フック名にパラメータが含まれる場合（例："validate_email:Email,Phone"）、
    /// パラメータ部分は context.Data["__hookConfig"] に設定されます。
    /// </summary>
    IEntityHook? Find(string hookNameWithConfig, EntityHookContext? context = null);
}
