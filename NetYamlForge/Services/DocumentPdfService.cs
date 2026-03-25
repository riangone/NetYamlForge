// ファイル概要: YAML 帳票テンプレートを読み込んで PDF を生成する汎用サービス。
// C# は 5 つのプリミティブ（line / paragraph / row / labelTable / dataTable）のみを定義します。
// すべての帳票レイアウトは YAML テンプレートで組み合わせて定義します。

using System.Text.RegularExpressions;
using iText.IO.Font;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using NetYamlForge.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public interface IDocumentPdfService
{
    /// <summary>
    /// プロジェクト固有の PDF テンプレートを読み込みます。
    /// </summary>
    PdfTemplateConfig? LoadTemplate(string projectDir, string templateName);

    /// <summary>
    /// 核心フレームワークの PDF テンプレートを読み込みます。
    /// </summary>
    PdfTemplateConfig? LoadGlobalTemplate(string templateName);

    byte[] Generate(
        PdfTemplateConfig template,
        IDictionary<string, object?> header,
        IDictionary<string, IList<IDictionary<string, object?>>> dataSources,
        string? projectDir = null);
}

public class DocumentPdfService : IDocumentPdfService
{
    // 核心フレームワークの PDF テンプレートディレクトリ
    private static readonly string GlobalTemplatesDir =
        System.IO.Path.Combine(AppContext.BaseDirectory, "Schemas", "pdf-templates");

    private static readonly string[] CjkFontPaths =
    [
        "/usr/share/fonts/opentype/ipafont-gothic/ipagp.ttf",
        "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/ipafont-mincho/ipamp.ttf",
        "/usr/share/fonts/truetype/vlgothic/VL-Gothic-Regular.ttf",
        "/Library/Fonts/Arial Unicode.ttf",
        "C:\\Windows\\Fonts\\msgothic.ttc",
    ];

    // ── テンプレート読み込み ──────────────────────────────────────────────────

    public PdfTemplateConfig? LoadTemplate(string projectDir, string templateName)
    {
        var path = System.IO.Path.Combine(projectDir, "pdf-templates", templateName + ".yaml");
        if (!System.IO.File.Exists(path)) return null;

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<PdfTemplateConfig>(System.IO.File.ReadAllText(path));
    }

    public PdfTemplateConfig? LoadGlobalTemplate(string templateName)
    {
        var path = System.IO.Path.Combine(GlobalTemplatesDir, templateName + ".yaml");
        if (!System.IO.File.Exists(path)) return null;

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<PdfTemplateConfig>(System.IO.File.ReadAllText(path));
    }

    // ── PDF 生成エントリポイント ──────────────────────────────────────────────

    public byte[] Generate(
        PdfTemplateConfig template,
        IDictionary<string, object?> header,
        IDictionary<string, IList<IDictionary<string, object?>>> dataSources,
        string? projectDir = null)
    {
        using var ms = new MemoryStream();
        var pageSize = ParsePageSize(template.PageSize, template.Orientation);
        var pdfDoc = new PdfDocument(new PdfWriter(ms));
        pdfDoc.SetDefaultPageSize(pageSize);
        var doc = new Document(pdfDoc, pageSize);

        var m = template.Margins;
        doc.SetMargins(
            m.Length > 0 ? m[0] : 36f,
            m.Length > 1 ? m[1] : 42f,
            m.Length > 2 ? m[2] : 36f,
            m.Length > 3 ? m[3] : 42f);

        var font = LoadFont(projectDir);
        doc.SetFont(font).SetFontSize(9f);

        var theme = BuildTheme(template.Theme);

        foreach (var section in template.Sections)
            RenderTopLevel(doc, font, theme, section, header, dataSources);

        doc.Close();
        return ms.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // トップレベルレンダリング（Document への追加）
    // ─────────────────────────────────────────────────────────────────────────

    private static void RenderTopLevel(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (s.Type == "line")
        {
            // LineSeparator は ILeafElement のためトップレベル専用
            var color = ResolveColor(s.Color, t) ?? t.Border;
            var line = new SolidLine(s.LineWeight);
            line.SetColor(color);
            doc.Add(new LineSeparator(line)
                .SetMarginTop(s.MarginTop ?? 0f)
                .SetMarginBottom(s.MarginBottom ?? 0f));
            return;
        }

        var element = BuildElement(font, t, s, h, ds);
        if (element != null) doc.Add(element);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 要素ビルド（再帰対応 — セル内からも呼ばれる）
    // ─────────────────────────────────────────────────────────────────────────

    private static IBlockElement? BuildElement(
        PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
        => s.Type switch
        {
            "paragraph"  => BuildParagraph(font, t, s, h, ds),
            "row"        => BuildRow(font, t, s, h, ds),
            "labelTable" => BuildLabelTable(font, t, s, h, ds),
            "dataTable"  => BuildDataTable(font, t, s, ds),
            _            => null,
        };

    // ── paragraph ────────────────────────────────────────────────────────────

    private static Paragraph BuildParagraph(
        PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        string value;
        if (s.Template != null)
            value = InterpolateTemplate(s.Template, h, ds);
        else if (s.Field != null)
            value = FormatValue(ResolveField(s.Field, h, ds), s.Format);
        else
            value = s.Text ?? "";

        var mainText = (s.Prefix ?? "") + value;
        var textColor = ResolveColor(s.Color, t);

        var p = new Paragraph().SetFont(font);
        if (s.MarginTop.HasValue)    p.SetMarginTop(s.MarginTop.Value);
        if (s.MarginBottom.HasValue) p.SetMarginBottom(s.MarginBottom.Value);

        var run = new Text(mainText).SetFontSize(s.FontSize);
        if (textColor != null) run.SetFontColor(textColor);
        if (s.Bold) run.SimulateBold();
        p.Add(run);

        if (s.Suffix != null)
        {
            var suffixRun = new Text(s.Suffix).SetFontSize(s.SuffixFontSize ?? s.FontSize);
            if (textColor != null) suffixRun.SetFontColor(textColor);
            p.Add(suffixRun);
        }

        if (s.Align != null) p.SetTextAlignment(MapAlign(s.Align));

        return p;
    }

    // ── row ──────────────────────────────────────────────────────────────────

    private static Table BuildRow(
        PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (s.Cells.Count == 0)
            return new Table(1).UseAllAvailableWidth().SetBorder(Border.NO_BORDER);

        // カラム幅を決定（省略時は均等分割）
        int colCount = s.ColumnWidths?.Length > 0
            ? s.ColumnWidths.Length
            : s.Cells.Max(c => c.ColSpan);
        float[] widths = s.ColumnWidths ?? Enumerable.Repeat(100f / colCount, colCount).ToArray();

        var tbl = new Table(UnitValue.CreatePercentArray(widths))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER);
        if (s.MarginTop.HasValue)    tbl.SetMarginTop(s.MarginTop.Value);
        if (s.MarginBottom.HasValue) tbl.SetMarginBottom(s.MarginBottom.Value);

        foreach (var cellCfg in s.Cells)
        {
            var cell = new Cell(cellCfg.RowSpan, cellCfg.ColSpan);

            // ボーダー（row セルはデフォルトなし）
            if (cellCfg.BorderWeight is > 0)
                cell.SetBorder(new SolidBorder(t.Border, cellCfg.BorderWeight.Value));
            else
                cell.SetBorder(Border.NO_BORDER);

            // 背景色
            var bg = ResolveColor(cellCfg.Background, t);
            if (bg != null) cell.SetBackgroundColor(bg);

            // パディング
            if (cellCfg.Padding.HasValue)    cell.SetPadding(cellCfg.Padding.Value);
            if (cellCfg.PaddingLeft.HasValue)   cell.SetPaddingLeft(cellCfg.PaddingLeft.Value);
            if (cellCfg.PaddingRight.HasValue)  cell.SetPaddingRight(cellCfg.PaddingRight.Value);
            if (cellCfg.PaddingTop.HasValue)    cell.SetPaddingTop(cellCfg.PaddingTop.Value);
            if (cellCfg.PaddingBottom.HasValue) cell.SetPaddingBottom(cellCfg.PaddingBottom.Value);

            // 縦揃え
            if (cellCfg.VAlign != null) cell.SetVerticalAlignment(MapVAlign(cellCfg.VAlign));

            // 最小高さ
            if (cellCfg.MinHeight.HasValue) cell.SetMinHeight(cellCfg.MinHeight.Value);

            // 再帰的に子要素をレンダリング
            foreach (var elem in cellCfg.Elements)
            {
                var built = BuildElement(font, t, elem, h, ds);
                if (built != null) cell.Add(built);
            }

            tbl.AddCell(cell);
        }

        return tbl;
    }

    // ── labelTable ───────────────────────────────────────────────────────────

    private static Table BuildLabelTable(
        PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        float[] widths = s.ColumnWidths ?? [35f, 65f];
        var tbl = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth();
        if (s.MarginTop.HasValue)    tbl.SetMarginTop(s.MarginTop.Value);
        if (s.MarginBottom.HasValue) tbl.SetMarginBottom(s.MarginBottom.Value);

        var border     = new SolidBorder(t.Border, s.BorderWeight);
        var labelBg    = ResolveColor(s.LabelBackground, t) ?? t.Label;
        var labelColor = s.LabelTextColor != null ? ResolveColor(s.LabelTextColor, t) : null;
        float pad = s.CellPadding;
        float fs  = s.FontSize;

        foreach (var row in s.Rows)
        {
            var rawVal = ResolveField(row.Field, h, ds);
            if (row.OmitIfEmpty && string.IsNullOrWhiteSpace(rawVal)) continue;
            if (row.OmitIfZero  && !decimal.TryParse(rawVal, out var dv)) dv = 0m;
            if (row.OmitIfZero  && decimal.TryParse(rawVal, out var dz) && dz == 0m) continue;

            // ラベルセル
            var labelCell = new Cell().SetBorder(border)
                .SetBackgroundColor(labelBg).SetPadding(pad).SetPaddingLeft(pad + 2f);
            var labelPar = new Paragraph(row.Label).SetFont(font).SetFontSize(fs);
            if (labelColor != null) labelPar.SetFontColor(labelColor);
            tbl.AddCell(labelCell.Add(labelPar));

            // 値セル
            var displayVal = FormatValue(rawVal, row.Format);
            var valueCell  = new Cell().SetBorder(border).SetPadding(pad).SetPaddingLeft(pad + 2f)
                .SetPaddingRight(pad + 2f);
            var valuePar = new Paragraph(displayVal).SetFont(font).SetFontSize(row.Bold ? fs + 1.5f : fs);
            if (row.Bold) valuePar.SimulateBold();
            // 通貨・数値は右揃え
            if (row.Format is "currency" or "quantity")
                valuePar.SetTextAlignment(TextAlignment.RIGHT);
            tbl.AddCell(valueCell.Add(valuePar));
        }

        return tbl;
    }

    // ── dataTable ────────────────────────────────────────────────────────────

    private static Table BuildDataTable(
        PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var items = new List<IDictionary<string, object?>>();
        if (s.DataSource != null && ds.TryGetValue(s.DataSource, out var src))
            items.AddRange(src);

        var widths = s.Columns.Select(c => c.Width).ToArray();
        var tbl = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth();
        if (s.MarginTop.HasValue)    tbl.SetMarginTop(s.MarginTop.Value);
        if (s.MarginBottom.HasValue) tbl.SetMarginBottom(s.MarginBottom.Value);

        var border     = new SolidBorder(t.Border, s.BorderWeight);
        var labelBg    = ResolveColor(s.LabelBackground, t) ?? t.Label;
        var labelColor = s.LabelTextColor != null ? ResolveColor(s.LabelTextColor, t) : null;
        var oddBg      = ResolveColor(s.OddRowBackground, t) ?? t.OddRow;
        float pad = s.CellPadding;
        float fs  = s.FontSize;

        // ヘッダー行
        foreach (var col in s.Columns)
        {
            var hCell = new Cell().SetBackgroundColor(labelBg).SetBorder(border).SetPadding(pad);
            var hPar  = new Paragraph(col.Label).SetFont(font).SetFontSize(fs)
                .SetTextAlignment(MapAlign(col.Align));
            if (labelColor != null) hPar.SetFontColor(labelColor);
            tbl.AddHeaderCell(hCell.Add(hPar));
        }

        // データ行（最低 minRows 行）
        var displayItems = items.ToList();
        while (displayItems.Count < s.MinRows)
            displayItems.Add(new Dictionary<string, object?>());

        int rowNum = 1;
        foreach (var item in displayItems)
        {
            bool isOdd   = rowNum % 2 == 1;
            bool hasData = item.Count > 0;
            foreach (var col in s.Columns)
            {
                string text;
                if (col.Field == "_rowNumber")
                    text = hasData ? rowNum.ToString() : "";
                else
                {
                    item.TryGetValue(col.Field, out var raw);
                    text = FormatValue(raw?.ToString() ?? "", col.Format);
                }

                var cell = new Cell().SetBorder(border).SetMinHeight(18f)
                    .SetPadding(pad).SetPaddingLeft(pad + 2f).SetPaddingRight(pad + 2f);
                cell.Add(new Paragraph(text).SetFont(font).SetFontSize(fs)
                    .SetTextAlignment(MapAlign(col.Align)));
                if (isOdd && hasData) cell.SetBackgroundColor(oddBg);
                tbl.AddCell(cell);
            }
            if (hasData) rowNum++;
        }

        return tbl;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ユーティリティ
    // ─────────────────────────────────────────────────────────────────────────

    private record ThemeColors(
        DeviceRgb Primary, DeviceRgb Label, DeviceRgb LabelText,
        DeviceRgb Subtle, DeviceRgb OddRow, DeviceRgb Border);

    private static ThemeColors BuildTheme(PdfThemeConfig cfg) => new(
        ParseColor(cfg.PrimaryColor),  ParseColor(cfg.LabelColor),
        ParseColor(cfg.LabelTextColor), ParseColor(cfg.SubtleBackground),
        ParseColor(cfg.OddRowColor),   ParseColor(cfg.BorderColor));

    /// <summary>テーマキーワードまたは hex 文字列から色を解決します</summary>
    private static DeviceRgb? ResolveColor(string? colorRef, ThemeColors t)
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
            _           => TryParseColor(colorRef),
        };
    }

    private static DeviceRgb ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        return new DeviceRgb(
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16));
    }

    private static DeviceRgb? TryParseColor(string hex)
    {
        try { return ParseColor(hex); }
        catch { return null; }
    }

    private static PageSize ParsePageSize(string size, string orientation)
    {
        var ps = size.ToUpperInvariant() switch
        {
            "A3"     => iText.Kernel.Geom.PageSize.A3,
            "LETTER" => iText.Kernel.Geom.PageSize.LETTER,
            "LEGAL"  => iText.Kernel.Geom.PageSize.LEGAL,
            _        => iText.Kernel.Geom.PageSize.A4,
        };
        return orientation.ToLowerInvariant() == "landscape" ? ps.Rotate() : ps;
    }

    private static PdfFont LoadFont(string? projectDir)
    {
        foreach (var path in CjkFontPaths)
        {
            if (System.IO.File.Exists(path))
                return PdfFontFactory.CreateFont(path, PdfEncodings.IDENTITY_H,
                    PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
        }
        return PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
    }

    private static TextAlignment MapAlign(string? align) => align?.ToLowerInvariant() switch
    {
        "center" => TextAlignment.CENTER,
        "right"  => TextAlignment.RIGHT,
        _        => TextAlignment.LEFT,
    };

    private static VerticalAlignment MapVAlign(string? vAlign) => vAlign?.ToLowerInvariant() switch
    {
        "middle" => VerticalAlignment.MIDDLE,
        "bottom" => VerticalAlignment.BOTTOM,
        _        => VerticalAlignment.TOP,
    };

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
            var srcName   = fieldRef[..dot];
            var fieldName = fieldRef[(dot + 1)..];
            if (ds.TryGetValue(srcName, out var src) && src.Count > 0)
                return GetStr(src[0], fieldName);
            return "";
        }
        return GetStr(h, fieldRef);
    }

    private static string InterpolateTemplate(
        string template,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var result = Regex.Replace(template, @"\{([^}]+)\}",
            m => ResolveField(m.Groups[1].Value, h, ds));
        return string.Join("\n", result.Split('\n')
            .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)));
    }

    private static string GetStr(IDictionary<string, object?>? d, string? key)
    {
        if (d == null || string.IsNullOrEmpty(key)) return "";
        return d.TryGetValue(key, out var v) && v != null ? v.ToString()! : "";
    }
}
