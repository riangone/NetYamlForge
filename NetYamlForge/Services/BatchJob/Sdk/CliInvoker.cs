using System;
using System.Threading;
using System.Threading.Tasks;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob.Sdk;

/// <summary>
/// CLI 呼び出しの実行を統一管理し、タイムアウトとフォールバックを適用する。
/// </summary>
public static class CliInvoker
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    public static async Task<CliChainResult> RunCliAsync(
        ICliChainService cliService,
        string prompt,
        string? imagePath = null,
        string? projectName = null,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeout);

        try
        {
            var result = await cliService.PromptAsync(prompt, imagePath, projectName, cancellationToken: cts.Token);
            return result;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new CliChainResult(false, null, null, "CLI request timed out");
        }
        catch (Exception ex)
        {
            return new CliChainResult(false, null, null, $"CLI execution error: {ex.Message}");
        }
    }
}
