using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetYamlForge.Services.BatchJob.Sdk;

/// <summary>
/// AI のレスポンステキストから JSON を抽出・解析するヘルパー。
/// </summary>
public static class AiResultParser
{
    public static bool TryParseJson<T>(string raw, out T? result, out string? error)
    {
        result = default;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Raw response text is empty";
            return false;
        }

        try
        {
            var cleaned = Regex.Replace(raw, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Trim();
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                start = cleaned.IndexOf('[');
                end = cleaned.LastIndexOf(']');
                if (start < 0 || end <= start)
                {
                    error = "Could not locate JSON object boundaries {} or []";
                    return false;
                }
            }

            var json = cleaned[start..(end + 1)];
            result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return true;
        }
        catch (Exception ex)
        {
            error = $"JSON deserialization failed: {ex.Message}";
            return false;
        }
    }
}
