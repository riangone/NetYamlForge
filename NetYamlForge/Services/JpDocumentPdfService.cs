// ファイル概要: 日本向けビジネス文書（見積書・請求書・納品書・契約書）の
// 帳票形式 PDF を生成するサービスです。
// Wondershare テンプレート (delivery-slip04) スタイルに準拠します。

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

namespace NetYamlForge.Services;

/// <summary>
/// 帳票生成に必要なデータコンテキスト
/// </summary>
public record JpDocumentContext(
    string DocType,                                        // delivery | invoice | estimate | contract
    IDictionary<string, object?> Header,                   // メインレコードのフィールド
    IDictionary<string, object?>? Customer,                // Customer テーブルの行
    IList<IDictionary<string, object?>> Items,             // 明細行（見積・請求・納品）
    string? ProjectDir = null
);

public interface IJpDocumentPdfService
{
    byte[] Generate(JpDocumentContext ctx);
}

public class JpDocumentPdfService : IJpDocumentPdfService
{
    // ── 色定数（Wondershare テンプレートに合わせた水色系） ──────────────────
    private static readonly DeviceRgb LabelBlue   = new(120, 180, 204);  // ラベル背景（水色）
    private static readonly DeviceRgb DarkBlue    = new(28,  54,  88);   // タイトル・区切り線
    private static readonly DeviceRgb LightGray   = new(240, 240, 240);  // 番号ボックス背景
    private static readonly DeviceRgb BorderColor = new(180, 180, 180);  // 表罫線
    private static readonly DeviceRgb OddRow      = new(248, 252, 254);  // 奇数行背景

    private static readonly string[] CjkFontPaths =
    {
        "/usr/share/fonts/opentype/ipafont-gothic/ipagp.ttf",
        "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/ipafont-mincho/ipamp.ttf",
        "/usr/share/fonts/truetype/vlgothic/VL-Gothic-Regular.ttf",
        "/Library/Fonts/Arial Unicode.ttf",
        "C:\\Windows\\Fonts\\msgothic.ttc",
    };

    public byte[] Generate(JpDocumentContext ctx)
    {
        using var ms = new MemoryStream();
        var pdfDoc = new PdfDocument(new PdfWriter(ms));
        pdfDoc.SetDefaultPageSize(PageSize.A4);
        var doc = new Document(pdfDoc, PageSize.A4);
        doc.SetMargins(36f, 42f, 36f, 42f);

        var font = LoadFont(ctx.ProjectDir);
        doc.SetFont(font).SetFontSize(9f);

        switch (ctx.DocType)
        {
            case "delivery": RenderDelivery(doc, font, ctx); break;
            case "invoice":  RenderInvoice(doc, font, ctx); break;
            case "estimate": RenderEstimate(doc, font, ctx); break;
            case "contract": RenderContract(doc, font, ctx); break;
            default:
                doc.Add(new Paragraph("不明な文書種別: " + ctx.DocType).SetFont(font));
                break;
        }

        doc.Close();
        return ms.ToArray();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 文書種別ごとのレンダリング
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void RenderDelivery(Document doc, PdfFont font, JpDocumentContext ctx)
    {
        var h  = ctx.Header;
        var cu = ctx.Customer;

        AddTitleSection(doc, font, "納　品　書",
            "納品No.", GetStr(h, "DeliveryNo"),
            "納品日",  GetStr(h, "DeliveryDate"));
        AddThickLine(doc);
        AddRecipientAndCompany(doc, font,
            GetStr(cu, "Name"), "下記のとおり、納品いたします。",
            GetStr(h, "CompanyStamp"), GetStr(h, "PreparedBy"));
        AddInfoAndCompanyTable(doc, font,
            new[]
            {
                ("件名",     GetStr(h, "Title")),
                ("納品日",   GetStr(h, "DeliveryDate")),
                ("納品場所", GetStr(h, "DeliveryPlace")),
                ("支払条件", GetStr(h, "Remarks") == "" ? "—" : ""),
            },
            GetStr(h, "CompanyStamp"), GetStr(h, "PreparedBy"));
        AddTotalBanner(doc, font, GetDecimal(h, "Total"));
        AddDoubleLine(doc);
        AddItemsTable(doc, font, ctx.Items, DeliveryItemCols());
        AddTaxSummary(doc, font,
            GetDecimal(h, "Subtotal"),
            GetDecimal(h, "TaxAmount10"), GetDecimal(h, "TaxAmount8"),
            GetDecimal(h, "Total"), withholding: false);

        var remarks = new System.Text.StringBuilder();
        var inspDays = GetInt(h, "InspectionPeriodDays");
        if (inspDays > 0)
            remarks.AppendLine($"検収期間：納品後 {inspDays} 営業日以内にご連絡ください。");
        var confirmed = GetStr(h, "ReceiptConfirmedDate");
        if (!string.IsNullOrWhiteSpace(confirmed))
            remarks.AppendLine($"受領確認日：{confirmed}");
        remarks.Append(GetStr(h, "Remarks"));
        AddRemarksBox(doc, font, remarks.ToString().Trim());
    }

    private void RenderInvoice(Document doc, PdfFont font, JpDocumentContext ctx)
    {
        var h  = ctx.Header;
        var cu = ctx.Customer;

        AddTitleSection(doc, font, "請　求　書",
            "請求番号", GetStr(h, "InvoiceNo"),
            "請求日",   GetStr(h, "IssueDate"));
        AddThickLine(doc);
        AddRecipientAndCompany(doc, font,
            GetStr(cu, "Name"), "下記のとおり、ご請求申し上げます。",
            GetStr(h, "CompanyStamp"), GetStr(h, "PreparedBy"));

        var regNo = GetStr(h, "RegistrationNo");
        var infoRows = new List<(string, string)>
        {
            ("件名",     GetStr(h, "Title")),
            ("請求日",   GetStr(h, "IssueDate")),
            ("支払期日", GetStr(h, "DueDate")),
        };
        if (!string.IsNullOrWhiteSpace(regNo))
            infoRows.Add(("登録番号", regNo));

        AddInfoAndCompanyTable(doc, font, infoRows.ToArray(),
            GetStr(h, "CompanyStamp"), GetStr(h, "PreparedBy"));
        AddTotalBanner(doc, font, GetDecimal(h, "Total"));
        AddDoubleLine(doc);
        AddItemsTable(doc, font, ctx.Items, InvoiceItemCols());
        AddTaxSummary(doc, font,
            GetDecimal(h, "Subtotal"),
            GetDecimal(h, "TaxAmount10"), GetDecimal(h, "TaxAmount8"),
            GetDecimal(h, "Total"), withholding: false);

        var bank = $"{GetStr(h, "BankName")} {GetStr(h, "BranchName")} " +
                   $"{GetStr(h, "AccountType")}口座 No.{GetStr(h, "AccountNo")} " +
                   $"名義：{GetStr(h, "AccountHolder")}";
        var transferNote = GetStr(h, "TransferFeeNote");
        var remarksLines = new List<string>();
        remarksLines.Add("【振込先】" + bank.Trim());
        if (!string.IsNullOrWhiteSpace(transferNote)) remarksLines.Add(transferNote);
        var rem = GetStr(h, "Remarks");
        if (!string.IsNullOrWhiteSpace(rem)) remarksLines.Add(rem);
        AddRemarksBox(doc, font, string.Join("\n", remarksLines));
    }

    private void RenderEstimate(Document doc, PdfFont font, JpDocumentContext ctx)
    {
        var h  = ctx.Header;
        var cu = ctx.Customer;

        AddTitleSection(doc, font, "御　見　積　書",
            "見積番号", GetStr(h, "EstimateNo"),
            "見積日",   GetStr(h, "IssueDate"));
        AddThickLine(doc);
        AddRecipientAndCompany(doc, font,
            GetStr(cu, "Name"), "下記のとおり、お見積り申し上げます。",
            GetStr(h, "CompanyStamp"), GetStr(h, "PreparedBy"));
        AddInfoAndCompanyTable(doc, font,
            new[]
            {
                ("件名",     GetStr(h, "Title")),
                ("見積日",   GetStr(h, "IssueDate")),
                ("有効期限", GetStr(h, "ExpiryDate")),
                ("納期",     GetStr(h, "DeliveryDays")),
                ("支払条件", GetStr(h, "PaymentTerms")),
            },
            GetStr(h, "CompanyStamp"), GetStr(h, "PreparedBy"));
        AddTotalBanner(doc, font, GetDecimal(h, "Total"));
        AddDoubleLine(doc);
        AddItemsTable(doc, font, ctx.Items, EstimateItemCols());
        AddTaxSummary(doc, font,
            GetDecimal(h, "Subtotal"),
            GetDecimal(h, "TaxAmount10"), GetDecimal(h, "TaxAmount8"),
            GetDecimal(h, "Total"), withholding: false);

        var sb = new System.Text.StringBuilder();
        var v = GetStr(h, "ValidityNote");
        var x = GetStr(h, "ExclusionNote");
        var r = GetStr(h, "Remarks");
        if (!string.IsNullOrWhiteSpace(v)) sb.AppendLine(v);
        if (!string.IsNullOrWhiteSpace(x)) sb.AppendLine(x);
        if (!string.IsNullOrWhiteSpace(r)) sb.Append(r);
        AddRemarksBox(doc, font, sb.ToString().Trim());
    }

    private void RenderContract(Document doc, PdfFont font, JpDocumentContext ctx)
    {
        var h  = ctx.Header;
        var cu = ctx.Customer;
        bool isElec = GetBool(h, "IsElectronic");
        int  stamp  = GetInt(h, "StampTaxAmount");

        AddTitleSection(doc, font, "契　約　書",
            "契約番号", GetStr(h, "ContractNo"),
            "締結日",   GetStr(h, "SignedDate"));
        AddThickLine(doc);

        // 甲乙の宛名
        doc.Add(new Paragraph()
            .SetMarginTop(8f).SetMarginBottom(2f)
            .Add(new Text(GetStr(cu, "Name")).SetFont(font).SetFontSize(13f))
            .Add(new Text("　（甲）").SetFont(font).SetFontSize(9f)));
        doc.Add(new Paragraph()
            .SetMarginBottom(6f)
            .Add(new Text(GetStr(h, "OurSignatory") == "" ? "　" : GetStr(h, "OurSignatory"))
                .SetFont(font).SetFontSize(9f))
            .Add(new Text("　（乙）").SetFont(font).SetFontSize(9f)));
        doc.Add(new Paragraph(
            $"「{GetStr(h, "Title")}」について、甲乙は以下のとおり合意し、本契約を締結する。")
            .SetFont(font).SetFontSize(9f).SetMarginBottom(8f));

        var infoRows = new List<(string, string)>
        {
            ("契約種別",   GetStr(h, "ContractType")),
            ("契約開始日", GetStr(h, "StartDate")),
            ("契約終了日", GetStr(h, "EndDate")),
            ("自動更新",   GetBool(h, "AutoRenew") ? "あり（前月末まで解約申告要）" : "なし"),
            ("契約金額",   FormatAmount(GetDecimal(h, "ContractAmount"))),
            ("支払条件",   GetStr(h, "PaymentTerms")),
            ("契約形態",   isElec
                ? "電子契約（電子署名法適用・印紙不要）"
                : $"紙契約（印紙税：{stamp:N0}円）"),
            ("合意管轄",   GetStr(h, "JurisdictionCourt")),
            ("準拠法",     GetStr(h, "GoverningLaw")),
        };

        // 署名欄を含む全幅テーブル
        AddContractInfoTable(doc, font, infoRows.ToArray(),
            GetStr(cu, "Name"),            GetStr(h, "TheirSignatory"),
            GetStr(h, "OurSignatory"),     GetStr(h, "SignedDate"));
        AddRemarksBox(doc, font, GetStr(h, "Remarks"));
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 共通 UI コンポーネント
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// ① タイトルセクション（左：文書名、右：番号・日付ボックス）
    private static void AddTitleSection(Document doc, PdfFont font,
        string docTitle,
        string numLabel, string docNumber,
        string dateLabel, string docDate)
    {
        var tbl = new Table(UnitValue.CreatePercentArray(new[] { 55f, 45f }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(2f);

        // 左：文書名
        tbl.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetVerticalAlignment(VerticalAlignment.MIDDLE)
            .Add(new Paragraph(docTitle).SetFont(font).SetFontSize(20f)
                .SetFontColor(DarkBlue).SetTextAlignment(TextAlignment.CENTER)));

        // 右：番号ボックス
        var box = new Table(UnitValue.CreatePercentArray(new[] { 38f, 62f })).UseAllAvailableWidth();
        var cellBorder = new SolidBorder(BorderColor, 0.5f);

        void AddBoxRow(string lbl, string val)
        {
            box.AddCell(new Cell().SetBorder(cellBorder).SetBackgroundColor(LightGray)
                .SetPadding(3f)
                .Add(new Paragraph(lbl).SetFont(font).SetFontSize(8f)
                    .SetTextAlignment(TextAlignment.CENTER)));
            box.AddCell(new Cell().SetBorder(cellBorder).SetPadding(3f)
                .Add(new Paragraph(val).SetFont(font).SetFontSize(8f)));
        }
        AddBoxRow(numLabel,  docNumber);
        AddBoxRow(dateLabel, docDate);

        tbl.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetPaddingLeft(16f)
            .SetVerticalAlignment(VerticalAlignment.BOTTOM).Add(box));

        doc.Add(tbl);
    }

    /// ② 宛名＋自社情報（2 カラム）
    private static void AddRecipientAndCompany(Document doc, PdfFont font,
        string customerName, string introText,
        string companyName, string preparedBy)
    {
        // 宛名ブロック（左寄せ）
        doc.Add(new Paragraph()
            .SetMarginTop(6f).SetMarginBottom(0f)
            .Add(new Text(customerName).SetFont(font).SetFontSize(13f))
            .Add(new Text("　御中").SetFont(font).SetFontSize(9f)));
        doc.Add(new Paragraph(introText).SetFont(font).SetFontSize(9f)
            .SetMarginTop(2f).SetMarginBottom(6f));
    }

    /// ③ 情報テーブル（左）＋ 自社情報（右）の 2 カラムレイアウト
    private static void AddInfoAndCompanyTable(Document doc, PdfFont font,
        (string Label, string Value)[] infoRows,
        string companyName, string preparedBy)
    {
        var outer = new Table(UnitValue.CreatePercentArray(new[] { 55f, 45f }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(8f);

        // 左：情報テーブル（件名・納期・支払条件など）
        var leftTbl = new Table(UnitValue.CreatePercentArray(new[] { 32f, 68f })).UseAllAvailableWidth();
        var b = new SolidBorder(BorderColor, 0.5f);
        foreach (var (lbl, val) in infoRows.Where(r => !string.IsNullOrWhiteSpace(r.Label)))
        {
            leftTbl.AddCell(new Cell().SetBorder(b)
                .SetBackgroundColor(LabelBlue).SetPadding(4f).SetPaddingLeft(6f)
                .Add(new Paragraph(lbl).SetFont(font).SetFontSize(8.5f)
                    .SetFontColor(ColorConstants.WHITE)));
            leftTbl.AddCell(new Cell().SetBorder(b).SetPadding(4f).SetPaddingLeft(6f)
                .Add(new Paragraph(val).SetFont(font).SetFontSize(8.5f)));
        }
        outer.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetPaddingRight(12f).Add(leftTbl));

        // 右：自社情報（テキストブロック）
        var rightContent = new Paragraph()
            .SetFont(font).SetFontSize(8.5f)
            .SetTextAlignment(TextAlignment.LEFT);
        rightContent.Add(new Text(companyName).SetFontSize(10f)).Add("\n");
        rightContent.Add(new Text("担当：" + preparedBy));
        outer.AddCell(new Cell().SetBorder(Border.NO_BORDER)
            .SetVerticalAlignment(VerticalAlignment.TOP)
            .SetPaddingTop(4f)
            .Add(rightContent));

        doc.Add(outer);
    }

    /// ④ 合計金額バナー
    private static void AddTotalBanner(Document doc, PdfFont font, decimal total)
    {
        var tbl = new Table(UnitValue.CreatePercentArray(new[] { 30f, 40f, 30f }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(4f);

        tbl.AddCell(new Cell().SetBorder(Border.NO_BORDER)
            .Add(new Paragraph("合　計　金　額").SetFont(font).SetFontSize(10f)));
        tbl.AddCell(new Cell().SetBorder(new SolidBorder(BorderColor, 1f))
            .SetPadding(4f)
            .Add(new Paragraph(FormatAmount(total)).SetFont(font).SetFontSize(11f)
                .SetTextAlignment(TextAlignment.RIGHT)));
        tbl.AddCell(new Cell().SetBorder(Border.NO_BORDER)
            .SetVerticalAlignment(VerticalAlignment.MIDDLE)
            .Add(new Paragraph("（税込）").SetFont(font).SetFontSize(9f)));
        doc.Add(tbl);
    }

    /// ⑤ 太い区切り線（タイトル直下）
    private static void AddThickLine(Document doc)
    {
        var solid = new SolidLine(2f);
        solid.SetColor(DarkBlue);
        doc.Add(new LineSeparator(solid).SetMarginTop(4f).SetMarginBottom(6f));
    }

    /// ⑥ 二重区切り線（明細表の直上）
    private static void AddDoubleLine(Document doc)
    {
        var solid1 = new SolidLine(2f);
        solid1.SetColor(DarkBlue);
        doc.Add(new LineSeparator(solid1).SetMarginTop(4f).SetMarginBottom(2f));
        var solid2 = new SolidLine(0.5f);
        solid2.SetColor(DarkBlue);
        doc.Add(new LineSeparator(solid2).SetMarginTop(0f).SetMarginBottom(4f));
    }

    /// ⑦ 明細テーブル
    private static void AddItemsTable(Document doc, PdfFont font,
        IList<IDictionary<string, object?>> items,
        (string Field, string Label, float Width, string Align)[] cols)
    {
        var widths = cols.Select(c => c.Width).ToArray();
        var tbl = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth();
        var b = new SolidBorder(BorderColor, 0.5f);

        // ヘッダー行
        int colIdx = 0;
        foreach (var (_, label, _, align) in cols)
        {
            bool isFirst = colIdx++ == 0;
            tbl.AddHeaderCell(new Cell()
                .SetBackgroundColor(LabelBlue)
                .SetBorder(b)
                .SetPadding(4f)
                .Add(new Paragraph(isFirst ? "No." + (label == "No." ? "" : "　" + label) : label)
                    .SetFont(font).SetFontSize(8.5f)
                    .SetFontColor(ColorConstants.WHITE)
                    .SetTextAlignment(MapAlign(align))));
        }

        // データ行（最低 8 行表示）
        int rowNum = 1;
        var displayItems = items.ToList();
        while (displayItems.Count < 8) displayItems.Add(new Dictionary<string, object?>());

        foreach (var item in displayItems)
        {
            bool isOdd = rowNum % 2 == 1;
            int fieldIdx = 0;
            foreach (var (field, _, _, align) in cols)
            {
                string text;
                if (fieldIdx == 0)
                {
                    text = item.Count > 0 ? rowNum.ToString() : "";
                }
                else
                {
                    item.TryGetValue(field, out var raw);
                    text = FormatCellValue(field, raw);
                }

                var cell = new Cell()
                    .SetBorder(b)
                    .SetMinHeight(18f)
                    .SetPadding(3f).SetPaddingLeft(5f).SetPaddingRight(5f)
                    .Add(new Paragraph(text).SetFont(font).SetFontSize(8.5f)
                        .SetTextAlignment(MapAlign(align)));
                if (isOdd && item.Count > 0) cell.SetBackgroundColor(OddRow);
                tbl.AddCell(cell);
                fieldIdx++;
            }
            if (item.Count > 0) rowNum++;
        }

        doc.Add(tbl);
    }

    /// ⑧ 税額集計（右側ボックス）
    private static void AddTaxSummary(Document doc, PdfFont font,
        decimal subtotal, decimal tax10, decimal tax8, decimal total,
        bool withholding)
    {
        // 右寄せの集計ボックス（全幅テーブルの右半分を使用）
        var outer = new Table(UnitValue.CreatePercentArray(new[] { 55f, 45f }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginTop(0f);

        outer.AddCell(new Cell().SetBorder(Border.NO_BORDER)); // 左空白

        var summaryTbl = new Table(UnitValue.CreatePercentArray(new[] { 55f, 45f })).UseAllAvailableWidth();
        var b = new SolidBorder(BorderColor, 0.5f);

        void AddRow(string lbl, string val, bool bold = false)
        {
            summaryTbl.AddCell(new Cell().SetBorder(b)
                .SetBackgroundColor(LightGray).SetPadding(3f).SetPaddingLeft(6f)
                .Add(new Paragraph(lbl).SetFont(font).SetFontSize(8.5f)));
            var p = new Paragraph(val).SetFont(font).SetFontSize(8.5f).SetTextAlignment(TextAlignment.RIGHT);
            if (bold) p.SetFontSize(10f);
            summaryTbl.AddCell(new Cell().SetBorder(b).SetPadding(3f).SetPaddingRight(6f).Add(p));
        }

        AddRow("小計",   FormatAmount(subtotal));
        if (tax10 > 0)
            AddRow("消費税（10%）", FormatAmount(tax10));
        if (tax8 > 0)
            AddRow("消費税（8%）",  FormatAmount(tax8));
        if (withholding)
            AddRow("源泉徴収額",    "—");
        AddRow("合　計　金　額", FormatAmount(total), bold: true);

        outer.AddCell(new Cell().SetBorder(Border.NO_BORDER).Add(summaryTbl));
        doc.Add(outer);
    }

    /// 契約書用：情報テーブル（全幅）＋ 署名欄
    private static void AddContractInfoTable(Document doc, PdfFont font,
        (string Label, string Value)[] infoRows,
        string theirName, string theirSignatory,
        string ourSignatory, string signedDate)
    {
        var tbl = new Table(UnitValue.CreatePercentArray(new[] { 28f, 72f })).UseAllAvailableWidth();
        var b = new SolidBorder(BorderColor, 0.5f);
        foreach (var (lbl, val) in infoRows)
        {
            tbl.AddCell(new Cell().SetBorder(b).SetBackgroundColor(LabelBlue)
                .SetPadding(4f).SetPaddingLeft(6f)
                .Add(new Paragraph(lbl).SetFont(font).SetFontSize(8.5f)
                    .SetFontColor(ColorConstants.WHITE)));
            tbl.AddCell(new Cell().SetBorder(b).SetPadding(4f).SetPaddingLeft(6f)
                .Add(new Paragraph(val).SetFont(font).SetFontSize(8.5f)));
        }
        doc.Add(tbl.SetMarginBottom(12f));

        // 署名欄（2 カラム）
        var sigTbl = new Table(UnitValue.CreatePercentArray(new[] { 50f, 50f }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER);
        var sb = new SolidBorder(BorderColor, 0.5f);

        void AddSigBlock(string role, string name, string signatory)
        {
            var block = new Table(UnitValue.CreatePercentArray(new[] { 35f, 65f })).UseAllAvailableWidth();
            block.AddCell(new Cell(2, 1).SetBorder(sb).SetBackgroundColor(LabelBlue).SetPadding(4f)
                .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                .Add(new Paragraph(role).SetFont(font).SetFontSize(8.5f)
                    .SetFontColor(ColorConstants.WHITE).SetTextAlignment(TextAlignment.CENTER)));
            block.AddCell(new Cell().SetBorder(sb).SetPadding(4f)
                .Add(new Paragraph(name).SetFont(font).SetFontSize(10f)));
            block.AddCell(new Cell().SetBorder(sb).SetPadding(4f)
                .Add(new Paragraph(signatory).SetFont(font).SetFontSize(8.5f)));
            sigTbl.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetPaddingRight(8f).Add(block));
        }
        AddSigBlock("甲", theirName,  theirSignatory);
        AddSigBlock("乙", ourSignatory, "代表取締役");
        doc.Add(sigTbl.SetMarginBottom(8f));
    }

    /// ⑨ 備考欄
    private static void AddRemarksBox(Document doc, PdfFont font, string? text)
    {
        var tbl = new Table(UnitValue.CreatePercentArray(new[] { 15f, 85f }))
            .UseAllAvailableWidth().SetMarginTop(8f);
        var b = new SolidBorder(BorderColor, 0.5f);
        tbl.AddCell(new Cell().SetBorder(b).SetBackgroundColor(LabelBlue)
            .SetPadding(6f).SetVerticalAlignment(VerticalAlignment.MIDDLE)
            .Add(new Paragraph("備　考").SetFont(font).SetFontSize(9f)
                .SetFontColor(ColorConstants.WHITE).SetTextAlignment(TextAlignment.CENTER)));
        tbl.AddCell(new Cell().SetBorder(b).SetMinHeight(50f).SetPadding(6f)
            .Add(new Paragraph(text ?? "").SetFont(font).SetFontSize(8.5f)));
        doc.Add(tbl);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 列定義
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static (string Field, string Label, float Width, string Align)[] DeliveryItemCols() =>
    [
        ("_no",      "No.", 6f, "center"),
        ("ItemName", "摘　要", 36f, "left"),
        ("Spec",     "仕様",   14f, "left"),
        ("Unit",     "単位",    7f, "center"),
        ("Quantity", "数量",    8f, "right"),
        ("UnitPrice","単価",   13f, "right"),
        ("Amount",   "金額",   16f, "right"),
    ];

    private static (string Field, string Label, float Width, string Align)[] InvoiceItemCols() =>
    [
        ("_no",      "No.", 6f, "center"),
        ("ItemName", "摘　要", 36f, "left"),
        ("Spec",     "仕様",   14f, "left"),
        ("Unit",     "単位",    7f, "center"),
        ("Quantity", "数量",    8f, "right"),
        ("UnitPrice","単価",   13f, "right"),
        ("Amount",   "金額",   16f, "right"),
    ];

    private static (string Field, string Label, float Width, string Align)[] EstimateItemCols() =>
    [
        ("_no",      "No.", 6f, "center"),
        ("ItemName", "摘　要", 36f, "left"),
        ("Spec",     "仕様",   14f, "left"),
        ("Unit",     "単位",    7f, "center"),
        ("Quantity", "数量",    8f, "right"),
        ("UnitPrice","単価",   13f, "right"),
        ("Amount",   "金額",   16f, "right"),
    ];

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ユーティリティ
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static PdfFont LoadFont(string? projectDir)
    {
        foreach (var path in CjkFontPaths)
        {
            if (File.Exists(path))
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

    private static string FormatAmount(decimal v)
        => v == 0 ? "¥0" : $"¥{v:N0}";

    private static string FormatCellValue(string field, object? raw)
    {
        if (raw == null) return "";
        // 金額系フィールドは ¥ 書式
        if (field is "Amount" or "UnitPrice" or "ContractAmount")
        {
            if (decimal.TryParse(raw.ToString(), out var d)) return FormatAmount(d);
        }
        // 数量は小数不要なら整数で
        if (field is "Quantity")
        {
            if (decimal.TryParse(raw.ToString(), out var q))
                return q == Math.Floor(q) ? ((int)q).ToString() : q.ToString("N1");
        }
        return raw.ToString() ?? "";
    }

    private static string GetStr(IDictionary<string, object?>? d, string key)
        => d != null && d.TryGetValue(key, out var v) && v != null ? v.ToString()! : "";

    private static decimal GetDecimal(IDictionary<string, object?> d, string key)
    {
        if (d.TryGetValue(key, out var v) && v != null &&
            decimal.TryParse(v.ToString(), out var r))
            return r;
        return 0m;
    }

    private static bool GetBool(IDictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v != null && v.ToString() is "1" or "true" or "True";

    private static int GetInt(IDictionary<string, object?> d, string key)
    {
        if (d.TryGetValue(key, out var v) && v != null && int.TryParse(v.ToString(), out var r))
            return r;
        return 0;
    }
}
