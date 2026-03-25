# biz-docs テストデータガイド

## 概要

biz-docs プロジェクトの全 18 表に挿入されたテストデータ（合計 83 件）のガイドです。

## データ一覧

### マスタデータ

#### Customer（取引先マスタ）- 8 件

| Code | Name | Country | 特徴 |
|------|------|---------|------|
| CUST-001 | 上海貿易有限公司 | CN | 中国・上海 |
| CUST-002 | 北京技術株式会社 | CN | 中国・北京 |
| CUST-003 | 東京商事株式会社 | JP | 日本・東京 |
| CUST-004 | 大阪工業株式会社 | JP | 日本・大阪 |
| CUST-005 | 深圳貿易有限公司 | CN | 中国・深圳 |
| CUST-006 | 横浜商事株式会社 | JP | 日本・横浜 |
| CUST-007 | シンガポール貿易 Pte Ltd | SG | シンガポール |
| CUST-008 | 台北科技股分有限公司 | TW | 台湾・台北 |

#### PdfTemplateCategory（PDF テンプレートカテゴリ）- 3 件

- TRADE: 貿易文書
- DOMESTIC: 国内文書
- TEMPLATE: テンプレート管理

#### PdfTemplate（PDF テンプレート）- 5 件

- TMPL-001: 標準報価単
- TMPL-002: ブルー請款書
- TMPL-003: 和風請求書
- TMPL-004: シンプル納品書
- TMPL-005: 英語報関単

### 貿易文書

#### Quotation（報価単）- 5 件

| QuoteNo | Customer | Total | Status | 通貨 |
|---------|----------|-------|--------|------|
| QT-2026-001 | 上海貿易 | 11,000 | sent | USD |
| QT-2026-002 | 北京技術 | 10,115 | accepted | EUR |
| QT-2026-003 | 東京商事 | 1,650,000 | draft | JPY |
| QT-2026-004 | 大阪工業 | 53,000 | rejected | CNY |
| QT-2026-005 | 深圳貿易 | 27,000 | expired | USD |

**ステータス網羅**: draft, sent, accepted, rejected, expired

#### Invoice（請款書）- 5 件

| InvoiceNo | Customer | Total | Status | 通貨 |
|-----------|----------|-------|--------|------|
| INV-2026-001 | 上海貿易 | 11,000 | paid | USD |
| INV-2026-002 | 北京技術 | 10,115 | issued | EUR |
| INV-2026-003 | 東京商事 | 2,200,000 | overdue | JPY |
| INV-2026-004 | 大阪工業 | 79,500 | cancelled | CNY |
| INV-2026-005 | 深圳貿易 | 16,200 | draft | USD |

**ステータス網羅**: draft, issued, paid, overdue, cancelled

#### CustomsDeclaration（報関単）- 5 件

| DeclarationNo | Customer | Status | Incoterms |
|---------------|----------|--------|-----------|
| CD-2026-001 | 上海貿易 | cleared | FOB |
| CD-2026-002 | 北京技術 | approved | CIF |
| CD-2026-003 | 東京商事 | submitted | EXW |
| CD-2026-004 | 深圳貿易 | draft | DDP |
| CD-2026-005 | 大阪工業 | rejected | DAP |

**ステータス網羅**: draft, submitted, approved, rejected, cleared  
**Incoterms 網羅**: FOB, CIF, EXW, DDP, DAP

### 国内文書

#### JpEstimate（見積書）- 5 件

- EST-2026-001: accepted, 110,000 円
- EST-2026-002: sent, 275,000 円
- EST-2026-003: draft, 55,000 円
- EST-2026-004: rejected, 198,000 円
- EST-2026-005: expired, 352,000 円

#### JpInvoice（請求書）- 5 件

- JP-INV-2026-001: paid, 110,000 円（入金済）
- JP-INV-2026-002: issued, 275,000 円（一部入金）
- JP-INV-2026-003: overdue, 55,000 円（期限超過）
- JP-INV-2026-004: draft, 198,000 円（下書）
- JP-INV-2026-005: cancelled, 88,000 円（キャンセル）

#### JpInvoiceStandard（請求書・標準）- 3 件

- STD-2026-001: 132,000 円
- STD-2026-002: 104,500 円
- STD-2026-003: 220,000 円

#### JpInvoiceBlue（請求書・青）- 3 件

- BLU-2026-001: 82,500 円
- BLU-2026-002: 165,000 円
- BLU-2026-003: 96,800 円

#### JpInvoiceBank（請求書・銀行用）- 3 件

- BNK-2026-001: 99,000 円（三菱 UFJ 銀行）
- BNK-2026-002: 121,000 円（三井住友銀行）
- BNK-2026-003: 71,500 円（みずほ銀行）

#### JpDelivery（納品書）- 5 件

- DLV-2026-001: delivered（配送完了）
- DLV-2026-002: received（受領確認）
- DLV-2026-003: delivered（配送中）
- DLV-2026-004: draft（出荷準備）
- DLV-2026-005: draft（在庫手配）

#### JpDeliverySlip（送付状）- 5 件

- SLP-2026-001: 3 点同梱
- SLP-2026-002: 5 点（サンプル同梱）
- SLP-2026-003: 2 点（請求書・納品書）
- SLP-2026-004: 1 点（出荷予定）
- SLP-2026-005: 4 点（複数口）

#### JpReceipt（領収書）- 5 件

- RCP-2026-001: 110,000 円（銀行振込）
- RCP-2026-002: 100,000 円（小切手）
- RCP-2026-003: 55,000 円（現金）
- RCP-2026-004: 50,000 円（分割 1 回目）
- RCP-2026-005: 50,000 円（分割 2 回目）

#### JpContract（契約書台帳）- 5 件

- CNT-2026-001: 基本取引契約（active）
- CNT-2026-002: 製品供給契約（active, 500 万円）
- CNT-2026-003: 保守サービス契約（draft, 120 万円）
- CNT-2025-001: 旧基本契約（completed）
- CNT-2025-002: 解約済契約（terminated）

### その他

#### JpResume（履歴書）- 3 件

- RES-2026-001: 田中太郎（東大卒，管理職経験）
- RES-2026-002: 山田花子（慶應卒，経理経験）
- RES-2026-003: 鈴木一郎（阪大卒，PMP 資格）

#### FaxCover（ファックス表紙）- 5 件

- FAX-2026-001: 見積書送付（5 枚）
- FAX-2026-002: 契約書確認（3 枚）
- FAX-2026-003: お問い合せ（2 枚）
- FAX-2026-004: 製品カタログ（10 枚）
- FAX-2026-005: 会議日程調整（1 枚）

#### Meeting（会議録）- 5 件

| MeetingNo | Title | Date | Organizer |
|-----------|-------|------|-----------|
| MTG-2026-001 | 第 1 回プロジェクトキックオフ | 2026-01-10 | 田中 |
| MTG-2026-002 | 要件定義レビュー | 2026-01-20 | 佐藤 |
| MTG-2026-003 | 中間報告会 | 2026-02-05 | 田中 |
| MTG-2026-004 | テスト結果報告 | 2026-02-15 | 鈴木 |
| MTG-2026-005 | プロジェクト完了報告 | 2026-02-28 | 田中 |

## データの特徴

### ステータス網羅

各テーブルのステータスを全て網羅：

- **Quotation**: draft, sent, accepted, rejected, expired
- **Invoice**: draft, issued, paid, overdue, cancelled
- **CustomsDeclaration**: draft, submitted, approved, rejected, cleared
- **JpEstimate**: draft, sent, accepted, rejected
- **JpInvoice**: draft, issued, paid, overdue, cancelled
- **JpDelivery**: draft, delivered, received
- **JpContract**: draft, active, completed, terminated
- **PdfTemplate**: active, inactive, draft

### 通貨対応

- **USD**: 米ドル
- **EUR**: ユーロ
- **JPY**: 日本円
- **CNY**: 人民元

### 日付範囲

- **開始日**: 2026-01-10
- **終了日**: 2026-03-26
- **期間**: 約 2.5 ヶ月

## 使用方法

### テストデータのリセット

```bash
cd NetYamlForge/projects/biz-docs
sqlite3 database/biz-docs.db < database/seed-data.sql
```

### 特定テーブルのデータ確認

```bash
sqlite3 database/biz-docs.db "SELECT * FROM Quotation;"
sqlite3 database/biz-docs.db "SELECT * FROM Invoice;"
sqlite3 database/biz-docs.db "SELECT * FROM Customer;"
```

### データ件数確認

```bash
sqlite3 database/biz-docs.db << EOF
SELECT 'Customer' as Table, COUNT(*) as Count FROM Customer
UNION ALL SELECT 'Quotation', COUNT(*) FROM Quotation
UNION ALL SELECT 'Invoice', COUNT(*) FROM Invoice
UNION ALL SELECT 'Meeting', COUNT(*) FROM Meeting;
EOF
```

## 関連ファイル

- `database/init.sql` - 表定義スクリプト
- `database/seed-data.sql` - テストデータスクリプト
- `entities/*.yml` - 各 Entity 定義
- `Hooks/BizDocsHooks.cs` - 検証フック

## 注意事項

1. **外部キー整合性**: Customer データを削除する場合は、関連する Quotation, Invoice 等のデータも削除してください
2. **ID 重複**: 手動でデータを追加する際は、ID の重複に注意してください
3. **日付形式**: DATE 型は 'YYYY-MM-DD'、DATETIME 型は 'YYYY-MM-DD HH:MM:SS' 形式です
4. **文字コード**: UTF-8 環境で使用してください（日本語データを含むため）
