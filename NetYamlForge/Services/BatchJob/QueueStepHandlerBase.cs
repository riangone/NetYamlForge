// ファイル概要: 「待機行を取得 → 1 行ずつ処理 → 成否を書き戻す」型バッチステップの共通基底。
// 状態遷移・リトライ計数・キャンセル・集計は基底が担い、派生は 3 つの抽象を実装します。

using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 行の処理結果。
/// </summary>
public enum RowStatus { Ok, Failed, Skipped }

/// <summary>
/// 行の処理結果を表すレコード。
/// </summary>
public sealed record RowOutcome(RowStatus Status, string? Reason = null, object? Payload = null)
{
    public static RowOutcome Ok(object? payload = null) => new(RowStatus.Ok, null, payload);
    public static RowOutcome Fail(string reason) => new(RowStatus.Failed, reason);
    public static RowOutcome Skip(string reason) => new(RowStatus.Skipped, reason);
}

/// <summary>
/// 「待機行を取得 → 1 行ずつ処理 → 成否を書き戻す」型バッチステップの共通基底。
/// 状態遷移・リトライ計数・キャンセル・集計は基底が担い、派生は 3 つの抽象を実装します。
/// </summary>
public abstract class QueueStepHandlerBase<TRow> : IBatchStepHandler
{
    /// <summary> BatchJobDefinition.Type に対応する文字列（小文字）。</summary>
    public abstract string StepType { get; }

    /// <summary> デフォルトのバッチサイズ。派生で上書き可能。</summary>
    protected virtual int DefaultBatchSize => 5;

    /// <summary> 処理対象行を取得（LIMIT batchSize は派生側 SQL に含める）。</summary>
    protected abstract Task<IReadOnlyList<TRow>> FetchPendingAsync(
        BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        int batchSize, CancellationToken ct);

    /// <summary> 1 行処理。RowOutcome.Ok / Fail(reason) / Skip(reason) を返す。</summary>
    protected abstract Task<RowOutcome> ProcessRowAsync(
        TRow row, BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        CancellationToken ct);

    /// <summary> 行を processing 状態にマークする（既定は何もしない）。</summary>
    protected virtual Task MarkProcessingAsync(TRow row, BatchJobDefinition job, IDbConnection db, IDbTransaction tx)
        => Task.CompletedTask;

    /// <summary> 成功/失敗の書き戻し（retry_count 加算・エラーメッセージ保存など）。</summary>
    protected abstract Task WriteOutcomeAsync(TRow row, RowOutcome outcome, BatchJobDefinition job, IDbConnection db, IDbTransaction tx);

    /// <summary> ロガー。派生クラス of 派生クラスで設定してください。</summary>
    protected ILogger Logger { get; set; } = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var batchSize = job.BatchSize > 0 ? job.BatchSize : DefaultBatchSize;

        var rows = await FetchPendingAsync(job, projectName, db, tx, batchSize, ct);

        if (rows.Count == 0)
        {
            result.Success = true;
            result.RowsAffected = 0;
            return;
        }

        var done = 0;
        var failed = 0;

        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await MarkProcessingAsync(row, job, db, tx);

                var outcome = await ProcessRowAsync(row, job, projectName, db, tx, ct);

                await WriteOutcomeAsync(row, outcome, job, db, tx);

                switch (outcome.Status)
                {
                    case RowStatus.Ok:
                        done++;
                        break;
                    case RowStatus.Failed:
                        failed++;
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unhandled exception processing row in {StepType}", StepType);
                try
                {
                    await WriteOutcomeAsync(row, RowOutcome.Fail(ex.Message), job, db, tx);
                }
                catch (Exception writeEx)
                {
                    Logger.LogError(writeEx, "Failed to write outcome for row in {StepType}", StepType);
                }
                failed++;
            }
        }

        result.Success = failed == 0 || done > 0;
        result.RowsAffected = done;
        if (failed > 0)
            result.ErrorMessage = $"{failed} row(s) failed in {StepType}";
    }
}
