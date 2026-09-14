# JPiere 契約サービス (JPCS) — プロジェクトドキュメント

## 概要

**JPiere Contract Service (JPCS)** は、日本企業向け契約・見積・請求・購買・会計・承認フローの総合管理システムです。
iDempiere（オープンソースERP）をベースに、NetYamlForgeフレームワーク上で実装されています。

| 項目 | 内容 |
|------|------|
| **プロジェクト名** | `jpiere-cs` |
| **表示名** | JPiere 契約サービス |
| **バージョン** | 2.0.0 |
| **データベース** | SQLite（デフォルト）|
| **エンティティ数** | 33 |
| **フックファイル** | 7 |
| **AI役割数** | 6 |

---

## ドキュメント索引

### 設計ドキュメント

| ドキュメント | 説明 |
|-------------|------|
| [詳細設計書](./DESIGN.md) | JPCS v2.0 詳細設計（AS-IS分析、TO-BE設計、ERD、Phase 1-3仕様） |
| [AIアシスタント設計](./AI-ASSISTANT-DESIGN.md) | 6役割AIプロンプト設計、エスカレーション、アクセス制御 |

### 実装報告

| ドキュメント | 説明 |
|-------------|------|
| [Phase 1-3 完了報告](./PHASE1_PHASE2_PHASE3_COMPLETE.md) | 全Phase実装完了报告（会計・購買・承認） |
| [AI実装サマリー](./AI-IMPLEMENTATION-SUMMARY.md) | AIコア機能（エンティティ、サービス、フック、ページ）実装一覧 |
| [AI統合報告](./AI-ASSISTANT-INTEGRATION.md) | サービス登録、コントローラー統合、ページ統合 |
| [チャットUI統一](../../../JPIERE_CS_AI_ASSISTANT_UNIFICATION.md) | 右滑パネル型チャットUI実装まとめ |

### 運用ドキュメント

| ドキュメント | 説明 |
|-------------|------|
| [テストデータ補足](./TEST_DATA_SUPPLEMENT.md) | Phase別テストデータ一覧 |

### プロジェクト設定

| ファイル | 説明 |
|---------|------|
| [project.yaml](./project.yaml) | プロジェクト設定（役割、ナビゲーション、AI設定） |
| [ai-config.yaml](./ai-config.yaml) | AI詳細設定 |
| [config/layout.yml](./config/layout.yml) | レイアウト設定 |
| [config/i18n.yml](./config/i18n.yml) | 多言語設定 |

### エンティティ定義

| カテゴリ | ファイル数 | 説明 |
|---------|-----------|------|
| コア契約 | 4 | contract, contract_line, contract_category, contract_template |
| 見積・請求 | 4 | estimation, estimation_line, bill, bill_line |
| 売上計上 | 2 | recognition, recognition_line |
| 購買 | 6 | purchase_order, purchase_order_line, purchase_receipt, purchase_receipt_line, ap_invoice, ap_invoice_line |
| 会計 | 3 | account, journal, journal_line |
| 支払 | 1 | payment |
| 承認 | 2 | approval_request, approval_step |
| 在庫 | 1 | stock_move |
| AI | 5 | ai_conversations, ai_messages, ai_handovers, ai_knowledge, ai_feedback |
| マスタ | 3 | business_partner, product, product_category |
| その他 | 2 | todo, todo_category |

### フック

| ファイル | フック数 | 説明 |
|---------|---------|------|
| [ContractHooks.cs](./Hooks/ContractHooks.cs) | 4 | 採番、金額計算、ステータス遷移、期限チェック |
| [EstimationHooks.cs](./Hooks/EstimationHooks.cs) | 3 | 採番、合計計算、契約変換 |
| [BillingHooks.cs](./Hooks/BillingHooks.cs) | 3 | 採番、期日計算、未済チェック |
| [AccountingHooks.cs](./Hooks/AccountingHooks.cs) | 4 | 仕訳生成、決算、Bill完了/取消 |
| [ApprovalHooks.cs](./Hooks/ApprovalHooks.cs) | 3 | 申請承認、ステップ進行、完了処理 |
| [PurchaseHooks.cs](./Hooks/PurchaseHooks.cs) | 3 | 発注承認、受入処理、AP請求連動 |
| [JpiereAIHooks.cs](./Hooks/JpiereAIHooks.cs) | 3 | エスカレーション、感情分析、TODO自動作成 |

### ページ設定

| ファイル | 説明 |
|---------|------|
| [Dashboard.yaml](./pages/Dashboard.yaml) | メインダッシュボード |
| [ContractDetail.yaml](./pages/ContractDetail.yaml) | 契約詳細ページ |
| [BillDetail.yaml](./pages/BillDetail.yaml) | 請求詳細ページ |
| [EstimationDetail.yaml](./pages/EstimationDetail.yaml) | 見積詳細ページ |
| [MyPage.yaml](./pages/MyPage.yaml) | マイページ |
| [AIAnalytics.yaml](./pages/AIAnalytics.yaml) | AI分析ダッシュボード |
| [AIDashboard.yaml](./pages/AIDashboard.yaml) | AI統合ダッシュボード |
| [ApprovalInquiry.yaml](./pages/ApprovalInquiry.yaml) | 承認照会 |
| [CashFlow.yaml](./pages/CashFlow.yaml) | 資金繰り |
| [StockInquiry.yaml](./pages/StockInquiry.yaml) | 在庫照会 |
| [TrialBalance.yaml](./pages/TrialBalance.yaml) | 残高照会 |
| [AccountBalance.yaml](./pages/AccountBalance.yaml) | 勘定科目残高 |

### バッチジョブ

| ファイル | 説明 |
|---------|------|
| [contract_expiry_alert.yml](./jobs/contract_expiry_alert.yml) | 契約期限アラート |
| [monthly_billing.yml](./jobs/monthly_billing.yml) | 月次請求処理 |
| [journal_close.yml](./jobs/journal_close.yml) | 仕訳締め処理 |
| [payment_reminder.yml](./jobs/payment_reminder.yml) | 支払催促 |

---

## クイックスタート

```bash
# プロジェクト実行
dotnet run --project NetYamlForge

# テスト実行
dotnet test --filter "FullyQualifiedName~Jpiere"
```

---

*最終更新：2026年4月9日*
