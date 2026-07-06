using System;
using System.Collections.Generic;
using System.Linq;
using NetYamlForge.Models;
using PdfSharp.Drawing;

namespace NetYamlForge.Services;

public partial class DocumentPdfService
{
    private static void RenderTopLevel(
        RenderState st, PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        double mt = s.MarginTop    ?? 0;
        double mb = s.MarginBottom ?? 0;

        // ページ足りなければ改ページ（簡易推測）
        double approx = ApproxHeight(st.Gfx, s, st.Theme, st.ContentWidth, h, ds);
        st.EnsureSpace(mt + approx + mb);

        st.Y += mt;

        switch (s.Type)
        {
            case "line":
                RenderLine(st, s);
                break;
            case "paragraph":
                st.Y += RenderParagraph(st.Gfx, s, st.Theme,
                    st.ContentLeft, st.Y, st.ContentWidth, h, ds);
                break;
            case "row":
                st.Y += RenderRow(st.Gfx, s, st.Theme,
                    st.ContentLeft, st.Y, st.ContentWidth, h, ds);
                break;
            case "labelTable":
                st.Y += RenderLabelTable(st.Gfx, s, st.Theme,
                    st.ContentLeft, st.Y, st.ContentWidth, h, ds);
                break;
            case "dataTable":
                st.Y += RenderDataTable(st.Gfx, s, st.Theme,
                    st.ContentLeft, st.Y, st.ContentWidth, ds);
                break;
        }

        st.Y += mb;
    }

    private static void RenderLine(RenderState st, PdfSectionConfig s)
    {
        var color = ResolveXColor(s.Color, st.Theme) ?? st.Theme.Border;
        var pen   = new XPen(color, s.LineWeight);
        st.Gfx.DrawLine(pen, st.ContentLeft, st.Y, st.ContentRight, st.Y);
        st.Y += s.LineWeight;
    }

    private static double RenderParagraph(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double width,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var value = BuildParaText(s, h, ds);
        var main  = (s.Prefix ?? "") + value;
        var color = ResolveXColor(s.Color, t) ?? XColors.Black;
        var style = s.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;
        var font  = new XFont(FontFamilyName, s.FontSize, style);
        var brush = new XSolidBrush(color);

        double totalH = DrawWrappedText(gfx, main, font, brush, x, y, width, s.Align);

        if (s.Suffix != null)
        {
            var sfSize  = s.SuffixFontSize ?? s.FontSize;
            var sfFont  = new XFont(FontFamilyName, sfSize, XFontStyleEx.Regular);
            var mainW   = gfx.MeasureString(main, font).Width;
            var lineH   = font.GetHeight() * 1.2;
            double sfX  = s.Align?.ToLowerInvariant() == "center"
                ? x + width / 2 + mainW / 2
                : x + Math.Min(mainW, width);
            gfx.DrawString(s.Suffix, sfFont, brush, sfX, y + lineH * 0.78);
        }

        return totalH;
    }

    private static string BuildParaText(
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (s.Template != null) return InterpolateTemplate(s.Template, h, ds);
        if (s.Field    != null) return FormatValue(ResolveField(s.Field, h, ds), s.Format);
        return s.Text ?? "";
    }

    private static double RenderRow(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double totalWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (s.Cells.Count == 0) return 0;
        var colW = CalcColWidths(s, totalWidth);

        // 全セルの高さを測定
        double[] heights = s.Cells.Select((c, i) =>
        {
            double cw = i < colW.Length ? colW[i] : totalWidth;
            return MeasureCellH(gfx, c, t, cw, h, ds);
        }).ToArray();
        double rowH = heights.Max();

        // 描画
        double cx = x;
        for (int i = 0; i < s.Cells.Count; i++)
        {
            double cw = i < colW.Length ? colW[i] : totalWidth;
            DrawCell(gfx, s.Cells[i], t, cx, y, cw, rowH, h, ds);
            cx += cw;
        }
        return rowH;
    }

    private static double RenderLabelTable(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double totalWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        float[] cw = s.ColumnWidths ?? [35f, 65f];
        double  lw = cw[0] / 100.0 * totalWidth;
        double  vw = cw.Length > 1 ? cw[1] / 100.0 * totalWidth : totalWidth - lw;

        var border       = new XPen(t.Border, s.BorderWeight);
        var labelBgColor = ResolveXColor(s.LabelBackground, t) ?? t.Label;
        var labelFgColor = s.LabelTextColor != null
            ? (ResolveXColor(s.LabelTextColor, t) ?? XColors.Black)
            : XColors.Black;
        float pad = s.CellPadding;
        float fs  = s.FontSize;

        double totalH = 0, cy = y;

        foreach (var row in s.Rows)
        {
            var rawVal = ResolveField(row.Field, h, ds);
            if (row.OmitIfEmpty && string.IsNullOrWhiteSpace(rawVal)) continue;
            if (row.OmitIfZero  && decimal.TryParse(rawVal, out var dz) && dz == 0m) continue;

            var displayVal = FormatValue(rawVal, row.Format);
            var style = row.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;
            var font  = new XFont(FontFamilyName, fs, style);
            double rowH = Math.Max(
                MeasureWrappedH(gfx, row.Label, font, lw - 2 * pad) + 2 * pad,
                Math.Max(MeasureWrappedH(gfx, displayVal, font, vw - 2 * pad) + 2 * pad,
                         fs * 1.5 + 2 * pad));

            // ラベルセル
            gfx.DrawRectangle(new XSolidBrush(labelBgColor), x,      cy, lw, rowH);
            gfx.DrawRectangle(border,                         x,      cy, lw, rowH);
            DrawWrappedText(gfx, row.Label, font, new XSolidBrush(labelFgColor),
                x + pad + 2, cy + pad, lw - 2 * pad - 2, "left");

            // 値セル
            var valueAlign = row.Format is "currency" or "quantity" ? "right" : "left";
            gfx.DrawRectangle(border, x + lw, cy, vw, rowH);
            DrawWrappedText(gfx, displayVal, font, new XSolidBrush(XColors.Black),
                x + lw + pad + 2, cy + pad, vw - 2 * pad - 2, valueAlign);

            cy      += rowH;
            totalH  += rowH;
        }
        return totalH;
    }

    private static double RenderDataTable(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double totalWidth,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var items = new List<IDictionary<string, object?>>();
        if (s.DataSource != null && ds.TryGetValue(s.DataSource, out var src))
            items.AddRange(src);

        double[] colW   = s.Columns.Select(c => c.Width / 100.0 * totalWidth).ToArray();
        var border      = new XPen(t.Border, s.BorderWeight);
        var labelBg     = ResolveXColor(s.LabelBackground, t) ?? t.Label;
        var labelFg     = s.LabelTextColor != null
            ? (ResolveXColor(s.LabelTextColor, t) ?? XColors.White)
            : XColors.White;
        var oddBg       = ResolveXColor(s.OddRowBackground, t) ?? t.OddRow;
        float pad = s.CellPadding, fs = s.FontSize;
        var font  = new XFont(FontFamilyName, fs, XFontStyleEx.Regular);
        var bfont = new XFont(FontFamilyName, fs, XFontStyleEx.Bold);
        double rowH = fs * 1.4 + 2 * pad;
        double totalH = 0, cy = y;

        // ── ヘッダー行 ──
        double cx = x;
        foreach ((var col, var cw) in s.Columns.Zip(colW))
        {
            gfx.DrawRectangle(new XSolidBrush(labelBg), cx, cy, cw, rowH);
            gfx.DrawRectangle(border,                   cx, cy, cw, rowH);
            DrawWrappedText(gfx, col.Label, bfont, new XSolidBrush(labelFg),
                cx + pad, cy + pad, cw - 2 * pad, col.Align);
            cx += cw;
        }
        cy += rowH; totalH += rowH;

        // ── データ行（minRows まで空行を補完）──
        var display = items.ToList();
        while (display.Count < s.MinRows)
            display.Add(new Dictionary<string, object?>());

        int rowNum = 1;
        foreach (var item in display)
        {
            bool isOdd  = rowNum % 2 == 1;
            bool hasData = item.Count > 0;
            cx = x;
            foreach ((var col, var cw) in s.Columns.Zip(colW))
            {
                var rect = new XRect(cx, cy, cw, rowH);
                if (isOdd && hasData)
                    gfx.DrawRectangle(new XSolidBrush(oddBg), rect);
                gfx.DrawRectangle(border, rect);

                string text;
                if (col.Field == "_rowNumber")
                    text = hasData ? rowNum.ToString() : "";
                else
                {
                    item.TryGetValue(col.Field, out var raw);
                    text = FormatValue(raw?.ToString() ?? "", col.Format);
                }
                DrawWrappedText(gfx, text, font, new XSolidBrush(XColors.Black),
                    cx + pad, cy + pad, cw - 2 * pad, col.Align);
                cx += cw;
            }
            cy += rowH; totalH += rowH;
            if (hasData) rowNum++;
        }
        return totalH;
    }

    private static void DrawCell(
        XGraphics gfx, PdfCellConfig cell, ThemeVars t,
        double x, double y, double width, double height,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        // 背景
        if (cell.Background != null)
        {
            var bg = ResolveXColor(cell.Background, t);
            if (bg.HasValue)
                gfx.DrawRectangle(new XSolidBrush(bg.Value), x, y, width, height);
        }
        // ボーダー
        if (cell.BorderWeight is > 0)
            gfx.DrawRectangle(new XPen(t.Border, cell.BorderWeight.Value), x, y, width, height);

        float   basePad = cell.Padding    ?? 0;
        double  pl      = cell.PaddingLeft   ?? basePad;
        double  pr      = cell.PaddingRight  ?? basePad;
        double  pt      = cell.PaddingTop    ?? basePad;
        double  pb      = cell.PaddingBottom ?? basePad;
        double  innerW  = Math.Max(width - pl - pr, 1);

        double cy = y + pt;
        foreach (var elem in cell.Elements)
            cy += DrawElementAt(gfx, elem, t, x + pl, cy, innerW, h, ds);
    }

    private static double DrawElementAt(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double width,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        double mt = s.MarginTop    ?? 0;
        double mb = s.MarginBottom ?? 0;

        double inner = s.Type switch
        {
            "paragraph"  => RenderParagraph(gfx, s, t, x, y + mt, width, h, ds),
            "row"        => RenderRow(gfx, s, t, x, y + mt, width, h, ds),
            "labelTable" => RenderLabelTable(gfx, s, t, x, y + mt, width, h, ds),
            "dataTable"  => RenderDataTable(gfx, s, t, x, y + mt, width, ds),
            _            => 0,
        };
        return mt + inner + mb;
    }

    private static double DrawInlineLine(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double width)
    {
        var color = ResolveXColor(s.Color, t) ?? t.Border;
        gfx.DrawLine(new XPen(color, s.LineWeight), x, y, x + width, y);
        return s.LineWeight;
    }
}
