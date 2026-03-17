// 図書館管理システム用カスタムフック実装

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Library.Hooks;

/// <summary>
/// [図書館] 貸出時に蔵書の在庫確認を行うフック。
/// 在庫がない場合は貸出を中止。
///
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: [validate_available_book]
/// </summary>
public class ValidateAvailableBookHook : IEntityHook
{
    private readonly ILogger<ValidateAvailableBookHook> _logger;

    public ValidateAvailableBookHook(ILogger<ValidateAvailableBookHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_available_book";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // loan.beforeCreate で呼ばれる
        // 必要な値: book_id, patron_id, loan_date, due_date

        if (!ctx.Values.TryGetValue("book_id", out var bookIdObj) || bookIdObj == null)
            return HookResult.Abort("蔵書が指定されていません。");

        if (!int.TryParse(bookIdObj.ToString(), out var bookId))
            return HookResult.Abort("無効な蔵書IDです。");

        // DB から book の available_count を確認
        var availableCount = await db.ExecuteScalarAsync<int>(
            "SELECT available_count FROM book WHERE id = @BookId",
            new { BookId = bookId },
            tx
        );

        if (availableCount <= 0)
        {
            _logger.LogWarning("[Hook:validate_available_book] 在庫なし: book_id={BookId}", bookId);
            return HookResult.Abort("この蔵書は現在利用できません。");
        }

        _logger.LogInformation("[Hook:validate_available_book] 在庫確認OK: book_id={BookId}, available={Count}", bookId, availableCount);
        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [図書館] 貸出期限を自動設定するフック。
/// loan_date から 14日後を due_date として自動セット。
///
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: [set_due_date]
/// </summary>
public class SetDueDateHook : IEntityHook
{
    private readonly ILogger<SetDueDateHook> _logger;

    public SetDueDateHook(ILogger<SetDueDateHook> logger)
    {
        _logger = logger;
    }

    public string Name => "set_due_date";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("loan_date", out var loanDateObj) || loanDateObj == null)
            return Task.FromResult(HookResult.Abort("貸出日が指定されていません。"));

        if (!DateTime.TryParse(loanDateObj.ToString(), out var loanDate))
            return Task.FromResult(HookResult.Abort("無効な貸出日です。"));

        // loan_date + 14日 を due_date として設定
        var dueDate = loanDate.AddDays(14);
        ctx.Values["due_date"] = dueDate;

        _logger.LogInformation("[Hook:set_due_date] 返却期限を自動設定: loan_date={LoanDate}, due_date={DueDate}", loanDate.Date, dueDate.Date);
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [図書館] 貸出時に蔵書の利用可能数を自動デクリメント。
///
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: [decrease_available_count]
/// </summary>
public class DecreaseAvailableCountHook : IEntityHook
{
    private readonly ILogger<DecreaseAvailableCountHook> _logger;

    public DecreaseAvailableCountHook(ILogger<DecreaseAvailableCountHook> logger)
    {
        _logger = logger;
    }

    public string Name => "decrease_available_count";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("book_id", out var bookIdObj) || bookIdObj == null)
            return HookResult.Continue();

        if (!int.TryParse(bookIdObj.ToString(), out var bookId))
            return HookResult.Continue();

        // Safe: bookId は validate_available_book で既に検証済み
#pragma warning disable DCS001
        var sql = "UPDATE book SET available_count = available_count - 1 WHERE id = @BookId";
#pragma warning restore DCS001
        await db.ExecuteAsync(sql, new { BookId = bookId }, tx);

        _logger.LogInformation("[Hook:decrease_available_count] 利用可能数をデクリメント: book_id={BookId}", bookId);
        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [図書館] 返却時に蔵書の利用可能数を自動インクリメント。
///
/// entities.yml での使用例:
///   hooks:
///     afterUpdate: [increase_available_count_on_return]
/// </summary>
public class IncreaseAvailableCountOnReturnHook : IEntityHook
{
    private readonly ILogger<IncreaseAvailableCountOnReturnHook> _logger;

    public IncreaseAvailableCountOnReturnHook(ILogger<IncreaseAvailableCountOnReturnHook> logger)
    {
        _logger = logger;
    }

    public string Name => "increase_available_count_on_return";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 返却時（status が returned に更新された場合）のみ実行
        if (!ctx.Values.TryGetValue("status", out var statusObj) || statusObj?.ToString() != "returned")
            return;

        if (!ctx.Values.TryGetValue("book_id", out var bookIdObj) || bookIdObj == null)
            return;

        if (!int.TryParse(bookIdObj.ToString(), out var bookId))
            return;

        // Safe: bookId はエンティティの主キー参照なので安全
#pragma warning disable DCS001
        var sql = "UPDATE book SET available_count = available_count + 1 WHERE id = @BookId";
#pragma warning restore DCS001
        await db.ExecuteAsync(sql, new { BookId = bookId }, tx);

        _logger.LogInformation("[Hook:increase_available_count_on_return] 利用可能数をインクリメント: book_id={BookId}", bookId);
    }
}

/// <summary>
/// [図書館] 返却状態遷移ルール検証。
/// active → returned のみ許可。
///
/// entities.yml での使用例:
///   hooks:
///     beforeUpdate: [validate_loan_status_transition]
/// </summary>
public class ValidateLoanStatusTransitionHook : IEntityHook
{
    private readonly ILogger<ValidateLoanStatusTransitionHook> _logger;

    public ValidateLoanStatusTransitionHook(ILogger<ValidateLoanStatusTransitionHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_loan_status_transition";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // loan の update 時に呼ばれる
        if (!ctx.Id.HasValue)
            return HookResult.Continue();

        if (!ctx.Values.TryGetValue("status", out var newStatusObj) || newStatusObj == null)
            return HookResult.Continue();

        var newStatus = newStatusObj.ToString()?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(newStatus))
            return HookResult.Continue();

        // 現在の status を DB から取得
        var currentStatus = await db.ExecuteScalarAsync<string>(
            "SELECT status FROM loan WHERE id = @Id",
            new { Id = ctx.Id.Value },
            tx
        );

        currentStatus = currentStatus?.ToLowerInvariant();

        // ルール: active → returned のみ許可
        if (currentStatus == "active" && newStatus == "returned")
        {
            _logger.LogInformation("[Hook:validate_loan_status_transition] 返却OK: loan_id={LoanId}, {Current} → {New}", ctx.Id, currentStatus, newStatus);
            return HookResult.Continue();
        }

        // 他の遷移は許可しない
        _logger.LogWarning("[Hook:validate_loan_status_transition] 無効な状態遷移: loan_id={LoanId}, {Current} → {New}", ctx.Id, currentStatus, newStatus);
        return HookResult.Abort($"状態遷移が許可されていません ({currentStatus} → {newStatus})。");
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [図書館] 返却通知フック。
/// 実装: ここではログ出力でシミュレート（実際にはメール送信等）
///
/// entities.yml での使用例:
///   hooks:
///     afterUpdate: [notify_patron]
/// </summary>
public class NotifyPatronHook : IEntityHook
{
    private readonly ILogger<NotifyPatronHook> _logger;

    public NotifyPatronHook(ILogger<NotifyPatronHook> logger)
    {
        _logger = logger;
    }

    public string Name => "notify_patron";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 返却確認時（status = returned）のみ
        if (!ctx.Values.TryGetValue("status", out var statusObj) || statusObj?.ToString() != "returned")
            return;

        if (!ctx.Id.HasValue)
            return;

        // 利用者情報を取得してメール送信（ここではログ出力でシミュレート）
        var patronEmail = await db.ExecuteScalarAsync<string>(
            @"SELECT p.email FROM loan l
              JOIN patron p ON l.patron_id = p.id
              WHERE l.id = @LoanId",
            new { LoanId = ctx.Id.Value },
            tx
        );

        _logger.LogInformation("[Hook:notify_patron] 返却通知を送信: loan_id={LoanId}, email={Email}", ctx.Id, patronEmail ?? "不明");
        // 実際にはここでメール送信: await _emailService.SendReturnNotificationAsync(patronEmail);
    }
}
