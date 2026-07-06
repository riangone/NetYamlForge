using System;
using System.Collections.Generic;
using System.Linq;
using NetYamlForge.Models;
using PdfSharp.Drawing;

namespace NetYamlForge.Services;

public partial class DocumentPdfService
{
    private static double ApproxHeight(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t, double width,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        return s.Type switch
        {
            "line"       => s.LineWeight,
            "paragraph"  => MeasureParagraphH(gfx, s, t, width, h, ds),
            "row"        => MeasureRowH(gfx, s, t, width, h, ds),
            "labelTable" => MeasureLabelTableH(gfx, s, t, width, h, ds),
            "dataTable"  => (s.FontSize * 1.4 + 2 * s.CellPadding) * (1 + Math.Max(s.MinRows, 3)),
            _            => 0,
        };
    }

    private static double MeasureCellH(
        XGraphics gfx, PdfCellConfig cell, ThemeVars t, double cellWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        float   basePad = cell.Padding    ?? 0;
        double  pl      = cell.PaddingLeft   ?? basePad;
        double  pr      = cell.PaddingRight  ?? basePad;
        double  pt      = cell.PaddingTop    ?? basePad;
        double  pb      = cell.PaddingBottom ?? basePad;
        double  innerW  = Math.Max(cellWidth - pl - pr, 1);

        double contentH = cell.Elements.Sum(e =>
        {
            double emt = e.MarginTop ?? 0, emb = e.MarginBottom ?? 0;
            double inner = e.Type switch
            {
                "line"       => e.LineWeight,
                "paragraph"  => MeasureParagraphH(gfx, e, t, innerW, h, ds),
                "row"        => MeasureRowH(gfx, e, t, innerW, h, ds),
                "labelTable" => MeasureLabelTableH(gfx, e, t, innerW, h, ds),
                "dataTable"  => (e.FontSize * 1.4 + 2 * e.CellPadding) * (1 + Math.Max(e.MinRows, 3)),
                _            => 0,
            };
            return emt + inner + emb;
        });

        double total = contentH + pt + pb;
        if (cell.MinHeight.HasValue) total = Math.Max(total, cell.MinHeight.Value);
        return Math.Max(total, 10);
    }

    private static double MeasureParagraphH(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t, double width,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var text = (s.Prefix ?? "") + BuildParaText(s, h, ds);
        var font = new XFont(FontFamilyName, s.FontSize,
            s.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        return MeasureWrappedH(gfx, text, font, width);
    }

    private static double MeasureRowH(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t, double totalWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (s.Cells.Count == 0) return 0;
        var colW = CalcColWidths(s, totalWidth);
        double max = 0;
        for (int i = 0; i < s.Cells.Count; i++)
        {
            double cw = i < colW.Length ? colW[i] : totalWidth;
            max = Math.Max(max, MeasureCellH(gfx, s.Cells[i], t, cw, h, ds));
        }
        return max;
    }

    private static double MeasureLabelTableH(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t, double totalWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        float[] cw = s.ColumnWidths ?? [35f, 65f];
        double  lw = cw[0] / 100.0 * totalWidth;
        double  vw = cw.Length > 1 ? cw[1] / 100.0 * totalWidth : totalWidth - lw;
        float pad = s.CellPadding, fs = s.FontSize;
        var font = new XFont(FontFamilyName, fs, XFontStyleEx.Regular);

        return s.Rows.Where(r =>
        {
            var v = ResolveField(r.Field, h, ds);
            if (r.OmitIfEmpty && string.IsNullOrWhiteSpace(v)) return false;
            if (r.OmitIfZero  && decimal.TryParse(v, out var dz) && dz == 0) return false;
            return true;
        }).Sum(r =>
        {
            var v = ResolveField(r.Field, h, ds);
            return Math.Max(
                MeasureWrappedH(gfx, r.Label, font, lw - 2 * pad) + 2 * pad,
                Math.Max(MeasureWrappedH(gfx, v, font, vw - 2 * pad) + 2 * pad,
                         fs * 1.5 + 2 * pad));
        });
    }
}
