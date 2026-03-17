// ファイル概要: QueryString のフィルタ入力を型別に正規化して辞書化します。

using System.Globalization;
using NetYamlForge.Models;
using Microsoft.AspNetCore.Http;

namespace NetYamlForge.Services;

public static class FilterValueParser
{
    public static Dictionary<string, string?> Build(EntityDefinition meta, IQueryCollection query)
    {
        var filters = new Dictionary<string, string?>();

        foreach (var f in meta.Filters)
        {
            var key = f.Key;
            var type = (f.Value.Type ?? "dropdown").ToLowerInvariant();

            switch (type)
            {
                case "range":
                    filters[$"{key}_min"] = query[$"{key}_min"].FirstOrDefault();
                    filters[$"{key}_max"] = query[$"{key}_max"].FirstOrDefault();
                    break;

                case "date-range":
                    filters[$"{key}_from"] = query[$"{key}_from"].FirstOrDefault();
                    filters[$"{key}_to"] = query[$"{key}_to"].FirstOrDefault();
                    break;

                case "checkbox":
                case "multi-select":
                    var allValues = query[key].ToArray()
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct()
                        .ToArray();
                    filters[key] = allValues.Length == 0 ? null : string.Join(",", allValues);
                    break;

                default:
                    filters[key] = query[key].FirstOrDefault();
                    break;
            }
        }

        return filters;
    }

    public static Dictionary<string, string?> BuildCleared(EntityDefinition meta)
    {
        var filters = new Dictionary<string, string?>();

        foreach (var f in meta.Filters)
        {
            var key = f.Key;
            var type = (f.Value.Type ?? "dropdown").ToLowerInvariant();

            switch (type)
            {
                case "range":
                    filters[$"{key}_min"] = null;
                    filters[$"{key}_max"] = null;
                    break;

                case "date-range":
                    filters[$"{key}_from"] = null;
                    filters[$"{key}_to"] = null;
                    break;

                default:
                    filters[key] = null;
                    break;
            }
        }

        return filters;
    }

    /// <summary>
    /// 日付文字列を解析し、SQLパラメータ用の正規化済み文字列を返す。
    /// 下限: "yyyy-MM-dd"（00:00:00 を含む）、上限: "yyyy-MM-dd 23:59:59"。
    /// 解析できない値は false を返す。
    /// </summary>
    public static bool TryParseDateBound(string raw, bool upperBound, out string normalized)
    {
        if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            normalized = upperBound
                ? dt.ToString("yyyy-MM-dd") + " 23:59:59"
                : dt.ToString("yyyy-MM-dd");
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}
