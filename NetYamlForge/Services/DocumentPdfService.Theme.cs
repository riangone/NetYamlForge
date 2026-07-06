using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NetYamlForge.Models;
using PdfSharp.Drawing;

namespace NetYamlForge.Services;

public partial class DocumentPdfService
{
    private record ThemeVars(
        XColor Primary, XColor Label, XColor LabelText,
        XColor Subtle,  XColor OddRow, XColor Border);

    private static ThemeVars BuildTheme(PdfThemeConfig cfg) => new(
        ParseXColor(cfg.PrimaryColor),    ParseXColor(cfg.LabelColor),
        ParseXColor(cfg.LabelTextColor),  ParseXColor(cfg.SubtleBackground),
        ParseXColor(cfg.OddRowColor),     ParseXColor(cfg.BorderColor));

    private static XColor? ResolveXColor(string? colorRef, ThemeVars t)
    {
        if (string.IsNullOrEmpty(colorRef)) return null;
        return colorRef.ToLowerInvariant() switch
        {
            "primary"   => t.Primary,
            "label"     => t.Label,
            "labeltext" => t.LabelText,
            "subtle"    => t.Subtle,
            "oddrow"    => t.OddRow,
            "border"    => t.Border,
            _           => TryParseXColor(colorRef),
        };
    }

    private static XColor ParseXColor(string hex)
    {
        hex = hex.TrimStart('#');
        return XColor.FromArgb(
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16));
    }

    private static XColor? TryParseXColor(string hex)
    {
        try { return ParseXColor(hex); }
        catch { return null; }
    }

    private static (double Width, double Height) ParsePageSize(string size, string orient)
    {
        var (w, h) = size.ToUpperInvariant() switch
        {
            "A3"     => (841.89, 1190.55),
            "LETTER" => (612.00,  792.00),
            "LEGAL"  => (612.00, 1008.00),
            _        => (595.28,  841.89),   // A4
        };
        return orient.ToLowerInvariant() == "landscape" ? (h, w) : (w, h);
    }

    private static double[] CalcColWidths(PdfSectionConfig s, double totalWidth)
    {
        if (s.ColumnWidths is { Length: > 0 })
            return s.ColumnWidths.Select(p => p / 100.0 * totalWidth).ToArray();
        int n = s.Cells.Count;
        return Enumerable.Repeat(totalWidth / n, n).ToArray();
    }

    private static string FormatValue(string? raw, string? format)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return format?.ToLowerInvariant() switch
        {
            "currency" => decimal.TryParse(raw, out var d) ? $"¥{d:N0}" : raw,
            "quantity" => decimal.TryParse(raw, out var q)
                ? (q == Math.Floor(q) ? ((int)q).ToString() : q.ToString("N1"))
                : raw,
            _ => raw,
        };
    }

    private static string ResolveField(
        string? fieldRef,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (string.IsNullOrEmpty(fieldRef)) return "";
        var dot = fieldRef.IndexOf('.');
        if (dot > 0)
        {
            var src  = fieldRef[..dot];
            var key  = fieldRef[(dot + 1)..];
            return ds.TryGetValue(src, out var rows) && rows.Count > 0
                ? GetStr(rows[0], key) : "";
        }
        return GetStr(h, fieldRef);
    }

    private static string InterpolateTemplate(
        string tpl,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var result = Regex.Replace(tpl, @"\{([^}]+)\}",
            m => ResolveField(m.Groups[1].Value, h, ds));
        return string.Join("\n",
            result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    private static string GetStr(IDictionary<string, object?>? d, string? key)
    {
        if (d == null || string.IsNullOrEmpty(key)) return "";
        return d.TryGetValue(key, out var v) && v != null ? v.ToString()! : "";
    }
}
