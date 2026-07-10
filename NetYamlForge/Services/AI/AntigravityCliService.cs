using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NetYamlForge.Services.BatchJob.Sdk;

namespace NetYamlForge.Services.AI;

public class AntigravityCliService : IAntigravityCliService
{
    private readonly ILogger<AntigravityCliService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<CliChainOptions> _cliOptionsMonitor;

    /// <summary>appsettings.json の変更（システム設定画面からの保存を含む）を即座に反映する。</summary>
    private CliChainOptions _cliOptions => _cliOptionsMonitor.CurrentValue;

    public AntigravityCliService(
        ILogger<AntigravityCliService> logger,
        IConfiguration configuration,
        IOptionsMonitor<CliChainOptions> cliOptions)
    {
        _logger = logger;
        _configuration = configuration;
        _cliOptionsMonitor = cliOptions;
    }

    public async Task<string> PromptAsync(string prompt, string? model = null, string? projectName = null, CancellationToken cancellationToken = default)
    {
        var env = ProjectEnvLoader.LoadForProject(projectName);
        _cliOptions.Providers.TryGetValue("antigravity", out var providerConfig);

        // モデル指定の優先順位：呼び出し元の明示指定 > プロジェクト .env の AI_MODEL >
        // システム設定画面（AiCliChain:Providers:antigravity:DefaultModel） > 未指定（CLI自身の既定）。
        var aiModel = model
            ?? env.GetValueOrDefault("AI_MODEL")
            ?? providerConfig?.DefaultModel
            ?? "";

        var modelArg = (string.IsNullOrWhiteSpace(aiModel) || aiModel.Equals("auto", StringComparison.OrdinalIgnoreCase))
            ? ""
            : $"--model \"{aiModel}\"";

        // 実行ファイル名は appsettings.json の AiCliChain:Providers:antigravity:Command から取得する。
        // 未設定の場合のみ従来の既定値 "antigravity" にフォールバックする。
        var command = !string.IsNullOrWhiteSpace(providerConfig?.Command)
            ? providerConfig!.Command
            : "antigravity";

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = $"-p \"{prompt.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" {modelArg} --dangerously-skip-permissions",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var kv in env)
        {
            startInfo.EnvironmentVariables[kv.Key] = kv.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 90 second timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(90));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            _logger.LogError("Antigravity CLI timed out after 90 seconds. Output: {Output}, Error: {Error}", outputBuilder.ToString(), errorBuilder.ToString());
            return "";
        }

        if (process.ExitCode != 0)
        {
            _logger.LogError("Antigravity CLI failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, errorBuilder.ToString());
            return "";
        }

        var fullOutput = outputBuilder.ToString();
        return ExtractResponseFromJson(fullOutput);
    }

    public async Task<T?> PromptJsonAsync<T>(string prompt, string? model = null, string? projectName = null, CancellationToken cancellationToken = default)
    {
        var responseText = await PromptAsync(prompt, model, projectName, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseText)) return default;

        try
        {
            // AI might return markdown code blocks
            var cleaned = Regex.Replace(responseText, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Trim();
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            
            if (start >= 0 && end > start)
            {
                var json = cleaned.Substring(start, end - start + 1);
                return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI JSON response: {Response}", responseText);
        }

        return default;
    }

    private string ExtractResponseFromJson(string fullOutput)
    {
        try
        {
            int firstBrace = fullOutput.IndexOf('{');
            int lastBrace = fullOutput.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                var jsonPart = fullOutput.Substring(firstBrace, lastBrace - firstBrace + 1);
                using var doc = JsonDocument.Parse(jsonPart);
                
                if (doc.RootElement.TryGetProperty("response", out var responseProp))
                {
                    return responseProp.GetString()?.Trim() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            // Fallback to raw output if JSON parse fails
        }
        return fullOutput.Trim();
    }

}
