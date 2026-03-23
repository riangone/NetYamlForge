// ファイル概要：YAML 定義に基づいて PDF ファイルを生成するサービスです。
// iText 9.x (itext7 NuGet パッケージ) を使用します。
// ライセンス: iText は AGPL v3 または商用ライセンスで提供されます。

using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Pdf.Event;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
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
    // シンプルなデータ行の最低高さ（pt）
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

        var pageSize = ResolvePageSize(options);
        var pdfDoc = new PdfDocument(new PdfWriter(ms));
        pdfDoc.SetDefaultPageSize(pageSize);

        var bodyFont = LoadFont(options.FontFile, projectDir);

        if (options.ShowPageNumbers)
            pdfDoc.AddEventHandler(PdfDocumentEvent.END_PAGE, new PageNumberHandler(bodyFont, 8f));

        // ヘッダー・フッター用の余白を確保
        float topMargin = string.IsNullOrWhiteSpace(options.Title) ? 36f : 50f;
        float bottomMargin = options.ShowPageNumbers ? 40f : 24f;
        var doc = new Document(pdfDoc, pageSize);
        doc.SetMargins(topMargin, 36f, bottomMargin, 36f);
        doc.SetFont(bodyFont).SetFontSize(9f);

        // ── タイトルブロック ───────────────────────────────────
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            doc.Add(new Paragraph(options.Title)
                .SetFontSize(15f)
                .SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))
                .SetMarginBottom(2f));
        }

        if (options.ShowGeneratedAt)
        {
            doc.Add(new Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}")
                .SetFontSize(8f)
                .SetFontColor(new DeviceRgb(120, 120, 120))
                .SetMarginBottom(10f));
        }

        // ── テーブル列設定 ────────────────────────────────────
        var colConfigs = BuildColumnConfigs(columns, options);
        var widthArray = UnitValue.CreatePercentArray(colConfigs.Select(c => c.Width).ToArray());

        // largeTable=true: ストリーミング書き込みで大量データも低メモリで処理
        var table = new Table(widthArray, largeTable: true).UseAllAvailableWidth();
        doc.Add(table);

        // ── ヘッダー行 ────────────────────────────────────────
        var hdrBg = ParseHex(options.HeaderColor);
        foreach (var col in colConfigs)
        {
            table.AddHeaderCell(
                new Cell()
                    .SetBackgroundColor(hdrBg)
                    .SetBorder(Border.NO_BORDER)
                    .SetPaddingTop(5f).SetPaddingBottom(5f)
                    .SetPaddingLeft(6f).SetPaddingRight(6f)
                    .Add(new Paragraph(col.Label)
                        .SetFontSize(9f)
                        .SetFontColor(ColorConstants.WHITE)
                        .SetTextAlignment(MapAlign(col.Align))));
        }

        // ── データ行 ─────────────────────────────────────────
        var oddBg = string.IsNullOrWhiteSpace(options.OddRowColor)
            ? null
            : (Color?)ParseHex(options.OddRowColor);
        var rowBorderColor = new DeviceRgb(220, 220, 220);

        int rowIdx = 0;
        foreach (var row in rows)
        {
            var bg = (rowIdx % 2 == 1 && oddBg != null) ? oddBg : null;
            foreach (var col in colConfigs)
            {
                row.TryGetValue(col.Key, out var raw);
                var colDef = meta.Columns.GetValueOrDefault(col.Key);
                var text = colDef != null
                    ? ColumnValueFormatter.FormatValue(colDef.Type, raw, colDef.OptionLabels)
                    : raw?.ToString() ?? string.Empty;

                var cell = new Cell()
                    .SetMinHeight(RowMinHeight)
                    .SetBorderTop(Border.NO_BORDER)
                    .SetBorderLeft(Border.NO_BORDER)
                    .SetBorderRight(Border.NO_BORDER)
                    .SetBorderBottom(new SolidBorder(rowBorderColor, 0.5f))
                    .SetPaddingTop(4f).SetPaddingBottom(4f)
                    .SetPaddingLeft(6f).SetPaddingRight(6f)
                    .Add(new Paragraph(text)
                        .SetFontSize(9f)
                        .SetTextAlignment(MapAlign(col.Align)));

                if (bg != null) cell.SetBackgroundColor(bg);
                table.AddCell(cell);

                // Flush で使用メモリを都度解放する
                if (rowIdx % 50 == 0) table.Flush();
            }
            rowIdx++;
        }

        table.Complete();
        doc.Close();
        return ms.ToArray();
    }

    // ── ヘルパーメソッド ─────────────────────────────────────

    private static iText.Kernel.Geom.PageSize ResolvePageSize(PdfExportOptions options)
    {
        var size = (options.PageSize ?? "A4").ToUpperInvariant() switch
        {
            "A3"     => iText.Kernel.Geom.PageSize.A3,
            "LETTER" => iText.Kernel.Geom.PageSize.LETTER,
            "LEGAL"  => iText.Kernel.Geom.PageSize.LEGAL,
            _        => iText.Kernel.Geom.PageSize.A4,
        };
        return (options.Orientation ?? "portrait").ToLowerInvariant() == "landscape"
            ? size.Rotate()
            : size;
    }

    private static PdfFont LoadFont(string? fontFile, string? projectDir)
    {
        // 1. YAML 指定フォント
        if (!string.IsNullOrWhiteSpace(fontFile))
        {
            var resolvedPath = System.IO.Path.IsPathRooted(fontFile)
                ? fontFile
                : System.IO.Path.Combine(projectDir ?? Directory.GetCurrentDirectory(), fontFile);
            if (File.Exists(resolvedPath))
                return CreateEmbeddedFont(resolvedPath);
        }

        // 2. システム上の CJK フォントを自動検出
        foreach (var p in CjkFontPaths)
        {
            if (File.Exists(p))
                return CreateEmbeddedFont(p);
        }

        // 3. フォールバック（ラテン文字のみ対応）
        return PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
    }

    private static PdfFont CreateEmbeddedFont(string path)
        => PdfFontFactory.CreateFont(path, PdfEncodings.IDENTITY_H,
               PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);

    private static DeviceRgb ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            int r = Convert.ToInt32(hex[0..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            return new DeviceRgb(r, g, b);
        }
        return new DeviceRgb(30, 58, 95); // デフォルト紺色
    }

    private static TextAlignment MapAlign(string align) => align?.ToLowerInvariant() switch
    {
        "center" => TextAlignment.CENTER,
        "right"  => TextAlignment.RIGHT,
        _        => TextAlignment.LEFT,
    };

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

    // ── ページ番号フッターイベントハンドラー ──────────────────

    private sealed class PageNumberHandler : AbstractPdfDocumentEventHandler
    {
        private readonly PdfFont _font;
        private readonly float   _fontSize;

        public PageNumberHandler(PdfFont font, float fontSize)
        {
            _font     = font;
            _fontSize = fontSize;
        }

        protected override void OnAcceptedEvent(AbstractPdfDocumentEvent abstractEvent)
        {
            var ev      = (PdfDocumentEvent)abstractEvent;
            var pdfDoc  = ev.GetDocument();
            var page    = ev.GetPage();
            var pageNum = pdfDoc.GetPageNumber(page);
            var ps      = pdfDoc.GetDefaultPageSize();

            var canvas = new PdfCanvas(
                page.NewContentStreamBefore(), page.GetResources(), pdfDoc);

            var footerRect = new Rectangle(36, 16, ps.GetWidth() - 72, 16);
            using var layoutCanvas = new Canvas(canvas, footerRect);
            layoutCanvas.Add(
                new Paragraph($"- {pageNum} -")
                    .SetFont(_font)
                    .SetFontSize(_fontSize)
                    .SetFontColor(new DeviceRgb(140, 140, 140))
                    .SetTextAlignment(TextAlignment.CENTER));

            canvas.Release();
        }
    }
}
