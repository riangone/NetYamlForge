using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetYamlForge.Services.AI;

public class GeminiCliService : IGeminiCliService
{
    private readonly ILogger<GeminiCliService> _logger;
    private readonly IConfiguration _configuration;

    public GeminiCliService(ILogger<GeminiCliService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<string> PromptAsync(string prompt, string? model = null, string? projectName = null, CancellationToken cancellationToken = default)
    {
        var env = LoadProjectEnv(projectName);
        var aiModel = model ?? env.GetValueOrDefault("AI_MODEL", "");
        
        var modelArg = (string.IsNullOrWhiteSpace(aiModel) || aiModel.Equals("auto", StringComparison.OrdinalIgnoreCase)) 
            ? "" 
            : $"--model \"{aiModel}\"";
        
        // Use -o json for structured output if we want reliability, but PromptAsync is for text.
        // Actually, gemini cli -o json returns { "response": "..." } which is safer.
        var startInfo = new ProcessStartInfo
        {
            FileName = "gemini",
            Arguments = $"-p \"{prompt.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" {modelArg} -o json",
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
            _logger.LogError("Gemini CLI timed out after 90 seconds.");
            return "";
        }

        if (process.ExitCode != 0)
        {
            _logger.LogError("Gemini CLI failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, errorBuilder.ToString());
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

    private Dictionary<string, string> LoadProjectEnv(string? projectName)
    {
        var env = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(projectName)) return env;

        var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName);
        var envPath = Path.Combine(projectDir, ".env");
        
        if (!File.Exists(envPath)) return env;

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
            
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = trimmed.Substring(0, eqIndex).Trim();
                var value = trimmed.Substring(eqIndex + 1).Trim();
                
                var commentIndex = value.IndexOf('#');
                if (commentIndex >= 0) value = value.Substring(0, commentIndex).Trim();

                if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                {
                    value = value.Substring(1, value.Length - 2);
                }
                env[key] = value;
            }
        }
        return env;
    }
}
