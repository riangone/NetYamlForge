using System.Data;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// AI バッチ executor のテンプレートメソッド基底クラス。
/// 「データ読込 → プロンプト構築 → CLI/AI 呼び出し → 結果解析 → 永続化」の共通フローを
/// 固化し、タイムアウト・fallback・状態回写・例外归一を基底で処理します。
/// 子クラスは BuildPrompt / ParseResult / PersistAsync のみ実装します。
/// </summary>
public abstract class AiBatchExecutorBase<TInput, TResult> : IBatchStepHandler
{
    public abstract string StepType { get; }

    protected readonly ICliChainService Cli;
    protected readonly ILogger Logger;

    protected AiBatchExecutorBase(ICliChainService cli, ILogger logger)
    {
        Cli = cli ?? throw new ArgumentNullException(nameof(cli));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken ct)
    {
        try
        {
            var items = await LoadItemsAsync(job, projectName, db, tx, ct);
            if (items.Count == 0)
            {
                result.Success = true;
                result.RowsAffected = 0;
                return;
            }

            var done = 0;
            var failed = 0;

            foreach (var input in items)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var itemResult = await ExecuteItemAsync(input, job, projectName, db, tx, ct);
                    if (itemResult.IsSuccess)
                        done++;
                    else
                        failed++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unhandled error in {StepType} item processing", StepType);
                    failed++;
                }
            }

            result.Success = failed == 0 || done > 0;
            result.RowsAffected = done;
            if (failed > 0)
                result.ErrorMessage = $"{failed} item(s) failed in {StepType}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Fatal error in {StepType}", StepType);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ErrorDetail = ex.ToString();
        }
        finally
        {
            result.EndedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 処理対象のアイテムを一括取得します。
    /// デフォルトでは LoadInputAsync を呼び出して単一アイテムを返します。
    /// 複数アイテムを処理する場合はオーバーライドしてください。
    /// </summary>
    protected virtual async Task<IReadOnlyList<TInput>> LoadItemsAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        var input = await LoadInputAsync(job, projectName, db, tx, ct);
        return input != null ? new[] { input } : Array.Empty<TInput>();
    }

    /// <summary>単一アイテムの入力データを読み込みます。</summary>
    protected abstract Task<TInput?> LoadInputAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct);

    /// <summary>入力データからプロンプトを構築します。</summary>
    protected abstract string BuildPrompt(TInput input);

    /// <summary>CLI/AI の生のレスポンステキストを解析して結果型に変換します。</summary>
    protected abstract TResult? ParseResult(string raw);

    /// <summary>処理結果をデータベースに永続化します。</summary>
    protected abstract Task PersistAsync(
        TInput input, TResult result,
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct);

    /// <summary>CLI 呼び出しに使用するプロンプト（画像パス付き）。デフォルトはテキストのみ。</summary>
    protected virtual string? GetImagePath(TInput input) => null;

    /// <summary>処理結果のサマリーログメッセージ。null でログをスキップ。</summary>
    protected virtual string? GetSuccessLogMessage(TInput input, TResult result) => null;

    private async Task<ItemResult> ExecuteItemAsync(
        TInput input,
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        var prompt = BuildPrompt(input);
        var imagePath = GetImagePath(input);

        var cliResult = await Cli.PromptAsync(prompt, imagePath: imagePath, projectName: projectName, cancellationToken: ct);

        if (!cliResult.Success || string.IsNullOrWhiteSpace(cliResult.Text))
        {
            Logger.LogWarning("AI returned empty response for {StepType}", StepType);
            return ItemResult.Fail("AI returned empty or failed response");
        }

        var parsed = ParseResult(cliResult.Text);
        if (parsed == null)
        {
            Logger.LogWarning("Failed to parse AI response for {StepType}", StepType);
            return ItemResult.Fail("Failed to parse AI response");
        }

        await PersistAsync(input, parsed, job, projectName, db, tx, ct);

        var logMsg = GetSuccessLogMessage(input, parsed);
        if (logMsg != null)
            Logger.LogInformation("{StepType}: {Message}", StepType, logMsg);

        return ItemResult.Ok();
    }

    private readonly record struct ItemResult(bool IsSuccess, string? Error)
    {
        public static ItemResult Ok() => new(true, null);
        public static ItemResult Fail(string error) => new(false, error);
    }
}
