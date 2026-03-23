// ファイル概要: CRUD フックのコンテキスト・操作種別・実行結果を定義します。

namespace NetYamlForge.Services.Hooks;

/// <summary>フックが対象とする CRUD 操作の種別</summary>
public enum CrudOperation
{
    Create,
    Update,
    Delete,
    CustomAction
}

/// <summary>
/// 前処理フック（BeforeAsync）の実行結果。
/// Abort() を返すと DB 操作がキャンセルされフォームにエラー表示されます。
/// </summary>
public class HookResult
{
    public bool Cancel { get; private set; }
    public string? CancelMessage { get; private set; }

    /// <summary>処理を続行する（正常）</summary>
    public static HookResult Continue() => new();

    /// <summary>処理をキャンセルし、指定メッセージをフォームに表示する</summary>
    public static HookResult Abort(string message) =>
        new() { Cancel = true, CancelMessage = message };
}

/// <summary>
/// フック実行時に渡されるコンテキスト情報。
/// BeforeAsync と AfterAsync の両方で共有されます。
/// </summary>
public class EntityHookContext
{
    /// <summary>対象エンティティ名（YAML キー）</summary>
    public string Entity { get; set; } = default!;

    /// <summary>操作種別（Create / Update）</summary>
    public CrudOperation Operation { get; set; }

    /// <summary>Update 時のレコード ID（Create 時は null、単一主鍵用）</summary>
    public int? Id { get; set; }

    /// <summary>複合主鍵の値マップ（Create 時は null、複合主鍵用）</summary>
    public Dictionary<string, object?>? KeyValues { get; set; }

    /// <summary>フォームから変換済みの値マップ</summary>
    public Dictionary<string, object?> Values { get; set; } = new();

    /// <summary>操作を実行したユーザー名</summary>
    public string? UserName { get; set; }

    /// <summary>BeforeAsync → AfterAsync 間でデータを受け渡す自由領域</summary>
    public Dictionary<string, object?> Data { get; set; } = new();
}
