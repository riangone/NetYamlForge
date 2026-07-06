using System;
using System.Collections.Generic;

namespace NetYamlForge.Models;

public static class ColumnValueFormatter
{
    public static string FormatValue(string type, object? value, Dictionary<string, string>? optionLabels = null)
    {
        if (type.Equals("bool", StringComparison.OrdinalIgnoreCase))
        {
            var boolStr = value?.ToString() ?? "";
            var isTrue = value is bool b ? b
                : boolStr.Equals("true", StringComparison.OrdinalIgnoreCase) || boolStr == "1";
            return isTrue ? "✓ Yes" : "✗ No";
        }
        if (type.Equals("datetime", StringComparison.OrdinalIgnoreCase) && value is DateTime dt)
            return dt.ToString("yyyy-MM-dd");
        if (optionLabels != null)
        {
            var key = value?.ToString() ?? "";
            return optionLabels.TryGetValue(key, out var label) ? label : key;
        }
        return value?.ToString() ?? string.Empty;
    }
}
