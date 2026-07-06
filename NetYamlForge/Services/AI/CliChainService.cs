using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 複数の対話型 AI CLI（opencode / antigravity / claude code）を優先順位付きで
/// 順番に試行し、最初に成功したものの結果を返す「CLI フォールバックチェーン」サービス。
///
/// 背景：単一の CLI（例：antigravity）に依存すると、その CLI の
/// OAuth ログイン期限切れ・レート制限・一時的な不調が発生した際に
/// 機能全体がサイレントに止まってしまう（実際に antigravity の
/// Google OAuth 期限切れで日記画像の自動標注が止まった事例あり）。
/// このサービスは同じ役割を果たせる複数の CLI を優先順位順に試し、
/// 1 つが失敗しても次にフォールバックすることで可用性を高める。
///
/// 課金方式について：API Key 方式（gemini/anthropic 等の従量課金 API）は
/// 使用量・費用の上限を管理しづらいため、意図的にチェーン対象から除外し、
/// サブスクリプション型のエージェント CLI のみを対象にしている。
/// </summary>
public interface ICliChainService
{
    /// <summary>
    /// プロンプトを優先順位順に各 CLI へ投げ、最初に成功した結果を返す。
    /// </summary>
    /// <param name="prompt">送信するプロンプト本文</param>
    /// <param name="imagePath">画像を解析させたい場合は絶対パスを指定する（各 CLI 自身のファイル閲覧ツールを使わせる）</param>
    /// <param name="projectName">プロジェクト名。projects/{name}/.env の CLI_CHAIN_ORDER / CLI_CHAIN_ENABLED でオーバーライド可能</param>
    /// <param name="preferredProvider">
    /// 画面などでユーザーが明示的に選択した CLI プロバイダー（opencode / antigravity / claude）。
    /// 指定された場合、そのプロバイダーをチェーン先頭に並べ替えて最優先で試す。
    /// 残りのプロバイダーは可用性フォールバックとして後段に残す（ハードコードした固定順序は使わない）。
    /// </param>
    /// <param name="model">preferredProvider に渡すモデル名（--model）。フォールバック時の他プロバイダーには適用しない。</param>
    /// <param name="variant">opencode 用のバリアント（--variant、AI レベル相当）。opencode が preferredProvider の場合のみ適用。</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<CliChainResult> PromptAsync(
        string prompt,
        string? imagePath = null,
        string? projectName = null,
        string? preferredProvider = null,
        string? model = null,
        string? variant = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// CLI チェーンの実行結果。Success が false の場合、Text は null。
/// </summary>
public record CliChainResult(bool Success, string? Text, string? Provider, string? Error);

public class CliChainService : ICliChainService
{
    /// <summary>ユーザー指定の既定優先順位：opencode → antigravity → claude code</summary>
    private static readonly string[] DefaultOrder = ["opencode", "antigravity", "claude"];
    private static readonly TimeSpan PerProviderTimeout = TimeSpan.FromSeconds(90);

    private readonly ILogger<CliChainService> _logger;

    public CliChainService(ILogger<CliChainService> logger)
    {
        _logger = logger;
    }

    public async Task<CliChainResult> PromptAsync(
        string prompt,
        string? imagePath = null,
        string? projectName = null,
        string? preferredProvider = null,
        string? model = null,
        string? variant = null,
        CancellationToken cancellationToken = default)
    {
        var env = LoadProjectEnv(projectName);

        // 機能全体を無効化するオプション（"设置为可选" 対応）。既定は有効。
        if (env.TryGetValue("CLI_CHAIN_ENABLED", out var enabledRaw)
            && bool.TryParse(enabledRaw, out var enabled) && !enabled)
        {
            _logger.LogInformation("CLI chain is disabled via CLI_CHAIN_ENABLED=false (project: {Project})", projectName);
            return new CliChainResult(false, null, null, "CLI chain disabled");
        }

        var order = ResolveOrder(env);

        // ユーザーが画面で明示選択したプロバイダーを最優先にする（固定順序をハードコードしない）。
        // 選択したプロバイダーが不調でも機能が止まらないよう、残りは後段のフォールバックとして維持する。
        var normalizedPreferred = NormalizeProvider(preferredProvider);
        if (normalizedPreferred != null)
        {
            var list = order.ToList();
            list.Remove(normalizedPreferred);
            list.Insert(0, normalizedPreferred);
            order = list.ToArray();
        }
        else if (!string.IsNullOrWhiteSpace(imagePath))
        {
            // 明示選択が無い場合のみ、画像解析に強い antigravity を先頭に寄せる従来挙動を維持。
            var list = order.ToList();
            if (list.Contains("antigravity"))
            {
                list.Remove("antigravity");
                list.Insert(0, "antigravity");
            }
            order = list.ToArray();
        }

        var fullPrompt = string.IsNullOrWhiteSpace(imagePath)
            ? prompt
            : $"请使用你自己的文件查看工具打开图片文件：`{imagePath}`，然后完成以下任务：\n{prompt}";

        var errors = new List<string>();

        foreach (var provider in order)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // model / variant はユーザーが選んだ preferredProvider に対してのみ適用する。
            // フォールバックで別 CLI に切り替わった際に、噛み合わないモデル名を渡さないため。
            var isPreferred = normalizedPreferred != null
                && string.Equals(provider, normalizedPreferred, StringComparison.OrdinalIgnoreCase);
            var providerModel = isPreferred ? model : null;
            var providerVariant = isPreferred ? variant : null;

            try
            {
                var (output, error) = provider switch
                {
                    "opencode"    => await RunProcessAsync("opencode", BuildOpencodeArgs(fullPrompt, providerModel, providerVariant), PerProviderTimeout, cancellationToken),
                    "antigravity" => await RunProcessAsync("antigravity", BuildPrintModeArgs(fullPrompt, providerModel), PerProviderTimeout, cancellationToken),
                    "claude"      => await RunProcessAsync("claude", BuildPrintModeArgs(fullPrompt, providerModel), PerProviderTimeout, cancellationToken),
                    _ => (null, $"未知の CLI プロバイダー: {provider}")
                };

                var text = ExtractText(output);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogInformation("CLI chain succeeded via {Provider}", provider);
                    return new CliChainResult(true, text, provider, null);
                }

                var reason = error ?? "空の応答";
                errors.Add($"{provider}: {reason}");
                _logger.LogWarning("CLI chain provider {Provider} failed, falling back. Reason: {Reason}", provider, reason);
            }
            catch (Exception ex)
            {
                errors.Add($"{provider}: {ex.Message}");
                _logger.LogWarning(ex, "CLI chain provider {Provider} threw an exception, falling back", provider);
            }
        }

        var combinedError = string.Join(" | ", errors);
        _logger.LogError("CLI chain exhausted all providers ({Order}). Errors: {Errors}", string.Join(">", order), combinedError);
        return new CliChainResult(false, null, null, combinedError);
    }

    // ──────────────────────────────────────────────────────────
    // Provider argument builders
    // ──────────────────────────────────────────────────────────

    private static string BuildOpencodeArgs(string prompt, string? model = null, string? variant = null)
    {
        var modelArg = string.IsNullOrWhiteSpace(model) ? "" : $" --model \"{Escape(model)}\"";
        var variantArg = string.IsNullOrWhiteSpace(variant) ? "" : $" --variant \"{Escape(variant.ToLowerInvariant())}\"";
        return $"run \"{Escape(prompt)}\"{modelArg}{variantArg} --dangerously-skip-permissions";
    }

    private static string BuildPrintModeArgs(string prompt, string? model = null)
    {
        var modelArg = string.IsNullOrWhiteSpace(model) ? "" : $" --model \"{Escape(model)}\"";
        return $"-p \"{Escape(prompt)}\"{modelArg} --dangerously-skip-permissions";
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ──────────────────────────────────────────────────────────
    // Process execution
    // ──────────────────────────────────────────────────────────

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
            // 実行ファイルが見つからない等（未インストールの CLI をスキップできるように例外化しない）
            return (null, $"起動失敗: {ex.Message}");
        }

        // 対話型認証プロンプトへの意図しないハングを避けるため、標準入力は即クローズする
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

    // ──────────────────────────────────────────────────────────
    // Output parsing
    // ──────────────────────────────────────────────────────────

    private static string? ExtractText(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput)) return null;

        var cleaned = Regex.Replace(rawOutput, @"\x1B\[[0-9;]*[a-zA-Z]", "");

        // antigravity 等が {"response": "..."} 形式の JSON を返すケースに対応
        try
        {
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                using var doc = JsonDocument.Parse(cleaned[start..(end + 1)]);
                if (doc.RootElement.TryGetProperty("response", out var responseProp))
                {
                    var text = responseProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
        }
        catch (JsonException)
        {
            // JSON でなければそのままテキストとして扱う
        }

        return cleaned.Trim();
    }

    // ──────────────────────────────────────────────────────────
    // Config helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// ユーザー選択のプロバイダー名を正規化する。既知の CLI（opencode/antigravity/claude）
    /// のみ受け付け、それ以外・空は null（＝選択なし）として扱う。
    /// </summary>
    private static string? NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var normalized = provider.Trim().ToLowerInvariant();
        return DefaultOrder.Contains(normalized) ? normalized : null;
    }

    private static string[] ResolveOrder(Dictionary<string, string> env)
    {
        if (env.TryGetValue("CLI_CHAIN_ORDER", out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            var custom = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .Where(s => DefaultOrder.Contains(s))
                .ToArray();
            if (custom.Length > 0) return custom;
        }
        return DefaultOrder;
    }

    private static Dictionary<string, string> LoadProjectEnv(string? projectName)
    {
        var env = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(projectName)) return env;

        var envPath = Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName, ".env");
        if (!File.Exists(envPath)) return env;

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..].Trim();

            var commentIndex = value.IndexOf('#');
            if (commentIndex >= 0) value = value[..commentIndex].Trim();

            if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
            {
                value = value[1..^1];
            }
            env[key] = value;
        }
        return env;
    }
}
