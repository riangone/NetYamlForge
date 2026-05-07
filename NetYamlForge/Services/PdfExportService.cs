// ファイル概要：YAML 定義に基づいて PDF ファイルを生成するサービスです。
// PDFsharp (MIT ライセンス) を使用します。

using PdfSharp.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public interface IPdfExportService
{
    /// <summary>
    /// 行データと列定義から PDF バイト列を生成します。
    /// </summary>
    byte[] Generate(
        List<IDictionary<string, object>> rows,
        List<(string Key, string Label)> columns,
        EntityDefinition meta,
        PdfExportOptions options,
        string? projectDir = null);
}

public class PdfExportService : IPdfExportService
{
    // シンプルなデータ行の最低高さ（ポイント）
    private const float RowMinHeight = 20f;

    // よく使われる CJK フォントのシステムパス候補
    private static readonly string[] CjkFontPaths =
    {
        "/usr/share/fonts/opentype/ipafont-gothic/ipagp.ttf",
        "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/ipafont-mincho/ipamp.ttf",
        "/usr/share/fonts/truetype/vlgothic/VL-Gothic-Regular.ttf",
        "/Library/Fonts/Arial Unicode.ttf",                  // macOS
        "C:\\Windows\\Fonts\\msgothic.ttc",                   // Windows
    };

    public byte[] Generate(
        List<IDictionary<string, object>> rows,
        List<(string Key, string Label)> columns,
        EntityDefinition meta,
        PdfExportOptions options,
        string? projectDir = null)
    {
        using var ms = new MemoryStream();
        var document = new PdfDocument();
        document.Info.Title = options.Title ?? "Export";

        var pageSize = ResolvePageSize(options);
        var page = document.AddPage();
        page.Width = pageSize.Width;
        page.Height = pageSize.Height;

        var gfx = XGraphics.FromPdfPage(page);
        var font = LoadFont(options.FontFile, projectDir);
        var fontNormal = new XFont(font.FontFamily.Name, XUnit.FromPoint(9), XFontStyleEx.Regular);
        var fontBold = new XFont(font.FontFamily.Name, XUnit.FromPoint(9), XFontStyleEx.Bold);

        // ヘッダー・フッター用の余白を確保
        float topMargin = string.IsNullOrWhiteSpace(options.Title) ? XUnit.FromPoint(36f).Point : XUnit.FromPoint(50f).Point;
        float bottomMargin = options.ShowPageNumbers ? XUnit.FromPoint(40f).Point : XUnit.FromPoint(24f).Point;
        float leftMargin = XUnit.FromPoint(36f).Point;
        float rightMargin = XUnit.FromPoint(36f).Point;

        var usableWidth = pageSize.Width - leftMargin - rightMargin;
        var usableHeight = pageSize.Height - topMargin - bottomMargin;

        // ── タイトルブロック ───────────────────────────────────
        float y = topMargin;
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            var titleFont = new XFont(font.FontFamily.Name, XUnit.FromPoint(15), XFontStyleEx.Bold);
            gfx.DrawString(options.Title, titleFont, XBrushes.Black,
                new XRect(XUnit.FromPoint(leftMargin), XUnit.FromPoint(y), XUnit.FromPoint(usableWidth), XUnit.FromPoint(20)), XStringFormats.TopLeft);
            y += 20;
        }

        if (options.ShowGeneratedAt)
        {
            var dateFont = new XFont(font.FontFamily.Name, XUnit.FromPoint(8), XFontStyleEx.Regular);
            var dateBrush = new XSolidBrush(XColors.Gray);
            gfx.DrawString($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", dateFont, dateBrush,
                new XRect(XUnit.FromPoint(leftMargin), XUnit.FromPoint(y), XUnit.FromPoint(usableWidth), XUnit.FromPoint(15)), XStringFormats.TopLeft);
            y += 15;
        }

        y += XUnit.FromPoint(10).Point; // スペース追加

        // ── テーブル列設定 ────────────────────────────────────
        var colConfigs = BuildColumnConfigs(columns, options);
        var columnWidths = colConfigs.Select(c => c.Width / 100f * usableWidth).ToArray();

        // ── ヘッダー行 ────────────────────────────────────────
        var hdrBg = ParseColor(options.HeaderColor);
        float rowHeight = 25f;

        for (int i = 0; i < colConfigs.Count; i++)
        {
            var col = colConfigs[i];
            var x = leftMargin + columnWidths.Take(i).Sum();
            var rect = new XRect(x, y, columnWidths[i], rowHeight);

            // 背景描画
            if (hdrBg != null)
            {
                gfx.DrawRectangle(hdrBg, rect);
            }

            // テキスト描画
            var format = MapStringFormat(col.Align);
            gfx.DrawString(col.Label, fontBold, XBrushes.White, rect, format);
        }

        y += rowHeight;

        // ── データ行 ─────────────────────────────────────────
        var oddBg = string.IsNullOrWhiteSpace(options.OddRowColor)
            ? null
            : ParseColor(options.OddRowColor);
        var rowBorderColor = XColors.LightGray;

        int rowIdx = 0;
        foreach (var row in rows)
        {
            var bg = (rowIdx % 2 == 1 && oddBg != null) ? oddBg : null;
            rowHeight = RowMinHeight;

            for (int i = 0; i < colConfigs.Count; i++)
            {
                var col = colConfigs[i];
                var x = leftMargin + columnWidths.Take(i).Sum();
                var rect = new XRect(x, y, columnWidths[i], rowHeight);

                // 背景描画
                if (bg != null)
                {
                    gfx.DrawRectangle(bg, rect);
                }

                // 罫線描画
                var pen = new XPen(rowBorderColor, 0.5f);
                gfx.DrawLine(pen, rect.X, rect.Y + rect.Height, rect.X + rect.Width, rect.Y + rect.Height);

                // テキスト描画
                row.TryGetValue(col.Key, out var raw);
                var colDef = meta.Columns.GetValueOrDefault(col.Key);
                var text = colDef != null
                    ? ColumnValueFormatter.FormatValue(colDef.Type, raw, colDef.OptionLabels)
                    : raw?.ToString() ?? string.Empty;

                var format = MapStringFormat(col.Align);
                gfx.DrawString(text, fontNormal, XBrushes.Black, rect, format);
            }

            y += rowHeight;
            rowIdx++;

            // 改ページチェック
            if (y + rowHeight > pageSize.Height - bottomMargin)
            {
                page = document.AddPage();
                page.Width = pageSize.Width;
                page.Height = pageSize.Height;
                gfx.Dispose();
                gfx = XGraphics.FromPdfPage(page);
                y = topMargin;
            }
        }

        // ページ番号
        if (options.ShowPageNumbers)
        {
            for (int i = 0; i < document.Pages.Count; i++)
            {
                var p = document.Pages[i];
                var pageGfx = XGraphics.FromPdfPage(p);
                var pageNumFont = new XFont(font.FontFamily.Name, XUnit.FromPoint(8), XFontStyleEx.Regular);
                var pageNumBrush = new XSolidBrush(XColors.Gray);
                var text = $"- {i + 1} -";
                var size = pageGfx.MeasureString(text, pageNumFont);
                var x = XUnit.FromPoint(p.Width.Point / 2 - size.Width / 2);
                var yPos = XUnit.FromPoint(p.Height.Point - XUnit.FromPoint(24).Point);
                pageGfx.DrawString(text, pageNumFont, pageNumBrush, x, yPos);
                pageGfx.Dispose();
            }
        }

        gfx.Dispose();
        document.Save(ms);
        return ms.ToArray();
    }

    // ── ヘルパーメソッド ─────────────────────────────────────

    private static XSize ResolvePageSize(PdfExportOptions options)
    {
        var size = (options.PageSize ?? "A4").ToUpperInvariant() switch
        {
            "A3"     => new XSize(841.89, 1190.55),  // A3: 297mm x 420mm
            "LETTER" => new XSize(612, 792),         // Letter: 8.5in x 11in
            "LEGAL"  => new XSize(612, 1008),        // Legal: 8.5in x 14in
            _        => new XSize(595.28, 841.89),   // A4: 210mm x 297mm
        };
        return (options.Orientation ?? "portrait").ToLowerInvariant() == "landscape"
            ? new XSize(size.Height, size.Width)
            : size;
    }

    private static XFont LoadFont(string? fontFile, string? projectDir)
    {
        // 1. YAML 指定フォント
        if (!string.IsNullOrWhiteSpace(fontFile))
        {
            var resolvedPath = System.IO.Path.IsPathRooted(fontFile)
                ? fontFile
                : System.IO.Path.Combine(projectDir ?? Directory.GetCurrentDirectory(), fontFile);
            if (File.Exists(resolvedPath))
                return new XFont(resolvedPath, 9);
        }

        // 2. システム上の CJK フォントを自動検出
        foreach (var p in CjkFontPaths)
        {
            if (File.Exists(p))
                return new XFont(p, 9);
        }

        // 3. フォールバック（ラテン文字のみ対応）
        return new XFont("Arial", 9);
    }

    private static XSolidBrush? ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            int r = Convert.ToInt32(hex[0..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            return new XSolidBrush(XColor.FromArgb(255, (byte)r, (byte)g, (byte)b));
        }
        return new XSolidBrush(XColors.DarkBlue);
    }

    private static XStringFormat MapStringFormat(string align)
    {
        var format = new XStringFormat();
        format.LineAlignment = XLineAlignment.Center;
        format.Alignment = align?.ToLowerInvariant() switch
        {
            "center" => XStringAlignment.Center,
            "right"  => XStringAlignment.Far,
            _        => XStringAlignment.Near,
        };
        return format;
    }

    /// <summary>
    /// 列幅を決定する。YAML の pdf.columns で指定した幅を優先し、
    /// 指定のない列は残りの幅を等分する。
    /// </summary>
    private static List<(string Key, string Label, float Width, string Align)> BuildColumnConfigs(
        List<(string Key, string Label)> columns,
        PdfExportOptions options)
    {
        var pdfCols = options.Columns ?? new List<PdfColumnOptions>();

        // YAML 指定の幅の合計
        float usedPercent = pdfCols.Where(c => c.Width > 0).Sum(c => c.Width);
        // 幅未指定の列数
        int unspecified = columns.Count(c => !pdfCols.Any(p => p.Key == c.Key && p.Width > 0));
        float autoWidth = unspecified > 0
            ? Math.Max(1f, (100f - usedPercent) / unspecified)
            : 0f;

        return columns.Select(c =>
        {
            var opt = pdfCols.FirstOrDefault(p => p.Key == c.Key);
            var width = opt?.Width > 0 ? opt.Width : autoWidth;
            var label = opt?.Label ?? c.Label;
            var align = opt?.Align ?? "left";
            return (c.Key, label, width, align);
        }).ToList();
    }
}
