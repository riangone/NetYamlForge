using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI;

/// <summary>
/// システム設定画面の「既定モデル」ドロップダウンを、CLI に直接問い合わせて最新化するためのサービス。
///
/// antigravity / opencode はどちらも `models` サブコマンドで実行環境にインストール済みのモデル一覧を
/// 返す（`--help` および実機実行で確認済み、2026-07-08）。ハードコードした一覧は将来 CLI の
/// バージョンアップやユーザーの provider 設定変更で陳腐化するため、まずライブ実行を試み、
/// 失敗した場合のみ <see cref="KnownCliProviders"/> の検証済みフォールバック値を返す。
///
/// claude には一覧サブコマンドが無い（`claude models` はヘルプ画面にフォールバックすることを確認済み）ため、
/// エイリアス（fable/opus/sonnet）を静的カタログとしてそのまま返す。
/// </summary>
public interface ICliModelCatalogService
{
    Task<CliModelCatalogResult> GetModelsAsync(string provider, CancellationToken cancellationToken = default);
}

public record CliModelCatalogResult(bool Success, IReadOnlyList<string> Models, string Source, string? Error);

public class CliModelCatalogService : ICliModelCatalogService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

    private readonly IOptionsMonitor<CliChainOptions> _optionsMonitor;
    private readonly ILogger<CliModelCatalogService> _logger;

    public CliModelCatalogService(IOptionsMonitor<CliChainOptions> optionsMonitor, ILogger<CliModelCatalogService> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<CliModelCatalogResult> GetModelsAsync(string provider, CancellationToken cancellationToken = default)
    {
        var normalized = provider?.Trim().ToLowerInvariant() ?? "";

        if (normalized == "claude")
        {
            // claude に一覧サブコマンドは無い（実機確認済み）。エイリアスは変わらないので静的カタログを返す。
            return new CliModelCatalogResult(true, KnownCliProviders.ClaudeModelAliases.ToList(), "static", null);
        }

        if (normalized != "antigravity" && normalized != "opencode")
        {
            return new CliModelCatalogResult(false, [], "unsupported",
                "この CLI はモデル一覧の自動取得に対応していません。Command/ArgsTemplate 欄を参照して手入力してください。");
        }

        var options = _optionsMonitor.CurrentValue;
        var command = options.Providers.TryGetValue(normalized, out var cfg) && !string.IsNullOrWhiteSpace(cfg.Command)
            ? cfg.Command
            : normalized;

        try
        {
            var (output, error) = await RunProcessAsync(command, "models", QueryTimeout, cancellationToken);
            var models = ParseLines(output);
            if (models.Count > 0)
            {
                return new CliModelCatalogResult(true, models, "live", null);
            }

            _logger.LogWarning("{Command} models returned no usable output: {Error}", command, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run '{Command} models'", command);
        }

        // ライブ取得に失敗した場合は検証済みフォールバックのみ返す（antigravity のみ用意がある）。
        if (normalized == "antigravity")
        {
            return new CliModelCatalogResult(true, KnownCliProviders.AntigravityFallbackModels.ToList(), "fallback",
                $"'{command} models' の実行に失敗したため、2026-07-08 に確認済みの一覧を表示しています。");
        }

        return new CliModelCatalogResult(false, [], "unavailable",
            $"'{command} models' を実行できませんでした。CLI が PATH に無いか、未ログインの可能性があります。");
    }

    private static List<string> ParseLines(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput)) return [];

        var cleaned = Regex.Replace(rawOutput, @"\x1B\[[0-9;]*[a-zA-Z]", "");
        return cleaned
            .Split('\n')
            .Select(l => l.Trim().TrimStart('-', '*', '•').Trim())
            .Where(l => l.Length > 0)
            .Distinct()
            .ToList();
    }

    private static async Task<(string? Output, string? Error)> RunProcessAsync(
        string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return (null, $"起動失敗: {ex.Message}");
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            return (null, $"{timeout.TotalSeconds}秒でタイムアウト");
        }

        if (process.ExitCode != 0)
        {
            return (null, $"exit code {process.ExitCode}: {errorBuilder}".Trim());
        }

        return (outputBuilder.ToString(), null);
    }
}
