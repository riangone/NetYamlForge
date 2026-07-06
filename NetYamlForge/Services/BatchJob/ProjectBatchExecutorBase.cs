using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob.Sdk;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// プロジェクト側 BatchExecutor が継承する推奨基底クラス。
/// ICliChainService をラップしたタイムアウト/フォールバック適用済みの CLI 実行と、AI 出力の JSON パース機能を提供します。
/// </summary>
public abstract class ProjectBatchExecutorBase<TInput, TResult> : AiBatchExecutorBase<TInput, TResult>
{
    protected ProjectBatchExecutorBase(ICliChainService cli, ILogger logger)
        : base(cli, logger)
    {
    }

    /// <summary>
    /// CLI チェーンを実行し、最初の成功結果を返します（タイムアウト・フォールバック適用済み）。
    /// </summary>
    protected Task<CliChainResult> RunCliAsync(
        string prompt,
        string? imagePath = null,
        string? projectName = null,
        CancellationToken ct = default)
    {
        return CliInvoker.RunCliAsync(Cli, prompt, imagePath, projectName, ct);
    }

    /// <summary>
    /// AI レスポンスの Markdown 囲み枠を除去し、JSON を TResult 型に変換します。
    /// </summary>
    protected bool TryParseJson(string raw, out TResult? result, out string? error)
    {
        return AiResultParser.TryParseJson(raw, out result, out error);
    }
}
