// ファイル概要: カスタムアクションハンドラーのインターフェースとコンテキストを定義します。

using System.Data;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// カスタムアクション実行時に渡されるコンテキスト情報。
/// </summary>
public class CustomActionContext
{
    /// <summary>対象プロジェクト名</summary>
    public string Project { get; set; } = default!;

    /// <summary>対象エンティティ名（YAML キー）</summary>
    public string Entity { get; set; } = default!;

    /// <summary>アクション名（YAML キー）</summary>
    public string Action { get; set; } = default!;

    /// <summary>対象レコードの主鍵値</summary>
    public string? RecordId { get; set; }

    /// <summary>アクションフォームからの入力値マップ</summary>
    public Dictionary<string, object?> Inputs { get; set; } = new();

    /// <summary>操作を実行したユーザー名</summary>
    public string? UserName { get; set; }

    /// <summary>ハンドラー間でデータを受け渡す自由領域</summary>
    public Dictionary<string, object?> Data { get; set; } = new();
}

/// <summary>
/// カスタムアクションの実行結果。
/// </summary>
public class ActionHandlerResult
{
    public bool Ok { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static ActionHandlerResult Success() => new() { Ok = true };
    public static ActionHandlerResult Failure(string message) => new() { Ok = false, ErrorMessage = message };
}

/// <summary>
/// カスタムアクションを実装するインターフェース。
/// projects/&lt;name&gt;/Hooks/ に配置すると自動的に読み込まれます。
/// </summary>
public interface ICustomActionHandler
{
    /// <summary>YAML の actions.*.handler で参照されるハンドラー名</summary>
    string Name { get; }

    /// <summary>
    /// アクションを実行します。トランザクション内で呼ばれるため、
    /// tx を使って同一 Tx の DB 操作が可能です。
    /// </summary>
    Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx);
}
