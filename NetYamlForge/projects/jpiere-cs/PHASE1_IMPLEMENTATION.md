# Phase 1 実装完了レポート

> 実装日: 2026-04-06 | ステータス: ✅ 完了

---

## 実装内容

### 1. データベーススキーマ追加

**ファイル**: `database/init.sql`

以下の3テーブルを追加:

| テーブル | 行数 | 説明 |
|---|---|---|
| `accounts` | 18 | 勘定科目マスタ（コード、名前、種別、正常残高） |
| `journals` | 21 | 仕訳ヘッダ（伝票番号、状態、借貸合計、均衡フラグ） |
| `journal_lines` | 17 | 仕訳明細（勘定科目、借方金額、貸方金額） |

**テストデータ追加**:
- 勘定科目: 17件（資産・負債・収益・費用）
- 仕訳ヘッダ: 3件（請求確定済み）
- 仕訳明細: 9件（借方・貸方・消費税）

---

### 2. YAMLエンティティ定義

**新規ファイル**:

| ファイル | エンティティ | 説明 |
|---|---|---|
| `entities/account.yml` | account | 勘定科目マスタ管理 |
| `entities/journal.yml` | journal | 仕訳管理（Hook登録済み） |
| `entities/journal_line.yml` | journal_line | 仕訳明細管理 |

**Hook登録**:

- `journal.yml`:
  - `beforeCreate`: journal_document_no, journal_balance_validation
  - `beforeUpdate`: journal_balance_validation

- `entities/bill.yml`（更新）:
  - `afterUpdate`: bill_complete, bill_reverse

- `entities/recognition.yml`（更新）:
  - `afterUpdate`: recognition_complete

---

### 3. Hook実装

**ファイル**: `Hooks/AccountingHooks.cs` (378行)

実装したHookクラス:

| クラス名 | Hook名 | 登録タイミング | 機能 |
|---|---|---|---|
| `JournalDocumentNoHook` | journal_document_no | beforeCreate | 仕訳番号の自動採番 `JNL-YYYYMM-XXXX` |
| `JournalBalanceValidationHook` | journal_balance_validation | beforeCreate/Update | 借貸均衡チェック（借方≠貸方の場合保存拒否） |
| `BillCompleteHook` | bill_complete | afterUpdate | 請求確定時の仕訳自動起票（売掛金/売上/消費税） |
| `BillReverseHook` | bill_reverse | afterUpdate | 請求取消時の逆仕訳起票（借方と貸方を入れ替え） |
| `RecognitionCompleteHook` | recognition_complete | afterUpdate | 売上認識確定時の仕訳自動起票（売掛金/サービス売上） |

#### 仕訳起票ルール

**請求確定時** (Bill.doc_status: DR → CO):
```
借方: 売掛金 (1100)        = grand_total
  貸方: 売上高 (4100)       = tax_base_amt
  貸方: 仮受消費税 (2400)   = tax_amt
```

**売上認識確定時** (Recognition.doc_status: DR → CO):
```
借方: 売掛金 (1100)        = grand_total
  貸方: サービス売上高 (4100) = grand_total
```

**請求取消時** (Bill.doc_status: CO → RE):
```
元の仕訳の逆仕訳（借方↔貸方）
```

---

### 4. カスタムページ

**新規ファイル**:

| ファイル | ページ名 | 説明 |
|---|---|---|
| `pages/AccountBalance.yaml` | 勘定科目残高照会 | 各勘定科目の借貸合計と残高を表示 |
| `pages/TrialBalance.yaml` | 試算表 | 勘定科目別の試算表（正常残高に基づく残高計算） |

---

### 5. ナビゲーション更新

**ファイル**: `config/layout.yml`

「会計」セクションを追加:

```
会計
├─ 勘定科目
├─ 仕訳
├─ 残高照会
└─ 試算表
```

---

### 6. テスト実装

**ファイル**: `NetYamlForge.Tests/Hooks/AccountingHooksTests.cs` (565行)

実装したテストクラス:

| テストクラス | テスト数 | 内容 |
|---|---|---|
| `JournalDocumentNoHookTests` | 4 | 採番ロジック、既存値スキップ |
| `JournalBalanceValidationHookTests` | 4 | 均衡チェック、不均衡拒否、許容誤差 |
| `BillCompleteHookTests` | 4 | 仕訳起票、明細数、重複防止 |
| `BillReverseHookTests` | 3 | 逆仕訳起票、借方貸方入替 |
| `RecognitionCompleteHookTests` | 4 | 仕訳起票、明細数、重複防止 |

**テスト結果**: ✅ 19件 全て成功

---

## ビルド・テスト結果

```bash
# ビルド
✅ 成功（警告のみ、エラーなし）

# 新規テスト実行
✅ 19 passed, 0 failed

# 全体テスト
✅ 502 passed, 13 failed（既存の失敗、本実装とは無関係）
```

---

## 設計原則の遵守

### iDempiere準拠

1. ✅ **ドキュメント中心**: 仕訳は請求・売上認識ドキュメントの確定時に自動起票
2. ✅ **二重仕訳**: 全ての仕訳は借方=貸方（`is_balanced=1`）を強制
3. ✅ **取消不可原則**: 確定済み仕訳の直接変更は不可、取消は逆仕訳で対応
4. ✅ **トランザクション一貫性**: Hookは同一トランザクション内で実行

### NetYamlForgeフレームワーク制約

1. ✅ Hookは `IEntityHook` インターフェース実装のみ
2. ✅ 仕訳起票は `AfterAsync` 内で `IDbConnection` / `IDbTransaction` を使用
3. ✅ `SqlSafetyGuard` を通した安全なSQL実行
4. ✅ カスタムページは YAML定義のSQLで集計クエリを実装

---

## 次のステップ（Phase 2）

Phase 1完了後に実装可能な機能:

1. **購買フロー**
   - purchase_orders, purchase_receipts, ap_invoices, payments テーブル
   - stock_moves（在庫移動）
   - PurchaseReceiptCompleteHook, APInvoiceCompleteHook, PaymentCompleteHook

2. **3方向照合**
   - 発注書・受入・請求書の照合チェック

3. **在庫管理**
   - 現在庫数量の集計クエリ
   - 在庫照会ページ

---

## 使用方法

### 仕訳の自動起票

1. 請求書（Bill）を作成し、`doc_status` を `CO` に更新
2. 自動的に `journals` + `journal_lines` に仕訳が挿入される

### 試算表の確認

1. メニュー → 会計 → 試算表
2. 確定済み仕訳（`doc_status='CO'`）の集計が表示される

---

*実装完了: 2026-04-06*
