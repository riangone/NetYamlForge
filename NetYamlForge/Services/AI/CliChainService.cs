using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NetYamlForge.Services.BatchJob.Sdk;

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
    /// <param name="model">
    /// preferredProvider に渡すモデル名（--model）。明示指定が無ければ、そのプロバイダーの
    /// システム設定画面での DefaultModel が使われる。フォールバック先の他プロバイダーには適用しない
    /// （ただし他プロバイダーも自分自身の DefaultModel は使う）。
    /// </param>
    /// <param name="variant">
    /// AI レベル相当のパラメータ。CLI ごとにフラグ名が異なる（opencode: --variant、claude: --effort、
    /// antigravity: 非対応）ため、実際に渡すフラグ名は CliProviderOptions.VariantFlag で解決する。
    /// 明示指定が無ければ、そのプロバイダーの DefaultVariant が使われる。
    /// </param>
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
    private readonly ILogger<CliChainService> _logger;
    private readonly IOptionsMonitor<CliChainOptions>? _optionsMonitor;
    private readonly CliChainOptions? _staticOptions;

    /// <summary>
    /// 現在有効な設定値。IOptionsMonitor 経由の場合は appsettings.json の変更が
    /// アプリ再起動なしに即座に反映される（システム設定画面からの保存を想定）。
    /// </summary>
    private CliChainOptions _options => _optionsMonitor?.CurrentValue ?? _staticOptions!;

    /// <summary>DI 経由のコンストラクタ。appsettings.json の "AiCliChain" セクションを反映する。</summary>
    public CliChainService(ILogger<CliChainService> logger, IOptionsMonitor<CliChainOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    /// <summary>設定ファイルにアクセスできない呼び出し元向けの簡易コンストラクタ（既定値を使用）。</summary>
    public CliChainService(ILogger<CliChainService> logger)
    {
        _logger = logger;
        _staticOptions = new CliChainOptions();
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
        var env = ProjectEnvLoader.LoadForProject(projectName);

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
            // 明示選択が無い場合のみ、設定で PreferredForImages=true とマークされた CLI を先頭に寄せる
            // （従来は "antigravity" 固定のハードコードだったが、設定ファイル側で切り替え可能にする）。
            var imagePreferred = order.FirstOrDefault(p =>
                _options.Providers.TryGetValue(p, out var c) && c.PreferredForImages);
            if (imagePreferred != null)
            {
                var list = order.ToList();
                list.Remove(imagePreferred);
                list.Insert(0, imagePreferred);
                order = list.ToArray();
            }
        }

        var fullPrompt = string.IsNullOrWhiteSpace(imagePath)
            ? prompt
            : $"请使用你自己的文件查看工具打开图片文件：`{imagePath}`，然后完成以下任务：\n{prompt}";

        var errors = new List<string>();

        foreach (var provider in order)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!_options.Providers.TryGetValue(provider, out var providerConfig)
                    || string.IsNullOrWhiteSpace(providerConfig.Command))
                {
                    errors.Add($"{provider}: 設定ファイル (AiCliChain:Providers) に起動情報が見つかりません");
                    _logger.LogWarning("CLI chain provider {Provider} has no configuration, skipping", provider);
                    continue;
                }

                // model / variant の明示指定（呼び出し元が渡した引数）はユーザーが選んだ preferredProvider に
                // 対してのみ適用する。フォールバックで別 CLI に切り替わった際に、噛み合わないモデル名を
                // 渡さないため。ただし「システム設定画面」で CLI ごとに登録した既定値（DefaultModel/DefaultVariant）は、
                // 明示指定が無い場合のフォールバックとしてどのプロバイダーでも使ってよい（CLI 固有の値なので安全）。
                var isPreferred = normalizedPreferred != null
                    && string.Equals(provider, normalizedPreferred, StringComparison.OrdinalIgnoreCase);
                var explicitModel = isPreferred ? model : null;
                var explicitVariant = isPreferred ? variant : null;
                var providerModel = string.IsNullOrWhiteSpace(explicitModel) ? providerConfig.DefaultModel : explicitModel;
                var providerVariant = string.IsNullOrWhiteSpace(explicitVariant) ? providerConfig.DefaultVariant : explicitVariant;

                var args = BuildArgs(providerConfig, fullPrompt, providerModel, providerVariant);
                var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 90);
                var (output, error) = await RunProcessAsync(providerConfig.Command, args, timeout, cancellationToken);

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

    /// <summary>
    /// 設定ファイル (CliProviderOptions.ArgsTemplate) に基づき、CLI に渡す引数文字列を組み立てる。
    /// CLI 種別ごとの引数フォーマットの違い（opencode の "run" サブコマンド等）はコード変更ではなく
    /// appsettings.json 側のテンプレート編集で吸収できるようにする。
    /// </summary>
    private static string BuildArgs(CliProviderOptions config, string prompt, string? model, string? variant)
    {
        var modelArg = string.IsNullOrWhiteSpace(model) ? "" : $" --model \"{Escape(model)}\"";
        // レベルを渡すフラグ名は CLI ごとに異なる（opencode: --variant、claude: --effort）。
        // 固定文字列にせず VariantFlag から取得する（未設定なら --variant を既定にフォールバック）。
        var variantFlag = string.IsNullOrWhiteSpace(config.VariantFlag) ? "--variant" : config.VariantFlag;
        var variantArg = (config.SupportsVariant && !string.IsNullOrWhiteSpace(variant))
            ? $" {variantFlag} \"{Escape(variant.ToLowerInvariant())}\""
            : "";

        return config.ArgsTemplate
            .Replace("{prompt}", Escape(prompt))
            .Replace("{model}", modelArg)
            .Replace("{variant}", variantArg);
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
    /// ユーザー選択のプロバイダー名を正規化する。appsettings.json の AiCliChain:Providers に
    /// 定義済みの CLI のみ受け付け、それ以外・空は null（＝選択なし）として扱う。
    /// </summary>
    private string? NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var normalized = provider.Trim().ToLowerInvariant();
        return _options.Providers.ContainsKey(normalized) ? normalized : null;
    }

    /// <summary>
    /// 試行順序を解決する。優先順位：プロジェクト .env の CLI_CHAIN_ORDER
    /// &gt; appsettings.json の AiCliChain:DefaultOrder &gt; 設定済みプロバイダー全件。
    /// いずれの場合も、実際に AiCliChain:Providers に定義されている CLI のみを対象にする
    /// （設定ファイルに存在しない名前をハードコードで許可しない）。
    /// </summary>
    private string[] ResolveOrder(Dictionary<string, string> env)
    {
        if (env.TryGetValue("CLI_CHAIN_ORDER", out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            var custom = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .Where(s => _options.Providers.ContainsKey(s))
                .ToArray();
            if (custom.Length > 0) return custom;
        }

        var configuredDefault = _options.DefaultOrder
            .Where(s => _options.Providers.ContainsKey(s))
            .ToArray();

        return configuredDefault.Length > 0 ? configuredDefault : _options.Providers.Keys.ToArray();
    }

}
