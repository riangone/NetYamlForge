# 核心フレームワーク PDF テンプレート

このディレクトリには、NetYamlForge フレームワーク全体で共有する PDF 帳票テンプレートが配置されています。

## テンプレート一覧

### invoice.yaml - 請求書
日本向けの請求書（インボイス制度対応）テンプレートです。

**主な機能:**
- 税率別集計（10%・8%）
- 適格請求書発行事業者登録番号対応
- 振込先情報記載

**データソース:**
- `customer`: 顧客情報
- `items`: 明細データ

### estimate.yaml - 見積書
日本向け見積書テンプレートです。

**主な機能:**
- 有効期限記載
- 税率別集計
- 備考欄

### delivery.yaml - 納品書
日本向け納品書テンプレートです。

**主な機能:**
- 検収期間記載
- 受領確認日欄
- 税率別集計

### contract.yaml - 契約書
業務委託契約書テンプレートです。

**主な機能:**
- 契約期間・更新条項
- 管轄裁判所・準拠法
- 電子契約・印紙税対応

## 使用方法

### コントローラーから呼び出す例

```csharp
public class PdfController : Controller
{
    private readonly IDocumentPdfService _pdfService;

    public PdfController(IDocumentPdfService pdfService)
    {
        _pdfService = pdfService;
    }

    public async Task<IActionResult> GenerateInvoice(int id)
    {
        // 1. 核心フレームワークのテンプレートを読み込み
        var template = _pdfService.LoadGlobalTemplate("invoice");
        if (template == null)
            return NotFound("テンプレートが見つかりません。");

        // 2. データを取得
        var invoice = await _db.Invoice.FindAsync(id);
        var customer = await _db.Customer.FindAsync(invoice.CustomerId);
        var items = await _db.InvoiceItem.Where(x => x.InvoiceId == id).ToListAsync();

        // 3. ヘッダーデータを準備
        var header = new Dictionary<string, object?>
        {
            ["InvoiceNo"] = invoice.InvoiceNo,
            ["IssueDate"] = invoice.IssueDate,
            ["DueDate"] = invoice.DueDate,
            ["Total"] = invoice.Total.ToString("N0"),
            // ... 他のフィールド
        };

        // 4. 明細データを準備
        var dataSources = new Dictionary<string, IList<IDictionary<string, object?>>>
        {
            ["customer"] = new List<IDictionary<string, object?>>
            {
                new Dictionary<string, object?>
                {
                    ["Id"] = customer.Id,
                    ["Name"] = customer.Name,
                    ["Address"] = customer.Address,
                    // ...
                }
            },
            ["items"] = items.Select(x => new Dictionary<string, object?>
            {
                ["LineNo"] = x.LineNo,
                ["ItemName"] = x.ItemName,
                ["Amount"] = x.Amount,
                // ...
            }).ToList()
        };

        // 5. PDF 生成
        var pdfBytes = _pdfService.Generate(template, header, dataSources);
        return File(pdfBytes, "application/pdf", $"請求書_{invoice.InvoiceNo}.pdf");
    }
}
```

### プロジェクト固有テンプレートとの優先順位

1. `LoadGlobalTemplate(templateName)`: 核心フレームワークのテンプレートを読み込み
2. `LoadTemplate(projectDir, templateName)`: プロジェクト固有のテンプレートを読み込み

プロジェクト固有のテンプレートを優先したい場合は、以下のように実装します:

```csharp
var template = _pdfService.LoadTemplate(projectDir, "invoice")
              ?? _pdfService.LoadGlobalTemplate("invoice");
```

## テンプレートの構造

各テンプレートは YAML で定義され、以下のセクションから構成されます:

```yaml
name: invoice
filenameTemplate: "請求書_{date:yyyyMMdd}.pdf"
pageSize: A4
orientation: portrait
margins: [36, 42, 36, 42]

theme:
  primaryColor: "1c3658"
  labelColor: "78b4cc"
  # ...

dataSources:
  customer:
    query: "SELECT * FROM Customer WHERE Id = @CustomerId"
  items:
    query: "SELECT * FROM InvoiceItem WHERE InvoiceId = @Id"

sections:
  - type: paragraph
    text: "請求書"
    # ...
```

## 使用可能なプリミティブ

核心フレームワークは 5 つの汎用プリミティブを提供します:

1. **line**: 区切り線
2. **paragraph**: テキスト段落
3. **row**: 複数カラムレイアウト
4. **labelTable**: ラベル付き 2 列テーブル
5. **dataTable**: データテーブル

これらのプリミティブを組み合わせて、複雑な帳票レイアウトを定義できます。

## カスタマイズ

プロジェクト固有の要件に合わせて、以下のディレクトリに同名の YAML ファイルを配置することで、核心フレームワークのテンプレートをオーバーライドできます:

```
NetYamlForge/projects/{project-name}/pdf-templates/invoice.yaml
```

## PDF 生成エンジン

| 実装クラス | ライセンス | 特徴 |
|---|---|---|
| `DocumentPdfService` | MIT (PdfSharpCore) | **既定**。クロスプラットフォーム対応。Google Fonts (Noto Sans JP) で日本語表示に対応 |

`IDocumentPdfService` を注入すると `DocumentPdfService` が使われます。

### サンプルデータでの動作確認

`biz-docs` プロジェクトには全テンプレートに対応するエンティティとシードデータが含まれています。
アプリを起動して `/biz-docs` にアクセスし、各エンティティの「DocumentPdf」アクションで PDF を確認できます。
