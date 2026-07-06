using System;
using System.Collections.Generic;
using System.Text;
using PdfSharp.Drawing;

namespace NetYamlForge.Services;

public partial class DocumentPdfService
{
    /// <summary>折り返しありのテキストを描画し、使用した高さを返します</summary>
    private static double DrawWrappedText(
        XGraphics gfx, string text, XFont font, XBrush brush,
        double x, double y, double width, string? align)
    {
        if (string.IsNullOrEmpty(text)) return font.GetHeight() * 1.2;

        var lines  = WrapText(text, font, Math.Max(width, 1), gfx);
        double lh  = font.GetHeight() * 1.2;

        var xAlign = align?.ToLowerInvariant() switch
        {
            "center" => XStringAlignment.Center,
            "right"  => XStringAlignment.Far,
            _        => XStringAlignment.Near,
        };
        var fmt = new XStringFormat
        {
            Alignment     = xAlign,
            LineAlignment = XLineAlignment.Near,
        };

        double cy = y;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, brush, new XRect(x, cy, width, lh + 2), fmt);
            cy += lh;
        }
        return lh * lines.Count;
    }

    private static double MeasureWrappedH(XGraphics gfx, string text, XFont font, double width)
    {
        if (string.IsNullOrEmpty(text)) return font.GetHeight() * 1.2;
        return font.GetHeight() * 1.2 * WrapText(text, font, Math.Max(width, 1), gfx).Count;
    }

    /// <summary>改行・折り返し対応のテキスト分割（CJK / ラテン混在対応）</summary>
    private static List<string> WrapText(string text, XFont font, double maxWidth, XGraphics gfx)
    {
        var result = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            if (string.IsNullOrEmpty(para)) { result.Add(""); continue; }
            if (gfx.MeasureString(para, font).Width <= maxWidth) { result.Add(para); continue; }

            // スペースがある場合は単語単位で折り返し（ラテン文字向け）
            var words = para.Split(' ');
            if (words.Length > 1)
            {
                var cur = new StringBuilder();
                foreach (var w in words)
                {
                    var test = cur.Length > 0 ? cur + " " + w : w;
                    if (gfx.MeasureString(test, font).Width > maxWidth && cur.Length > 0)
                    {
                        result.Add(cur.ToString()); cur.Clear(); cur.Append(w);
                    }
                    else { if (cur.Length > 0) cur.Append(' '); cur.Append(w); }
                }
                if (cur.Length > 0) result.Add(cur.ToString());
            }
            else
            {
                // スペースなし→文字単位折り返し（CJK 向け）
                var cur = new StringBuilder();
                foreach (var ch in para)
                {
                    var test = cur.ToString() + ch;
                    if (gfx.MeasureString(test, font).Width > maxWidth && cur.Length > 0)
                    {
                        result.Add(cur.ToString()); cur.Clear(); cur.Append(ch);
                    }
                    else cur.Append(ch);
                }
                if (cur.Length > 0) result.Add(cur.ToString());
            }
        }
        return result.Count > 0 ? result : [""];
    }
}
