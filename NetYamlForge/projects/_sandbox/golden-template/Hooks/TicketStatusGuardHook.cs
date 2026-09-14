// 責務: GoldenTemplate プロジェクト固有の「ticket_status_guard」フックを実装する。
// entities.yml の hooks.beforeCreate / hooks.beforeUpdate に「ticket_status_guard」と記述して使用。

using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.GoldenTemplate.Hooks;

/// <summary>
/// GoldenTemplate 固有フック: ticket_status_guard
/// <para>entities.yml での使用例:</para>
/// <code>
///   hooks:
///     beforeCreate: [ticket_status_guard]
///     beforeUpdate: [ticket_status_guard]
/// </code>
/// </summary>
public sealed class TicketStatusGuardHook : IEntityHook
{
    private readonly ILogger<TicketStatusGuardHook> _logger;

    public string Name => "ticket_status_guard";

    public TicketStatusGuardHook(ILogger<TicketStatusGuardHook> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ctx.Values   : フォームフィールド値 Dictionary&lt;string, object?&gt;
    /// ctx.Operation: CrudOperation.Create / Update / Delete
    /// ctx.Entity   : エンティティ名
    /// </remarks>
    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // TODO: バリデーション・変換ロジックを実装する
        //
        // 例: フィールド値を取得
        // if (!ctx.Values.TryGetValue("fieldName", out var raw))
        //     return Task.FromResult(HookResult.Continue());
        //
        // 例: バリデーション失敗時
        // return Task.FromResult(HookResult.Abort("エラーメッセージ"));
        //
        // 例: DB 参照（同一トランザクション内）
        // var count = await db.ExecuteScalarAsync<int>(
        //     "SELECT COUNT(*) FROM table WHERE col = @val",
        //     new { val = raw }, tx);

        return Task.FromResult(HookResult.Continue());
    }

    /// <inheritdoc />
    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // TODO: 書き込み成功後の後処理（通知・連携等）を実装する（任意）
        return Task.CompletedTask;
    }
}