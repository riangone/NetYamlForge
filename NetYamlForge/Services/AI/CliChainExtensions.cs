using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NetYamlForge.Services.AI;

public static class CliChainExtensions
{
    public static async Task<T?> PromptJsonAsync<T>(
        this ICliChainService cli,
        string prompt,
        string? imagePath = null,
        string? projectName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await cli.PromptAsync(prompt, imagePath, projectName, cancellationToken: cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
            return default;

        try
        {
            var cleaned = Regex.Replace(result.Text, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Trim();
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            
            if (start >= 0 && end > start)
            {
                var json = cleaned.Substring(start, end - start + 1);
                return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (JsonException)
        {
            // JSON parsing failed
        }
        return default;
    }
}
