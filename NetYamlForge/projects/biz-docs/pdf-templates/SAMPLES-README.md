# PDF テンプレートサンプルデータ

このディレクトリには、NetYamlForge 核心フレームワークの PDF テンプレートを使用するためのサンプルデータが含まれています。

## テンプレート一覧とサンプルデータ

### 1. invoice.yaml - 請求書

**ファイル**: `Schemas/pdf-templates/invoice.yaml`

**サンプルデータ**:
```bash
# biz-docs プロジェクトのデータベースにサンプルデータを読み込む
sqlite3 projects/biz-docs/database/biz-docs.db < projects/biz-docs/database/pdf_template_samples.sql
```

**サンプルレコード**:
- `INV-PDF-001`: 山田商事株式会社向け Web システム開発費用
- `INV-PDF-002`: 鈴木工業株式会社向けサーバー保守サービス
- `INV-PDF-003`: 佐藤物産株式会社向けネットワーク機器導入費用

**テスト方法**:
```
/biz-docs/JpInvoice/Index にアクセスし、請求書アイコンをクリック
```

---

### 2. estimate.yaml - 御見積書

**ファイル**: `Schemas/pdf-templates/estimate.yaml`

**サンプルデータ**:
- `EST-PDF-001`: モバイルアプリ開発見積（¥1,650,000）
- `EST-PDF-002`: クラウド移行サービス見積（¥880,000）
- `EST-PDF-003`: 社内研修プログラム見積（¥385,000）

**テスト方法**:
```
/biz-docs/JpEstimate/Index にアクセスし、見積書アイコンをクリック
```

---

### 3. delivery.yaml - 納品書

**ファイル**: `Schemas/pdf-templates/delivery.yaml`

**サンプルデータ**:
- `DLV-PDF-001`: Web システム 成果物一式（第 2 フェーズ）
- `DLV-PDF-002`: サーバー監視ツール導入セット
- `DLV-PDF-003`: ネットワーク機器一式

**テスト方法**:
```
/biz-docs/JpDelivery/Index にアクセスし、納品書アイコンをクリック
```

---

### 4. contract.yaml - 契約書

**ファイル**: `Schemas/pdf-templates/contract.yaml`

**サンプルデータ**:
- `CTR-PDF-001`: Web システム開発請負契約（山田商事）
- `CTR-PDF-002`: サーバー保守サービス委託契約（鈴木工業）
- `CTR-PDF-003`: 秘密保持契約（NDA）（佐藤物産）

**テスト方法**:
```
/biz-docs/JpContract/Index にアクセスし、契約書アイコンをクリック
```

---

## その他の PDF テンプレート

### invoices/ ディレクトリ

- `invoice-standard.yaml`: 標準請求書（黒色テーマ）
- `invoice-blue.yaml`: ブルーテーマの請求書
- `invoice-bank.yaml`: 銀行振込用紙付き請求書

### orders/ ディレクトリ

- `purchase-order4.yaml`: 購入依頼書（発注書）

### receipts/ ディレクトリ

- `receipt-02.yaml`: 領収書

### others/ ディレクトリ

- `delivery-slip.yaml`: 納品伝票
- `fax-cover.yaml`: FAX 送付状
- `minutes-03.yaml`: 会議議事録
- `resume-jis.yaml`: 職務経歴書（JIS 規格）

---

## サンプルデータの内容

### Customer テーブル

| Code | Name | 電話番号 | 国 |
|------|------|----------|-----|
| PDF-C001 | 山田商事株式会社 | +81-3-1234-5678 | JP |
| PDF-C002 | 鈴木工業株式会社 | +81-6-9876-5432 | JP |
| PDF-C003 | 佐藤物産株式会社 | +81-52-1111-2222 | JP |

### JpInvoice テーブル

| InvoiceNo | Customer | Total | Status |
|-----------|----------|-------|--------|
| INV-PDF-001 | 山田商事 | ¥550,000 | issued |
| INV-PDF-002 | 鈴木工業 | ¥165,000 | issued |
| INV-PDF-003 | 佐藤物産 | ¥880,000 | draft |

### JpEstimate テーブル

| EstimateNo | Customer | Total | Status |
|------------|----------|-------|--------|
| EST-PDF-001 | 山田商事 | ¥1,650,000 | sent |
| EST-PDF-002 | 鈴木工業 | ¥880,000 | draft |
| EST-PDF-003 | 佐藤物産 | ¥385,000 | sent |

### JpDelivery テーブル

| DeliveryNo | Customer | Total | Status |
|------------|----------|-------|--------|
| DLV-PDF-001 | 山田商事 | ¥825,000 | delivered |
| DLV-PDF-002 | 鈴木工業 | ¥198,000 | confirmed |
| DLV-PDF-003 | 佐藤物産 | ¥880,000 | delivered |

### JpContract テーブル

| ContractNo | Customer | Type | Status |
|------------|----------|------|--------|
| CTR-PDF-001 | 山田商事 | 請負契約 | active |
| CTR-PDF-002 | 鈴木工業 | 業務委託契約 | active |
| CTR-PDF-003 | 佐藤物産 | 秘密保持契約 | active |

---

## 使い方

### 1. データベースにサンプルデータを読み込む

```bash
cd /home/ubuntu/ws/NetYamlForge

# SQLite の場合
sqlite3 NetYamlForge/projects/biz-docs/database/biz-docs.db < NetYamlForge/projects/biz-docs/database/pdf_template_samples.sql
```

### 2. アプリケーションを起動

```bash
dotnet run --project NetYamlForge
```

### 3. PDF テンプレートをテスト

ブラウザで以下の URL にアクセス：

- 請求書：`http://localhost:5000/biz-docs/JpInvoice/Index`
- 見積書：`http://localhost:5000/biz-docs/JpEstimate/Index`
- 納品書：`http://localhost:5000/biz-docs/JpDelivery/Index`
- 契約書：`http://localhost:5000/biz-docs/JpContract/Index`

各リストの行アクションから PDF エクスポートを選択し、サンプル PDF を生成できます。

---

## PDF 生成のカスタマイズ

### ヘッダー情報の準備

コントローラーで以下のデータを準備します：

```csharp
var header = new Dictionary<string, object?>
{
    ["InvoiceNo"] = invoice.InvoiceNo,
    ["IssueDate"] = invoice.IssueDate,
    ["DueDate"] = invoice.DueDate,
    ["Title"] = invoice.Title,
    ["RegistrationNo"] = invoice.RegistrationNo,
    ["Total"] = invoice.Total.ToString("N0"),
    ["Subtotal"] = invoice.Subtotal.ToString("N0"),
    ["TaxAmount10"] = invoice.TaxAmount10.ToString("N0"),
    ["TaxAmount8"] = invoice.TaxAmount8.ToString("N0"),
    ["BankName"] = invoice.BankName,
    ["BranchName"] = invoice.BranchName,
    ["AccountType"] = invoice.AccountType,
    ["AccountNo"] = invoice.AccountNo,
    ["AccountHolder"] = invoice.AccountHolder,
    ["PreparedBy"] = invoice.PreparedBy,
    ["CompanyStamp"] = invoice.CompanyStamp,
    ["Remarks"] = invoice.Remarks
};
```

### データソースの準備

```csharp
var dataSources = new Dictionary<string, IList<IDictionary<string, object?>>>
{
    ["customer"] = new List<IDictionary<string, object?>>
    {
        new Dictionary<string, object?>
        {
            ["Id"] = customer.Id,
            ["Name"] = customer.Name,
            ["Address"] = customer.Address,
            ["Phone"] = customer.Phone,
            ["Email"] = customer.Email
        }
    },
    ["items"] = items.Select(x => new Dictionary<string, object?>
    {
        ["LineNo"] = x.LineNo,
        ["ItemCode"] = x.ItemCode,
        ["ItemName"] = x.ItemName,
        ["Spec"] = x.Spec,
        ["Unit"] = x.Unit,
        ["Quantity"] = x.Quantity,
        ["UnitPrice"] = x.UnitPrice,
        ["Amount"] = x.Amount,
        ["TaxRate"] = x.TaxRate
    }).ToList()
};
```

### PDF 生成

```csharp
var template = _pdfService.LoadGlobalTemplate("invoice");
var pdfBytes = _pdfService.Generate(template, header, dataSources);
return File(pdfBytes, "application/pdf", $"請求書_{invoice.InvoiceNo}.pdf");
```

---

## 注意事項

1. **フォント**: PDF 生成には CJK フォントが必要です。`DocumentPdfService.cs` の `CjkFontPaths` で定義されたパスのいずれかにフォントをインストールしてください。

2. **iText ライセンス**: iText 7 は AGPL v3 または商用ライセンスで提供されます。詳細は https://itextpdf.com/ をご覧ください。

3. **サンプルデータの削除**: サンプルデータを削除するには、以下の SQL を実行します：
   ```sql
   DELETE FROM JpInvoiceItem WHERE InvoiceId IN (SELECT Id FROM JpInvoice WHERE InvoiceNo LIKE 'INV-PDF-%');
   DELETE FROM JpInvoice WHERE InvoiceNo LIKE 'INV-PDF-%';
   DELETE FROM JpEstimateItem WHERE EstimateId IN (SELECT Id FROM JpEstimate WHERE EstimateNo LIKE 'EST-PDF-%');
   DELETE FROM JpEstimate WHERE EstimateNo LIKE 'EST-PDF-%';
   DELETE FROM JpDeliveryItem WHERE DeliveryId IN (SELECT Id FROM JpDelivery WHERE DeliveryNo LIKE 'DLV-PDF-%');
   DELETE FROM JpDelivery WHERE DeliveryNo LIKE 'DLV-PDF-%';
   DELETE FROM JpContract WHERE ContractNo LIKE 'CTR-PDF-%';
   DELETE FROM Customer WHERE Code LIKE 'PDF-%';
   ```

---

## 関連ドキュメント

- [Schemas/pdf-templates/README.md](../../../Schemas/pdf-templates/README.md) - 核心フレームワーク PDF テンプレート
- [docs/fix-20260325-startup-issue.md](../../../docs/fix-20260325-startup-issue.md) - 起動障害修復レポート
