# PDF テンプレート定義

このディレクトリには、Wondershare PDFelement のテンプレートライブラリからダウンロードした PDF ファイルに対応する YAML 定義ファイルが含まれています。

## ディレクトリ構造

```
pdf-templates/
├── invoices/           # 請求書テンプレート
│   ├── invoice-standard.yaml
│   ├── invoice-bank.yaml
│   └── invoice-blue.yaml
├── receipts/           # 領収書テンプレート
│   └── receipt-02.yaml
├── orders/             # 注文書テンプレート
│   └── purchase-order4.yaml
└── others/             # その他テンプレート
    ├── fax-cover.yaml
    ├── minutes-03.yaml
    ├── resume-jis.yaml
    └── delivery-slip.yaml
```

## 実体定義

### PdfTemplate エンティティ

PDF テンプレートメタデータを管理します。

**主なフィールド:**
- `TemplateNo`: テンプレート番号
- `CategoryId`: カテゴリ ID（外部キー）
- `Name`: テンプレート名
- `FileName`: PDF ファイル名
- `PageSize`: 用紙サイズ（A4, A3, B4, B5, Letter, Legal）
- `Orientation`: 向き（portrait, landscape）
- `Theme`: テーマ（standard, blue, green, orange, gray, snow, bank）
- `HeaderColor`: ヘッダー色
- `IsDefault`: デフォルトフラグ
- `Status`: ステータス（active, inactive, draft）

### PdfTemplateCategory エンティティ

PDF テンプレートカテゴリを管理します。

**主なカテゴリ:**
| コード | 名称 | 説明 |
|--------|------|------|
| INVOICE | 請求書 | 各種請求書テンプレート |
| ESTIMATE | 見積書 | 各種見積書テンプレート |
| DELIVERY | 納品書 | 各種納品書テンプレート |
| RECEIPT | 領収書 | 各種領収書テンプレート |
| ORDER | 注文書 | 各種注文書テンプレート |
| CONTRACT | 契約書 | 各種契約書テンプレート |
| MINUTES | 議事録 | 議事録テンプレート |
| FAX | FAX 送付状 | FAX カバーシート |
| RESUME | 履歴書 | 履歴書・職務経歴書 |

## YAML テンプレート構造

各 YAML ファイルは以下のセクションで構成されます：

```yaml
name: invoice-standard                    # テンプレート名
filenameTemplate: "請求書_{date:yyyyMMdd}.pdf"  # ファイル名テンプレート
pageSize: A4                              # 用紙サイズ
orientation: portrait                     # 向き
margins: [36, 42, 36, 42]                # 余白 [上，右，下，左]

theme:                                    # テーマ設定
  primaryColor: "1c3658"
  labelColor: "78b4cc"
  labelTextColor: "ffffff"
  subtleBackground: "f0f0f0"
  oddRowColor: "f8fcfe"
  borderColor: "b4b4b4"

dataSources:                              # データソース定義
  customer:
    query: "SELECT ..."
  items:
    query: "SELECT ..."

sections:                                 # レイアウトセクション
  - type: row
    columnWidths: [55, 45]
    cells: [...]
  - type: dataTable
    dataSource: items
    columns: [...]
```

## セクションタイプ

| タイプ | 説明 |
|--------|------|
| `row` | 複数カラムのレイアウト |
| `paragraph` | テキスト段落 |
| `labelTable` | ラベル付きテーブル |
| `dataTable` | データバインドテーブル |
| `line` | 区切り線 |

## 初期データ

`database/seeds/` ディレクトリに SQL 初期データが含まれています：

- `01_pdf_template_categories.sql`: カテゴリマスター
- `02_pdf_templates.sql`: テンプレートメタデータ

## 使用方法

### 1. データベース初期化

```bash
# SQLite データベースにシードデータをインポート
sqlite3 NetYamlForge/projects/biz-docs/database/biz-docs.db < database/seeds/01_pdf_template_categories.sql
sqlite3 NetYamlForge/projects/biz-docs/database/biz-docs.db < database/seeds/02_pdf_templates.sql
```

### 2. アプリケーション起動

```bash
dotnet run --project NetYamlForge
```

### 3. ブラウザでアクセス

```
http://localhost:5000/biz-docs/DynamicEntity/Index?entity=pdf_template
```

## PDF ファイルの配置

ダウンロードした PDF ファイルは以下のディレクトリに配置します：

```
NetYamlForge/projects/biz-docs/pdf-templates/
├── invoices/
│   ├── invoice-standard.pdf
│   ├── invoice-bank.pdf
│   └── ...
├── receipts/
│   └── receipt-02.pdf
└── ...
```

## 関連リンク

- [テンプレート元サイト](https://pdf.wondershare.jp/templates/)
- [プロジェクト設定](../project.yaml)
- [エンティティ定義](../entities/)
