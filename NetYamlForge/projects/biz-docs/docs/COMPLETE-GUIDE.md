# biz-docs 完全ガイド

## 概要

biz-docs プロジェクトの完全なデータベース定義、テストデータ、および Entity YAML 設定のガイドです。

## データベース

### 表定義

18 個の業務表が定義されています：

| # | 表名 | Entity | 説明 | PDF テンプレート |
|---|------|--------|------|-----------------|
| 1 | Customer | customer | 取引先マスタ | - |
| 2 | Quotation | quotation | 報価単（貿易用） | orders/purchase-order4 |
| 3 | Invoice | invoice | 請款書（貿易用） | invoices/invoice-standard |
| 4 | CustomsDeclaration | customs_declaration | 報関単 | trade/customs-declaration |
| 5 | PdfTemplateCategory | pdf_template_category | PDF テンプレートカテゴリ | - |
| 6 | PdfTemplate | pdf_template | PDF テンプレート | - |
| 7 | JpEstimate | jp_estimate | 見積書（国内用） | domestic/estimate-standard |
| 8 | JpInvoice | jp_invoice | 請求書（国内用） | domestic/invoice-standard |
| 9 | JpDelivery | jp_delivery | 納品書 | domestic/delivery-standard |
| 10 | JpContract | jp_contract | 契約書台帳 | domestic/contract-standard |
| 11 | JpReceipt | jp_receipt | 領収書 | - |
| 12 | JpDeliverySlip | jp_delivery_slip | 送付状 | - |
| 13 | JpInvoiceStandard | jp_invoice_standard | 請求書（標準） | - |
| 14 | JpInvoiceBlue | jp_invoice_blue | 請求書（青） | - |
| 15 | JpInvoiceBank | jp_invoice_bank | 請求書（銀行用） | - |
| 16 | JpResume | jp_resume | 履歴書 | - |
| 17 | FaxCover | fax_cover | ファックス表紙 | - |
| 18 | Meeting | meeting | 会議録 | - |

### 初期化スクリプト

**ファイル**: `NetYamlForge/projects/biz-docs/database/init_and_seed.sql`

このスクリプトには以下が含まれています：
- 18 表の CREATE TABLE 文（インデックス・外部キー制約付き）
- 83 件のテストデータ INSERT 文
- PDF テンプレートカテゴリとテンプレートの初期データ

**使用方法**:
```bash
cd NetYamlForge/projects/biz-docs/database
sqlite3 biz-docs.db < init_and_seed.sql
```

### テストデータ

各表に以下のテストデータが登録されています：

- **Customer**: 8 件（中国 4 社、日本 2 社、シンガポール 1 社、台湾 1 社）
- **Quotation**: 5 件（全ステータスを網羅：draft, sent, accepted, rejected, expired）
- **Invoice**: 5 件（全ステータスを網羅：draft, issued, paid, overdue, cancelled）
- **CustomsDeclaration**: 5 件（全ステータス：draft, submitted, approved, rejected, cleared）
- **PdfTemplateCategory**: 3 件
- **PdfTemplate**: 5 件
- **JpEstimate**: 5 件
- **JpInvoice**: 5 件
- **JpDelivery**: 5 件
- **JpContract**: 5 件
- **JpReceipt**: 5 件
- **JpDeliverySlip**: 5 件
- **JpInvoiceStandard**: 3 件
- **JpInvoiceBlue**: 3 件
- **JpInvoiceBank**: 3 件（銀行口座情報付き）
- **JpResume**: 3 件
- **FaxCover**: 5 件
- **Meeting**: 5 件（プロジェクトの全ライフサイクルを記録）

**合計**: 83 件のテストデータ

## Entity YAML 定義

### 基本構造

各 Entity YAML ファイルは以下のセクションで構成されます：

```yaml
# 表示名
# 説明

imports: []
entities:
  entity_name:
    table: Table_Name
    key: Id
    displayName: 表示名
    softDelete: false
    isPublic: true
    pdfTemplate: path/to/pdf/template  # PDF テンプレートを使用する場合

    # 検証フック
    hooks:
      beforeCreate:
        - validate_*_status
        - validate_tax_rate
        - validate_amount_positive
        - validate_currency
      beforeUpdate: [...]

    # 結合
    joins:
      - type: left
        table: Customer
        alias: cu
        on: Table.CustomerId = cu.Id

    # フォームフィールド
    forms:
      FieldName:
        type: string|int|date|decimal
        label: 日本語ラベル
        editable: true
        required: true  # NOT NULL の場合
        precision: 2    # decimal の場合
        options: [...]  # 列挙型の場合
        foreignKey:     # 外部キーの場合
          entity: other_entity
          displayColumn: Name
          picker: false

    # 一覧表示カラム
    columns:
      FieldName:
        type: ...
        label: ...
        sortable: true
        searchable: true  # 検索可能フィールド

    # フィルター
    filters:
      Status:
        type: toggle-group
        label: ステータス
      Currency:
        type: dropdown
        label: 通貨
      CustomerId:
        type: dropdown
        label: 取引先

    # ページネーション
    paging:
      pageSize: 15
      mode: numbered
      enableCount: true

    # レイアウト
    layout:
      forms:
        columns: 2

    # エクスポート
    exports:
      pdf:
        label: "PDF"
        format: pdf
      csv:
        label: "CSV"
        format: csv
```

### 主なフィールド設定

#### 通貨フィールド
```yaml
Currency:
  type: string
  label: 通貨
  editable: true
  options: [USD, EUR, CNY, JPY]
```

#### ステータスフィールド
```yaml
Status:
  type: string
  label: ステータス
  editable: true
  required: true
  options: [draft, sent, accepted, rejected, expired]
```

#### 金額フィールド
```yaml
Total:
  type: decimal
  label: 合計
  editable: true
  required: true
  precision: 2
```

#### 外部キーフィールド
```yaml
CustomerId:
  type: int
  label: 取引先 ID
  editable: true
  required: true
  foreignKey:
    entity: customer
    displayColumn: Name
    picker: false
```

## PDF テンプレート定義

### 設定方法

Entity YAML で `pdfTemplate` を指定：

```yaml
entities:
  quotation:
    table: Quotation
    pdfTemplate: orders/purchase-order4
```

### PDF テンプレートパス

| Entity | PDF テンプレートパス |
|--------|---------------------|
| quotation | orders/purchase-order4 |
| invoice | invoices/invoice-standard |
| customs_declaration | trade/customs-declaration |
| jp_estimate | domestic/estimate-standard |
| jp_invoice | domestic/invoice-standard |
| jp_delivery | domestic/delivery-standard |
| jp_contract | domestic/contract-standard |

## 検証フック

### 利用可能なフック

- `validate_quotation_status` - 報価単ステータス検証
- `validate_invoice_status` - 請款書ステータス検証
- `validate_customs_declaration_status` - 報関単ステータス検証
- `validate_tax_rate` - 税率検証（0-100%）
- `validate_amount_positive` - 金額検証（0 以上）
- `validate_currency` - 通貨検証（USD/EUR/CNY/JPY）
- `validate_pdf_template_status` - PDF テンプレートステータス検証

### フックの登録

```yaml
hooks:
  beforeCreate:
    - validate_quotation_status
    - validate_tax_rate
    - validate_amount_positive
    - validate_currency
  beforeUpdate:
    - validate_quotation_status
    - validate_tax_rate
    - validate_amount_positive
    - validate_currency
```

## 関連ファイル

- `database/init_and_seed.sql` - 完全な DB 初期化スクリプト
- `entities/*.yml` - 18 個の Entity 定義
- `Hooks/BizDocsHooks.cs` - 検証フック実装
- `docs/TEST-DATA-GUIDE.md` - テストデータガイド
- `docs/YAML-FIELD-FIX-REPORT.md` - YAML 字段修復報告

## 検証コマンド

### データベース初期化
```bash
cd NetYamlForge/projects/biz-docs/database
sqlite3 biz-docs.db < init_and_seed.sql
```

### データ確認
```bash
sqlite3 biz-docs.db "SELECT COUNT(*) FROM Customer;"
sqlite3 biz-docs.db "SELECT * FROM Quotation LIMIT 3;"
```

### アプリケーション実行
```bash
cd NetYamlForge
dotnet run
```

### テスト実行
```bash
dotnet test --filter "FullyQualifiedName~biz-docs"
```

## ステータス値の一覧

### 報価単 (Quotation)
- `draft` - 下書き
- `sent` - 送付済み
- `accepted` - 承認済み
- `rejected` - 拒否
- `expired` - 有効期限切れ

### 請款書 (Invoice)
- `draft` - 下書き
- `issued` - 発行済み
- `paid` - 入金済み
- `overdue` - 期限超過
- `cancelled` - キャンセル

### 報関単 (CustomsDeclaration)
- `draft` - 下書き
- `submitted` - 申告済み
- `approved` - 承認
- `rejected` - 差戻し
- `cleared` - 通関完了

## 通貨

- `USD` - 米ドル
- `EUR` - ユーロ
- `CNY` - 人民元
- `JPY` - 日本円

## インコタームズ

- `EXW` - 工場渡し
- `FOB` - 本船渡し
- `CIF` - 運賃保険込
- `DAP` - 仕向地持ち込み
- `DDP` - 関税込み持ち込み

## 更新履歴

- 2026-03-25: 完全な DB 初期化スクリプト作成
- 2026-03-25: 83 件のテストデータ追加
- 2026-03-25: 18 表の YAML 定義をデータベースと一致
