// ファイル概要: PDFsharp 実装を使い、核心フレームワークの全 PDF テンプレートで
// サンプル PDF を生成するデモランナー。
// CLI コマンド: dotnet run -- --generate-pdf-samples [--output-dir=<path>]

namespace NetYamlForge.Services;

/// <summary>
/// 核心フレームワーク (Schemas/pdf-templates/) の全テンプレートに対し
/// PDFsharp でサンプル PDF を生成するランナー。
/// </summary>
public class PdfTemplateSampleRunner
{
    private readonly IDocumentPdfService _pdf;

    public PdfTemplateSampleRunner(IDocumentPdfService pdf)
    {
        _pdf = pdf;
    }

    /// <summary>全テンプレートのサンプル PDF を <paramref name="outputDir"/> に書き出します。</summary>
    /// <returns>生成ファイル数</returns>
    public int Run(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        int count = 0;

        count += Generate("invoice",                 BuildInvoiceData(),       outputDir, "請求書.pdf");
        count += Generate("estimate",                BuildEstimateData(),      outputDir, "見積書.pdf");
        count += Generate("delivery",                BuildDeliveryData(),      outputDir, "納品書.pdf");
        count += Generate("contract",                BuildContractData(),      outputDir, "契約書.pdf");
        count += Generate("invoices/invoice-standard", BuildInvoiceData(),     outputDir, "請求書_standard.pdf");
        count += Generate("invoices/invoice-blue",     BuildInvoiceBankData(), outputDir, "請求書_blue.pdf");
        count += Generate("invoices/invoice-bank",     BuildInvoiceBankData(), outputDir, "請求書_bank.pdf");
        count += Generate("orders/purchase-order4",    BuildPurchaseOrderData(), outputDir, "発注書.pdf");
        count += Generate("receipts/receipt-02",       BuildReceiptData(),     outputDir, "領収書.pdf");
        count += Generate("others/delivery-slip",      BuildDeliverySlipData(), outputDir, "納品伝票.pdf");
        count += Generate("others/fax-cover",          BuildFaxCoverData(),    outputDir, "FAX送付状.pdf");
        count += Generate("others/minutes-03",         BuildMinutesData(),     outputDir, "議事録.pdf");
        count += Generate("others/resume-jis",         BuildResumeData(),      outputDir, "履歴書.pdf");

        return count;
    }

    private int Generate(
        string templateName,
        (IDictionary<string, object?> Header,
         IDictionary<string, IList<IDictionary<string, object?>>> DataSources) data,
        string outputDir,
        string fileName)
    {
        var template = _pdf.LoadGlobalTemplate(templateName);
        if (template == null)
        {
            Console.Error.WriteLine($"  [SKIP] テンプレートが見つかりません: {templateName}");
            return 0;
        }

        try
        {
            var bytes = _pdf.Generate(template, data.Header, data.DataSources);
            var path  = Path.Combine(outputDir, fileName);
            File.WriteAllBytes(path, bytes);
            Console.WriteLine($"  [OK] {path}  ({bytes.Length / 1024} KB)");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [ERR] {templateName}: {ex.Message}");
            return 0;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 請求書
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildInvoiceData()
    {
        var h = H(
            ("InvoiceNo",       "INV-DEMO-001"),
            ("IssueDate",       "2026-03-25"),
            ("DueDate",         "2026-04-30"),
            ("Title",           "Webシステム開発費用 第1フェーズ"),
            ("RegistrationNo",  "T1234567890123"),
            ("Total",           "550000"),
            ("Subtotal",        "500000"),
            ("TaxAmount10",     "50000"),
            ("TaxAmount8",      "0"),
            ("BankName",        "みずほ銀行"),
            ("BranchName",      "渋谷支店"),
            ("AccountType",     "普通"),
            ("AccountNo",       "1234567"),
            ("AccountHolder",   "カ）デモシステムズ"),
            ("TransferFeeNote", "※振込手数料はご負担ください"),
            ("PreparedBy",      "田中 太郎"),
            ("CompanyStamp",    "株式会社デモシステムズ"),
            ("Remarks",         "")
        );

        var customer = Rows(
            R(("Id", "1"), ("Name", "山田商事株式会社"),
              ("Address", "東京都渋谷区〇〇1-2-3"),
              ("Phone", "03-1234-5678"), ("Email", "info@yamada.example.jp"))
        );
        var items = Rows(
            R(("LineNo","1"), ("ItemCode","SVC-001"), ("ItemName","要件定義・基本設計"),
              ("Spec","一式"), ("Unit","式"), ("Quantity","1"),
              ("UnitPrice","150000"), ("Amount","150000"), ("TaxRate","10")),
            R(("LineNo","2"), ("ItemCode","SVC-002"), ("ItemName","システム開発（フロントエンド）"),
              ("Spec","一式"), ("Unit","式"), ("Quantity","1"),
              ("UnitPrice","200000"), ("Amount","200000"), ("TaxRate","10")),
            R(("LineNo","3"), ("ItemCode","SVC-003"), ("ItemName","システム開発（バックエンド）"),
              ("Spec","一式"), ("Unit","式"), ("Quantity","1"),
              ("UnitPrice","150000"), ("Amount","150000"), ("TaxRate","10"))
        );

        return (h, DS(("customer", customer), ("items", items)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 銀行振込請求書 (invoice-bank / invoice-blue 共用)
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildInvoiceBankData()
        => BuildInvoiceData();   // フィールドは同じ

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 見積書
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildEstimateData()
    {
        var h = H(
            ("EstimateNo",     "EST-DEMO-001"),
            ("IssueDate",      "2026-03-25"),
            ("ValidUntil",     "2026-04-30"),
            ("Title",          "モバイルアプリ開発 御見積"),
            ("Total",          "1650000"),
            ("Subtotal",       "1500000"),
            ("TaxAmount10",    "150000"),
            ("TaxAmount8",     "0"),
            ("PreparedBy",     "田中 太郎"),
            ("CompanyStamp",   "株式会社デモシステムズ"),
            ("Remarks",        "本見積の有効期限は発行日から30日間です。")
        );

        var customer = Rows(
            R(("Name", "鈴木工業株式会社"),
              ("Address", "大阪府大阪市北区〇〇2-3-4"),
              ("Phone", "06-9876-5432"))
        );
        var items = Rows(
            R(("LineNo","1"), ("ItemCode","APP-001"), ("ItemName","iOS / Androidアプリ開発"),
              ("Spec","一式"), ("Unit","式"), ("Quantity","1"),
              ("UnitPrice","800000"), ("Amount","800000"), ("TaxRate","10")),
            R(("LineNo","2"), ("ItemCode","APP-002"), ("ItemName","サーバーAPI開発"),
              ("Spec","一式"), ("Unit","式"), ("Quantity","1"),
              ("UnitPrice","500000"), ("Amount","500000"), ("TaxRate","10")),
            R(("LineNo","3"), ("ItemCode","APP-003"), ("ItemName","テスト・品質保証"),
              ("Spec","一式"), ("Unit","式"), ("Quantity","1"),
              ("UnitPrice","200000"), ("Amount","200000"), ("TaxRate","10"))
        );

        return (h, DS(("customer", customer), ("items", items)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 納品書
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildDeliveryData()
    {
        var h = H(
            ("DeliveryNo",     "DLV-DEMO-001"),
            ("IssueDate",      "2026-03-25"),
            ("DeliveryDate",   "2026-03-25"),
            ("Title",          "Webシステム 成果物一式（第2フェーズ）"),
            ("Total",          "825000"),
            ("Subtotal",       "750000"),
            ("TaxAmount10",    "75000"),
            ("TaxAmount8",     "0"),
            ("PreparedBy",     "田中 太郎"),
            ("CompanyStamp",   "株式会社デモシステムズ"),
            ("Remarks",        "ご検収の程よろしくお願いいたします。")
        );

        var customer = Rows(
            R(("Name", "山田商事株式会社"),
              ("Address", "東京都渋谷区〇〇1-2-3"))
        );
        var items = Rows(
            R(("LineNo","1"), ("ItemName","設計書（基本・詳細）"),
              ("Unit","式"), ("Quantity","1"), ("Amount","200000")),
            R(("LineNo","2"), ("ItemName","ソースコード一式"),
              ("Unit","式"), ("Quantity","1"), ("Amount","450000")),
            R(("LineNo","3"), ("ItemName","テスト報告書"),
              ("Unit","式"), ("Quantity","1"), ("Amount","100000"))
        );

        return (h, DS(("customer", customer), ("items", items)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 契約書
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildContractData()
    {
        var h = H(
            ("ContractNo",      "CTR-DEMO-001"),
            ("ContractDate",    "2026-03-25"),
            ("StartDate",       "2026-04-01"),
            ("EndDate",         "2027-03-31"),
            ("Title",           "Webシステム開発請負契約"),
            ("ContractType",    "請負契約"),
            ("Amount",          "5500000"),
            ("ClientName",      "山田商事株式会社"),
            ("ClientAddress",   "東京都渋谷区〇〇1-2-3"),
            ("ClientRepName",   "代表取締役 山田 一郎"),
            ("VendorName",      "株式会社デモシステムズ"),
            ("VendorAddress",   "東京都新宿区〇〇4-5-6"),
            ("VendorRepName",   "代表取締役 田中 太郎"),
            ("Jurisdiction",    "東京地方裁判所"),
            ("Remarks",         "本契約に定めのない事項については、甲乙協議の上決定する。")
        );

        return (h, DS<string, object?>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 発注書
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildPurchaseOrderData()
    {
        var h = H(
            ("OrderNo",        "PO-DEMO-001"),
            ("IssueDate",      "2026-03-25"),
            ("DeliveryDate",   "2026-04-30"),
            ("Title",          "サーバー機材購入"),
            ("Total",          "330000"),
            ("Subtotal",       "300000"),
            ("TaxAmount10",    "30000"),
            ("PreparedBy",     "購買部 佐藤 花子"),
            ("CompanyStamp",   "株式会社デモシステムズ"),
            ("Remarks",        "納品の際は事前にご連絡ください。")
        );

        var vendor = Rows(
            R(("Name", "株式会社ITサプライ"), ("Address", "東京都千代田区〇〇5-6-7"))
        );
        var items = Rows(
            R(("LineNo","1"), ("ItemName","高性能サーバー（ラックマウント型）"),
              ("Spec","4コア/32GB/1TB SSD"), ("Unit","台"), ("Quantity","2"),
              ("UnitPrice","120000"), ("Amount","240000")),
            R(("LineNo","2"), ("ItemName","UPS（無停電電源装置）"),
              ("Spec","750VA"), ("Unit","台"), ("Quantity","1"),
              ("UnitPrice","60000"), ("Amount","60000"))
        );

        return (h, DS(("vendor", vendor), ("items", items)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 領収書
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildReceiptData()
    {
        var h = H(
            ("ReceiptNo",      "REC-DEMO-001"),
            ("IssueDate",      "2026-03-25"),
            ("PayerName",      "山田商事株式会社"),
            ("Amount",         "550000"),
            ("TaxAmount10",    "50000"),
            ("Purpose",        "Webシステム開発費用（第1フェーズ）"),
            ("PaymentMethod",  "銀行振込"),
            ("IssuedBy",       "株式会社デモシステムズ"),
            ("StampNote",      "収入印紙")
        );

        var customer = Rows(
            R(("Name", "山田商事株式会社"), ("Address", "東京都渋谷区〇〇1-2-3"))
        );

        return (h, DS(("customer", customer)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 納品伝票
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildDeliverySlipData()
    {
        var h = H(
            ("SlipNo",         "SLP-DEMO-001"),
            ("IssueDate",      "2026-03-25"),
            ("DeliveryDate",   "2026-03-25"),
            ("RecipientName",  "山田商事株式会社 資材部"),
            ("SenderName",     "株式会社デモシステムズ"),
            ("Remarks",        "取り扱いにご注意ください。")
        );

        var items = Rows(
            R(("LineNo","1"), ("ItemName","設計書一式"), ("Unit","冊"), ("Quantity","2")),
            R(("LineNo","2"), ("ItemName","USBメモリ（ソースコード）"), ("Unit","本"), ("Quantity","1")),
            R(("LineNo","3"), ("ItemName","テスト報告書"), ("Unit","冊"), ("Quantity","1"))
        );

        return (h, DS(("items", items)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― FAX 送付状
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildFaxCoverData()
    {
        var h = H(
            ("SenderName",     "株式会社デモシステムズ　営業部"),
            ("SenderPhone",    "03-0000-1111"),
            ("SenderFax",      "03-0000-1112"),
            ("SentDate",       "2026-03-25 14:30"),
            ("TotalPages",     "3"),
            ("Subject",        "Webシステム開発 御見積書のご送付"),
            ("Message",        "平素より大変お世話になっております。\n標記の件につきまして、御見積書をお送りいたします。\nご確認いただけますようお願いいたします。"),
            ("PreparedBy",     "田中 太郎")
        );

        var recipient = Rows(
            R(("Name", "山田商事株式会社"), ("Department", "情報システム部"),
              ("FaxNumber", "03-1234-5679"), ("Phone", "03-1234-5678"))
        );

        return (h, DS(("recipient", recipient)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 議事録
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildMinutesData()
    {
        var h = H(
            ("Title",          "Webシステム開発 第3回進捗会議"),
            ("MeetingDate",    "2026-03-25"),
            ("StartTime",      "14:00"),
            ("EndTime",        "15:30"),
            ("Location",       "第1会議室（オンライン併用）"),
            ("Facilitator",    "田中 太郎"),
            ("Secretary",      "鈴木 花子"),
            ("NextMeeting",    "2026-04-08 14:00〜"),
            ("Summary",        "フロントエンド実装が70%完了。バックエンドAPIのテストを来週開始予定。")
        );

        var attendees = Rows(
            R(("Name","田中 太郎"),  ("Role","PM"),         ("Organization","デモシステムズ")),
            R(("Name","鈴木 花子"),  ("Role","エンジニア"),  ("Organization","デモシステムズ")),
            R(("Name","山田 一郎"),  ("Role","発注者"),      ("Organization","山田商事")),
            R(("Name","佐藤 次郎"),  ("Role","IT担当"),      ("Organization","山田商事"))
        );
        var decisions = Rows(
            R(("ItemNo","1"), ("Content","フロントエンドの画面デザインを3/31までに最終確定する")),
            R(("ItemNo","2"), ("Content","バックエンドAPIのテスト仕様書を4/5までに提出する")),
            R(("ItemNo","3"), ("Content","次回会議は4/8（水）14時〜 同会議室で実施"))
        );
        var actionItems = Rows(
            R(("Priority","高"), ("Task","画面デザイン最終確定"),
              ("Owner","鈴木 花子"), ("DueDate","2026-03-31")),
            R(("Priority","高"), ("Task","APIテスト仕様書作成"),
              ("Owner","田中 太郎"), ("DueDate","2026-04-05")),
            R(("Priority","中"), ("Task","次回会議室の予約"),
              ("Owner","鈴木 花子"), ("DueDate","2026-03-27"))
        );

        return (h, DS(
            ("attendees",   attendees),
            ("decisions",   decisions),
            ("actionItems", actionItems)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // サンプルデータ ― 履歴書 (JIS)
    // ─────────────────────────────────────────────────────────────────────────

    private static (IDictionary<string, object?>, IDictionary<string, IList<IDictionary<string, object?>>>) BuildResumeData()
    {
        var h = H(
            ("FullName",        "田中 太郎"),
            ("FullNameKana",    "タナカ タロウ"),
            ("BirthDate",       "1990年4月1日"),
            ("Age",             "35"),
            ("Gender",          "男"),
            ("Address",         "東京都新宿区〇〇1-2-3-456"),
            ("Phone",           "090-0000-1234"),
            ("Email",           "taro.tanaka@example.jp"),
            ("Nationality",     "日本"),
            ("PR",              "システム開発に10年以上従事し、フルスタックエンジニアとして多数のプロジェクトをリード。チームマネジメント経験もあり。"),
            ("Availability",    "即日可能")
        );

        var education = Rows(
            R(("StartDate","2006-04"), ("EndDate","2009-03"), ("SchoolName","○○県立△△高等学校"),  ("Department","普通科"),     ("Status","卒業")),
            R(("StartDate","2009-04"), ("EndDate","2013-03"), ("SchoolName","××大学"),            ("Department","情報工学科"),  ("Status","卒業"))
        );
        var workHistory = Rows(
            R(("StartDate","2013-04"), ("EndDate","2018-03"), ("CompanyName","株式会社ABCシステム"),
              ("Department","システム開発部"), ("Title","エンジニア"),
              ("Description","Webアプリケーション開発（Java/Spring Boot）")),
            R(("StartDate","2018-04"), ("EndDate",""),        ("CompanyName","株式会社デモシステムズ"),
              ("Department","開発本部"), ("Title","シニアエンジニア / PM"),
              ("Description","大規模Webシステムの設計・開発・チームマネジメント（.NET/React）"))
        );
        var qualifications = Rows(
            R(("ObtainedDate","2014-06"), ("Name","基本情報技術者試験"),       ("Issuer","IPA")),
            R(("ObtainedDate","2017-11"), ("Name","応用情報技術者試験"),       ("Issuer","IPA")),
            R(("ObtainedDate","2020-03"), ("Name","AWS認定ソリューションアーキテクト"), ("Issuer","Amazon Web Services"))
        );

        return (h, DS(
            ("education",      education),
            ("workHistory",    workHistory),
            ("qualifications", qualifications)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ヘルパー: 辞書ビルダー
    // ─────────────────────────────────────────────────────────────────────────

    private static IDictionary<string, object?> H(
        params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    private static IDictionary<string, object?> R(
        params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    private static IList<IDictionary<string, object?>> Rows(
        params IDictionary<string, object?>[] rows)
        => rows;

    // DS() ― データソース辞書のビルダー
    private static IDictionary<string, IList<IDictionary<string, object?>>> DS<TK, TV>()
        => new Dictionary<string, IList<IDictionary<string, object?>>>();

    private static IDictionary<string, IList<IDictionary<string, object?>>> DS(
        params (string Key, IList<IDictionary<string, object?>> Rows)[] sources)
    {
        var d = new Dictionary<string, IList<IDictionary<string, object?>>>();
        foreach (var (k, v) in sources) d[k] = v;
        return d;
    }
}
