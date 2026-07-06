using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetYamlForge.Services.BatchJob.Sdk;

/// <summary>
/// AI のレスポンステキストから JSON を抽出・解析するヘルパー。
/// </summary>
public static class AiResultParser
{
    /// <summary>
    /// AI レスポンスから markdown コードフェンス（<c>```json</c> 等）を除去し、
    /// 最初の <c>{</c>〜最後の <c>}</c>（無ければ <c>[</c>〜<c>]</c>）を JSON テキストとして切り出します。
    /// 境界が見つからない場合はフェンス除去済みテキストをそのまま返します。
    /// 文字列としての JSON が必要な呼び出し側（ファイル保存など）向けのヘルパーです。
    /// </summary>
    public static string ExtractJsonText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var cleaned = Regex.Replace(raw, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Trim();

        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start >= 0 && end > start) return cleaned[start..(end + 1)];

        var aStart = cleaned.IndexOf('[');
        var aEnd = cleaned.LastIndexOf(']');
        if (aStart >= 0 && aEnd > aStart) return cleaned[aStart..(aEnd + 1)];

        return cleaned;
    }

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
