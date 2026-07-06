# 日本向けビジネス文書 設計リファレンス

NetYamlForge の `biz-docs` サブプロジェクトに実装された、日本の商習慣・法令に準拠したビジネス文書のリファレンス。

---

## 1. 見積書（Estimate / 御見積書）

### 法的位置づけ
- 法的拘束力はないが、注文書/発注書で受諾されると契約が成立する
- 有効期限（通常 30 日）を明記することが商習慣

### 必須記載事項
| 項目 | 説明 |
|------|------|
| 見積番号 | 一意の管理番号 |
| 作成日 / 有効期限 | 有効期限は「発行日より30日」等の文言で記載 |
| 宛名（取引先） | 「○○株式会社 御中」 |
| 品名・数量・単価・金額 | 明細行ごとに記載 |
| 消費税区分 | 10%（通常税率）/ 8%（軽減税率）を品目ごとに区分 |
| 小計 / 消費税額 / 合計 | 税率別に集計してから合算 |
| 納期・納品場所 | 「受注後○営業日以内」「貴社指定場所」等 |
| 支払条件 | 「納品後30日以内」等 |
| 除外事項 | 見積に含まれない項目を明記 |
| 担当者 / 社印 | 作成者名と会社印鑑 |

### 消費税の扱い（インボイス制度対応）
- `Subtotal10` / `TaxAmount10` : 10%通常税率の小計・税額
- `Subtotal8` / `TaxAmount8` : 8%軽減税率（食品等）の小計・税額
- `Subtotal` = `Subtotal10 + Subtotal8`（税抜合計）
- `TaxAmount` = `TaxAmount10 + TaxAmount8`（消費税合計）
- `Total` = `Subtotal + TaxAmount`（税込合計）

### DBスキーマ（JpEstimate / JpEstimateItem）
```sql
JpEstimate:
  EstimateNo, CustomerId, Title, IssueDate, ExpiryDate
  Subtotal10, TaxAmount10, Subtotal8, TaxAmount8
  Subtotal, TaxAmount, Total
  PaymentTerms, DeliveryDays, DeliveryPlace
  ValidityNote, ExclusionNote
  Status(draft/sent/accepted/rejected/expired)
  PreparedBy, CompanyStamp, Remarks

JpEstimateItem:
  EstimateId, LineNo, ItemCode, ItemName, Spec
  Unit, Quantity, UnitPrice, Amount
  TaxRate(10 or 8)
```

---

## 2. 請求書（Invoice / 御請求書）

### 法的位置づけ
- 商取引の対価請求書類。**2023年10月よりインボイス制度（適格請求書等保存方式）開始**
- 適格請求書を発行できるのは、税務署登録を受けた「適格請求書発行事業者」のみ
- 買い手が消費税の仕入税額控除を受けるためには、適格請求書の保存が必要

### 必須記載事項（インボイス制度対応）
| 項目 | 説明 |
|------|------|
| 請求書番号 | 一意の管理番号 |
| 作成日 / 支払期日 | 支払期日は「○年○月○日」または「発行月翌月末」等 |
| 登録番号 | **T + 13桁の数字**（適格請求書発行事業者のみ） |
| 宛名（取引先） | 「○○株式会社 御中」 |
| 品名・数量・単価・金額 | 明細行ごとに記載 |
| 税率区分 | 品目ごとに「10%」「8%（軽減）」を明示 |
| 税率別小計 | 10%対象合計額・8%対象合計額を区分して記載 |
| 税率別消費税額 | 10%分消費税・8%分消費税を区分して記載 |
| 合計金額（税込） | 請求総額 |
| 振込先 | 銀行名・支店名・口座種別・口座番号・口座名義 |
| 振込手数料 | 「振込手数料はご負担ください」の文言 |
| 担当者 / 社印 | 作成者名と会社印鑑 |

### インボイス登録番号
- `T` + 13桁の法人番号（例: `T1234567890123`）
- 国税庁適格請求書発行事業者公表サイトで確認可能
- 未登録事業者（免税事業者等）は「登録番号なし」と明記、または空欄

### DBスキーマ（JpInvoice / JpInvoiceItem）
```sql
JpInvoice:
  InvoiceNo, CustomerId, Title, IssueDate, DueDate
  RegistrationNo         -- T + 13桁
  Subtotal10, TaxAmount10, Subtotal8, TaxAmount8
  Subtotal, TaxAmount, Total
  BankName, BranchName, AccountType, AccountNo, AccountHolder
  TransferFeeNote
  Status(draft/issued/paid/overdue/cancelled)
  PreparedBy, CompanyStamp, Remarks

JpInvoiceItem:
  InvoiceId, LineNo, ItemCode, ItemName, Spec
  Unit, Quantity, UnitPrice, Amount
  TaxRate(10 or 8)
```

---

## 3. 納品書（Delivery Note / 納品書）

### 法的位置づけ
- 商品・役務を納品した事実を証明する書類
- 受領確認後「受領印」または「検収書」が発行されることが多い
- 検収期間を明記する（通常 5 営業日）

### 必須記載事項
| 項目 | 説明 |
|------|------|
| 納品書番号 | 一意の管理番号 |
| 納品日 | 実際の納品日 |
| 宛名（取引先） | 「○○株式会社 御中」 |
| 納品場所 | 「貴社○○部門」「貴社指定倉庫」等 |
| 品名・数量・単価・金額 | 明細行ごとに記載（単価省略可だが推奨） |
| 税率区分 | 10%/8%の区分（請求書と同様） |
| 小計 / 消費税 / 合計 | 税率別集計 |
| 検収期間 | 「納品後5営業日以内にご連絡ください」等 |
| 受領確認日 | 相手方確認後に記入（受領印相当） |
| 担当者 / 社印 | 作成者名と会社印鑑 |

### ステータス管理
- `draft` → `delivered`（納品完了）→ `confirmed`（受領確認） / `returned`（返品）

### DBスキーマ（JpDelivery / JpDeliveryItem）
```sql
JpDelivery:
  DeliveryNo, CustomerId, Title, DeliveryDate, DeliveryPlace
  Subtotal10, TaxAmount10, Subtotal8, TaxAmount8
  Subtotal, TaxAmount, Total
  InspectionPeriodDays   -- 検収期間（営業日数）
  ReceiptConfirmedDate   -- 受領確認日
  Status(draft/delivered/confirmed/returned)
  PreparedBy, CompanyStamp, Remarks

JpDeliveryItem:
  DeliveryId, LineNo, ItemCode, ItemName, Spec
  Unit, Quantity, UnitPrice, Amount
  TaxRate(10 or 8)
```

---

## 4. 契約書（Contract）

### 法的位置づけ
- 民法上の契約は口頭でも成立するが、証拠として書面化が必須
- 2023年のデジタル化推進により電子契約（電子署名法）が普及
- **紙契約**は印紙税法の対象（金額に応じて印紙貼付が必要）
- **電子契約**は印紙税非課税（電子文書には印紙不要）

### 契約書の主要種別
| 種別 | 説明 |
|------|------|
| 業務委託契約 | 準委任（成果物なし）または請負（成果物あり） |
| 売買契約 | 物品・ソフトウェアの売買 |
| 秘密保持契約（NDA） | 機密情報の保護 |
| 保守契約 | システム・機器の保守運用 |
| 賃貸借契約 | 不動産・設備の賃貸 |
| 請負契約 | 成果物完成義務付きの委託 |

### 必須記載事項
| 項目 | 説明 |
|------|------|
| 契約番号 | 一意の管理番号 |
| 契約件名 | 「○○開発業務委託契約」等 |
| 甲乙（当事者） | 「甲：自社」「乙：取引先」として定義 |
| 契約期間 | 開始日・終了日・自動更新の有無 |
| 契約金額・支払条件 | 総額と支払スケジュール |
| 業務内容 | 委託業務の詳細 |
| 締結日 | 合意日（有効期間の起算点） |
| 合意管轄裁判所 | 紛争時の第一審裁判所 |
| 準拠法 | 「日本法」を明記 |
| 反社会的勢力排除条項 | 現在ほぼ全契約で必須 |
| 秘密保持条項 | |
| 損害賠償・免責条項 | |

### 印紙税額の目安（紙契約の場合）
| 契約金額 | 印紙税額 |
|----------|----------|
| 〜100万円 | 1,000円 |
| 〜200万円 | 2,000円 |
| 〜300万円 | 2,000円 |
| 〜500万円 | 2,000円 |
| 〜1,000万円 | 10,000円 |
| 〜5,000万円 | 20,000円 |
| 〜1億円 | 60,000円 |
| 記載金額なし | 200円 |

### DBスキーマ（JpContract）
```sql
JpContract:
  ContractNo, CustomerId, Title, ContractType
  StartDate, EndDate, AutoRenew
  ContractAmount, PaymentTerms
  Status(draft/review/active/expired/terminated)
  SignedDate
  IsElectronic           -- 1=電子契約（印紙不要）/ 0=紙
  StampTaxAmount         -- 印紙税額（紙契約時のみ）
  JurisdictionCourt      -- 合意管轄裁判所
  GoverningLaw           -- 準拠法（通常「日本法」）
  OurSignatory, TheirSignatory
  Remarks
```

---

## 5. 共通ルール

### 金額の記載
- 単位は「円」（JPY）
- 3桁カンマ区切り（例: 1,100,000円）
- 消費税は税抜金額に対して計算（端数は切り捨て推奨）

### 日付表記
- 西暦（yyyy-MM-dd）または和暦（令和○年○月○日）
- 請求書では「支払期日: 20xx年xx月xx日」と明記

### 消費税区分
- **10%（通常税率）**: 一般的な商品・サービス全般
- **8%（軽減税率）**: 飲食料品・定期購読新聞のみ対象
- IT/ソフトウェア/ハードウェアは全て10%

### 宛名の書き方
- 法人: 「○○株式会社 御中」
- 個人: 「○○ 様」
- 部署宛: 「○○株式会社 ○○部 御中」

### 社印（会社印鑑）
- 角印（会社名のみ）: 一般的な取引書類に使用
- 丸印（代表者印）: 契約書等の重要書類に使用
- 電子印鑑: 画像として押印位置に配置するか電子署名を付与

---

## 6. PDF エクスポート仕様（NetYamlForge）

各エンティティに定義された PDF エクスポート:

| エンティティ | エクスポート名 | 説明 |
|-------------|---------------|------|
| jp_invoice | invoice_pdf | 請求書一覧（インボイス登録番号・税率別集計付き） |
| jp_invoice | overdue_invoice_pdf | 未回収請求書（入金催促用・赤） |
| jp_estimate | estimate_pdf | 見積書一覧 |
| jp_estimate | estimate_detail_pdf | 見積書明細（品目展開・カスタムSQL） |
| jp_delivery | delivery_pdf | 納品書一覧 |
| jp_delivery | delivery_detail_pdf | 納品書明細（品目展開・カスタムSQL） |
| jp_contract | contract_pdf | 契約書台帳 |
| jp_contract | expiry_warning_pdf | 期限切れ警告（90日以内・電子/紙区分付き） |

### カスタムSQL エクスポート（sqlFile:）
- `exports/sql/jp_estimate_detail.sql` - 見積書＋明細品目JOIN
- `exports/sql/jp_overdue_invoices.sql` - 未収金一覧（滞納日数計算）
- `exports/sql/jp_delivery_detail.sql` - 納品書＋明細品目JOIN
- `exports/sql/jp_contract_expiry_warning.sql` - 期限90日以内の有効契約
