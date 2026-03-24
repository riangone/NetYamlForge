// ファイル概要: YAML 帳票テンプレート（pdf-templates/*.yaml）を読み込んで PDF を生成する
// 汎用ドキュメントサービス。C# 側にレイアウト・フィールド名・日本語文字列を持ちません。

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
    PdfTemplateConfig? LoadTemplate(string projectDir, string templateName);

    byte[] Generate(
        PdfTemplateConfig template,
        IDictionary<string, object?> header,
        IDictionary<string, IList<IDictionary<string, object?>>> dataSources,
        string? projectDir = null);
}

public class DocumentPdfService : IDocumentPdfService
{
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

        var yaml = System.IO.File.ReadAllText(path);
        return deserializer.Deserialize<PdfTemplateConfig>(yaml);
    }

    // ── PDF 生成エントリポイント ───────────────────────────────────────────────

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
            RenderSection(doc, font, theme, section, header, dataSources);

        doc.Close();
        return ms.ToArray();
    }

    // ── セクション種別ごとのレンダリング ─────────────────────────────────────

    private static void RenderSection(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        switch (s.Type)
        {
            case "documentHeader":    RenderDocumentHeader(doc, font, t, s, h); break;
            case "separator":         RenderSeparator(doc, t, s.Style); break;
            case "recipientBlock":    RenderRecipientBlock(doc, font, s, h, ds); break;
            case "infoWithSender":    RenderInfoWithSender(doc, font, t, s, h, ds); break;
            case "totalBanner":       RenderTotalBanner(doc, font, t, s, h); break;
            case "itemsTable":        RenderItemsTable(doc, font, t, s, ds); break;
            case "taxSummary":        RenderTaxSummary(doc, font, t, s, h); break;
            case "remarksBox":        RenderRemarksBox(doc, font, t, s, h, ds); break;
            case "contractParties":   RenderContractParties(doc, font, s, h, ds); break;
            case "contractInfoTable": RenderContractInfoTable(doc, font, t, s, h); break;
            case "contractSignatures":RenderContractSignatures(doc, font, t, s, h, ds); break;
        }
    }

    // ① documentHeader ────────────────────────────────────────────────────────

    private static void RenderDocumentHeader(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s, IDictionary<string, object?> h)
    {
        var tbl = new Table(UnitValue.CreatePercentArray([55f, 45f]))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(2f);

        tbl.AddCell(new Cell().SetBorder(Border.NO_BORDER)
            .SetVerticalAlignment(VerticalAlignment.MIDDLE)
            .Add(new Paragraph(s.Title ?? "")
                .SetFont(font).SetFontSize(s.TitleFontSize)
                .SetFontColor(t.Primary).SetTextAlignment(TextAlignment.CENTER)));

        var box = new Table(UnitValue.CreatePercentArray([38f, 62f])).UseAllAvailableWidth();
        var cb = new SolidBorder(t.Border, 0.5f);

        void AddBoxRow(string lbl, string val)
        {
            box.AddCell(new Cell().SetBorder(cb).SetBackgroundColor(t.Subtle).SetPadding(3f)
                .Add(new Paragraph(lbl).SetFont(font).SetFontSize(8f)
                    .SetTextAlignment(TextAlignment.CENTER)));
            box.AddCell(new Cell().SetBorder(cb).SetPadding(3f)
                .Add(new Paragraph(val).SetFont(font).SetFontSize(8f)));
        }
        if (s.NumberLabel != null)
            AddBoxRow(s.NumberLabel, GetStr(h, s.NumberField));
        if (s.DateLabel != null)
            AddBoxRow(s.DateLabel, GetStr(h, s.DateField));

        tbl.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetPaddingLeft(16f)
            .SetVerticalAlignment(VerticalAlignment.BOTTOM).Add(box));

        doc.Add(tbl);
    }

    // ② separator ─────────────────────────────────────────────────────────────

    private static void RenderSeparator(Document doc, ThemeColors t, string style)
    {
        if (style == "double")
        {
            var l1 = new SolidLine(2f); l1.SetColor(t.Primary);
            doc.Add(new LineSeparator(l1).SetMarginTop(4f).SetMarginBottom(2f));
            var l2 = new SolidLine(0.5f); l2.SetColor(t.Primary);
            doc.Add(new LineSeparator(l2).SetMarginTop(0f).SetMarginBottom(4f));
        }
        else if (style == "thick")
        {
            var l = new SolidLine(2f); l.SetColor(t.Primary);
            doc.Add(new LineSeparator(l).SetMarginTop(4f).SetMarginBottom(6f));
        }
        else
        {
            var l = new SolidLine(0.5f); l.SetColor(t.Border);
            doc.Add(new LineSeparator(l).SetMarginTop(4f).SetMarginBottom(4f));
        }
    }

    // ③ recipientBlock ────────────────────────────────────────────────────────

    private static void RenderRecipientBlock(
        Document doc, PdfFont font,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var name = ResolveField(s.NameSource, h, ds);
        doc.Add(new Paragraph()
            .SetMarginTop(6f).SetMarginBottom(0f)
            .Add(new Text(name).SetFont(font).SetFontSize(s.NameFontSize))
            .Add(new Text(s.Suffix ?? "").SetFont(font).SetFontSize(9f)));
        if (!string.IsNullOrWhiteSpace(s.IntroText))
            doc.Add(new Paragraph(s.IntroText).SetFont(font).SetFontSize(9f)
                .SetMarginTop(2f).SetMarginBottom(6f));
    }

    // ④ infoWithSender ────────────────────────────────────────────────────────

    private static void RenderInfoWithSender(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var outer = new Table(UnitValue.CreatePercentArray([55f, 45f]))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(8f);

        // 左: 情報テーブル
        var leftTbl = new Table(UnitValue.CreatePercentArray([32f, 68f])).UseAllAvailableWidth();
        var b = new SolidBorder(t.Border, 0.5f);
        foreach (var row in s.InfoRows)
        {
            var val = GetStr(h, row.Field);
            if (row.OmitIfEmpty && string.IsNullOrWhiteSpace(val)) continue;
            leftTbl.AddCell(new Cell().SetBorder(b)
                .SetBackgroundColor(t.Label).SetPadding(4f).SetPaddingLeft(6f)
                .Add(new Paragraph(row.Label).SetFont(font).SetFontSize(8.5f)
                    .SetFontColor(t.LabelText)));
            leftTbl.AddCell(new Cell().SetBorder(b).SetPadding(4f).SetPaddingLeft(6f)
                .Add(new Paragraph(val).SetFont(font).SetFontSize(8.5f)));
        }
        outer.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetPaddingRight(12f).Add(leftTbl));

        // 右: 送付元情報
        var rightP = new Paragraph().SetFont(font).SetFontSize(8.5f);
        bool first = true;
        foreach (var sf in s.SenderFields)
        {
            var val = GetStr(h, sf.Field);
            if (string.IsNullOrWhiteSpace(val)) continue;
            if (!first) rightP.Add("\n");
            var text = new Text((sf.Prefix ?? "") + val).SetFontSize(sf.FontSize);
            rightP.Add(text);
            first = false;
        }
        outer.AddCell(new Cell().SetBorder(Border.NO_BORDER)
            .SetVerticalAlignment(VerticalAlignment.TOP).SetPaddingTop(4f)
            .Add(rightP));

        doc.Add(outer);
    }

    // ⑤ totalBanner ───────────────────────────────────────────────────────────

    private static void RenderTotalBanner(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s, IDictionary<string, object?> h)
    {
        var total = GetDecimal(h, s.Field);
        var tbl = new Table(UnitValue.CreatePercentArray([30f, 40f, 30f]))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(4f);

        tbl.AddCell(new Cell().SetBorder(Border.NO_BORDER)
            .Add(new Paragraph(s.Label ?? "").SetFont(font).SetFontSize(10f)));
        tbl.AddCell(new Cell().SetBorder(new SolidBorder(t.Border, 1f)).SetPadding(4f)
            .Add(new Paragraph(FormatCurrency(total)).SetFont(font).SetFontSize(11f)
                .SetTextAlignment(TextAlignment.RIGHT)));
        tbl.AddCell(new Cell().SetBorder(Border.NO_BORDER)
            .SetVerticalAlignment(VerticalAlignment.MIDDLE)
            .Add(new Paragraph(s.BannerSuffix ?? "").SetFont(font).SetFontSize(9f)));
        doc.Add(tbl);
    }

    // ⑥ itemsTable ────────────────────────────────────────────────────────────

    private static void RenderItemsTable(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var items = new List<IDictionary<string, object?>>();
        if (s.DataSource != null && ds.TryGetValue(s.DataSource, out var src))
            items.AddRange(src);

        var widths = s.Columns.Select(c => c.Width).ToArray();
        var tbl = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth();
        var b = new SolidBorder(t.Border, 0.5f);

        // ヘッダー行
        foreach (var col in s.Columns)
        {
            tbl.AddHeaderCell(new Cell().SetBackgroundColor(t.Label).SetBorder(b).SetPadding(4f)
                .Add(new Paragraph(col.Label).SetFont(font).SetFontSize(8.5f)
                    .SetFontColor(t.LabelText).SetTextAlignment(MapAlign(col.Align))));
        }

        // データ行（最低 minRows 行）
        var displayItems = items.ToList();
        while (displayItems.Count < s.MinRows)
            displayItems.Add(new Dictionary<string, object?>());

        int rowNum = 1;
        foreach (var item in displayItems)
        {
            bool isOdd = rowNum % 2 == 1;
            bool hasData = item.Count > 0;
            foreach (var col in s.Columns)
            {
                string text;
                if (col.Field == "_rowNumber")
                    text = hasData ? rowNum.ToString() : "";
                else
                    text = FormatItemCell(col, item);

                var cell = new Cell().SetBorder(b).SetMinHeight(18f)
                    .SetPadding(3f).SetPaddingLeft(5f).SetPaddingRight(5f)
                    .Add(new Paragraph(text).SetFont(font).SetFontSize(8.5f)
                        .SetTextAlignment(MapAlign(col.Align)));
                if (isOdd && hasData) cell.SetBackgroundColor(t.OddRow);
                tbl.AddCell(cell);
            }
            if (hasData) rowNum++;
        }
        doc.Add(tbl);
    }

    // ⑦ taxSummary ────────────────────────────────────────────────────────────

    private static void RenderTaxSummary(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s, IDictionary<string, object?> h)
    {
        var outer = new Table(UnitValue.CreatePercentArray([55f, 45f]))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginTop(0f);
        outer.AddCell(new Cell().SetBorder(Border.NO_BORDER));

        var summaryTbl = new Table(UnitValue.CreatePercentArray([55f, 45f])).UseAllAvailableWidth();
        var b = new SolidBorder(t.Border, 0.5f);

        foreach (var row in s.TaxRows)
        {
            var val = GetDecimal(h, row.Field);
            if (row.OmitIfZero && val == 0m) continue;

            summaryTbl.AddCell(new Cell().SetBorder(b)
                .SetBackgroundColor(t.Subtle).SetPadding(3f).SetPaddingLeft(6f)
                .Add(new Paragraph(row.Label).SetFont(font).SetFontSize(8.5f)));

            var p = new Paragraph(FormatCurrency(val)).SetFont(font)
                .SetFontSize(row.Bold ? 10f : 8.5f)
                .SetTextAlignment(TextAlignment.RIGHT);
            summaryTbl.AddCell(new Cell().SetBorder(b).SetPadding(3f).SetPaddingRight(6f).Add(p));
        }

        outer.AddCell(new Cell().SetBorder(Border.NO_BORDER).Add(summaryTbl));
        doc.Add(outer);
    }

    // ⑧ remarksBox ────────────────────────────────────────────────────────────

    private static void RenderRemarksBox(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var text = string.IsNullOrWhiteSpace(s.RemarksTemplate)
            ? ""
            : InterpolateTemplate(s.RemarksTemplate, h, ds);

        var tbl = new Table(UnitValue.CreatePercentArray([15f, 85f]))
            .UseAllAvailableWidth().SetMarginTop(8f);
        var b = new SolidBorder(t.Border, 0.5f);

        tbl.AddCell(new Cell().SetBorder(b).SetBackgroundColor(t.Label)
            .SetPadding(6f).SetVerticalAlignment(VerticalAlignment.MIDDLE)
            .Add(new Paragraph(s.Label ?? "").SetFont(font).SetFontSize(9f)
                .SetFontColor(t.LabelText).SetTextAlignment(TextAlignment.CENTER)));
        tbl.AddCell(new Cell().SetBorder(b).SetMinHeight(s.MinHeight).SetPadding(6f)
            .Add(new Paragraph(text).SetFont(font).SetFontSize(8.5f)));

        doc.Add(tbl);
    }

    // ⑨ contractParties ───────────────────────────────────────────────────────

    private static void RenderContractParties(
        Document doc, PdfFont font,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var partyA = ResolveField(s.PartyASource, h, ds);
        var partyB = GetStr(h, s.PartyBField);

        doc.Add(new Paragraph()
            .SetMarginTop(8f).SetMarginBottom(2f)
            .Add(new Text(partyA).SetFont(font).SetFontSize(13f))
            .Add(new Text(s.PartyALabel ?? "").SetFont(font).SetFontSize(9f)));
        doc.Add(new Paragraph()
            .SetMarginBottom(6f)
            .Add(new Text(string.IsNullOrWhiteSpace(partyB) ? "　" : partyB)
                .SetFont(font).SetFontSize(9f))
            .Add(new Text(s.PartyBLabel ?? "").SetFont(font).SetFontSize(9f)));

        if (!string.IsNullOrWhiteSpace(s.BodyTemplate))
        {
            var body = InterpolateTemplate(s.BodyTemplate, h, ds);
            doc.Add(new Paragraph(body).SetFont(font).SetFontSize(9f).SetMarginBottom(8f));
        }
    }

    // ⑩ contractInfoTable ─────────────────────────────────────────────────────

    private static void RenderContractInfoTable(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s, IDictionary<string, object?> h)
    {
        var tbl = new Table(UnitValue.CreatePercentArray([28f, 72f])).UseAllAvailableWidth();
        var b = new SolidBorder(t.Border, 0.5f);
        foreach (var row in s.InfoRows)
        {
            var val = GetStr(h, row.Field);
            if (row.OmitIfEmpty && string.IsNullOrWhiteSpace(val)) continue;
            tbl.AddCell(new Cell().SetBorder(b).SetBackgroundColor(t.Label)
                .SetPadding(4f).SetPaddingLeft(6f)
                .Add(new Paragraph(row.Label).SetFont(font).SetFontSize(8.5f)
                    .SetFontColor(t.LabelText)));
            tbl.AddCell(new Cell().SetBorder(b).SetPadding(4f).SetPaddingLeft(6f)
                .Add(new Paragraph(val).SetFont(font).SetFontSize(8.5f)));
        }
        doc.Add(tbl.SetMarginBottom(12f));
    }

    // ⑪ contractSignatures ────────────────────────────────────────────────────

    private static void RenderContractSignatures(
        Document doc, PdfFont font, ThemeColors t,
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        int count = s.Signatories.Count;
        if (count == 0) return;

        float[] colWidths = Enumerable.Repeat(100f / count, count).ToArray();
        var sigTbl = new Table(UnitValue.CreatePercentArray(colWidths))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER);
        var sb = new SolidBorder(t.Border, 0.5f);

        for (int i = 0; i < count; i++)
        {
            var sig = s.Signatories[i];
            var name = ResolveField(sig.NameSource, h, ds);
            var signatory = GetStr(h, sig.SignatoryField);
            if (string.IsNullOrWhiteSpace(signatory))
                signatory = sig.SignatoryFallback ?? "";

            var block = new Table(UnitValue.CreatePercentArray([35f, 65f])).UseAllAvailableWidth();
            block.AddCell(new Cell(2, 1).SetBorder(sb).SetBackgroundColor(t.Label)
                .SetPadding(4f).SetVerticalAlignment(VerticalAlignment.MIDDLE)
                .Add(new Paragraph(sig.Role).SetFont(font).SetFontSize(8.5f)
                    .SetFontColor(t.LabelText).SetTextAlignment(TextAlignment.CENTER)));
            block.AddCell(new Cell().SetBorder(sb).SetPadding(4f)
                .Add(new Paragraph(name).SetFont(font).SetFontSize(10f)));
            block.AddCell(new Cell().SetBorder(sb).SetPadding(4f)
                .Add(new Paragraph(signatory).SetFont(font).SetFontSize(8.5f)));

            bool isLast = i == count - 1;
            sigTbl.AddCell(new Cell().SetBorder(Border.NO_BORDER)
                .SetPaddingRight(isLast ? 0f : 8f).Add(block));
        }
        doc.Add(sigTbl.SetMarginBottom(8f));
    }

    // ── ユーティリティ ────────────────────────────────────────────────────────

    private record ThemeColors(
        DeviceRgb Primary, DeviceRgb Label, DeviceRgb LabelText,
        DeviceRgb Subtle, DeviceRgb OddRow, DeviceRgb Border);

    private static ThemeColors BuildTheme(PdfThemeConfig cfg) => new(
        ParseColor(cfg.PrimaryColor),
        ParseColor(cfg.LabelColor),
        ParseColor(cfg.LabelTextColor),
        ParseColor(cfg.SubtleBackground),
        ParseColor(cfg.OddRowColor),
        ParseColor(cfg.BorderColor));

    private static DeviceRgb ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return new DeviceRgb(0, 0, 0);
        return new DeviceRgb(
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16));
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

    private static string FormatCurrency(decimal v)
        => v == 0 ? "¥0" : $"¥{v:N0}";

    private static string FormatItemCell(ItemColumnConfig col, IDictionary<string, object?> item)
    {
        item.TryGetValue(col.Field, out var raw);
        if (raw == null) return "";

        return col.Format switch
        {
            "currency" => decimal.TryParse(raw.ToString(), out var d) ? FormatCurrency(d) : raw.ToString() ?? "",
            "quantity" => decimal.TryParse(raw.ToString(), out var q)
                ? (q == Math.Floor(q) ? ((int)q).ToString() : q.ToString("N1"))
                : raw.ToString() ?? "",
            _ => raw.ToString() ?? "",
        };
    }

    /// <summary>
    /// "dataSource.Field" または "Field" 形式のフィールド参照を解決します。
    /// dataSource 形式の場合はデータソースの最初の行を使用します。
    /// </summary>
    private static string ResolveField(
        string? fieldRef,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (string.IsNullOrEmpty(fieldRef)) return "";

        var dotIdx = fieldRef.IndexOf('.');
        if (dotIdx > 0)
        {
            var srcName = fieldRef[..dotIdx];
            var fieldName = fieldRef[(dotIdx + 1)..];
            if (ds.TryGetValue(srcName, out var src) && src.Count > 0)
                return GetStr(src[0], fieldName);
            return "";
        }
        return GetStr(h, fieldRef);
    }

    /// <summary>
    /// テンプレート文字列の {FieldName} または {dataSource.Field} を解決します。
    /// 解決後に空行を除去します。
    /// </summary>
    private static string InterpolateTemplate(
        string template,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var result = Regex.Replace(template, @"\{([^}]+)\}", m =>
            ResolveField(m.Groups[1].Value, h, ds));

        var lines = result.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l));
        return string.Join("\n", lines);
    }

    private static string GetStr(IDictionary<string, object?>? d, string? key)
    {
        if (d == null || string.IsNullOrEmpty(key)) return "";
        return d.TryGetValue(key, out var v) && v != null ? v.ToString()! : "";
    }

    private static decimal GetDecimal(IDictionary<string, object?> d, string? key)
    {
        if (string.IsNullOrEmpty(key)) return 0m;
        if (d.TryGetValue(key, out var v) && v != null &&
            decimal.TryParse(v.ToString(), out var r))
            return r;
        return 0m;
    }
}
