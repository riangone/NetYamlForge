# 自動車ディーラーシステム × NetYamlForge 対応状況分析

> 対象仕様書：[auto-dealer-system-spec.md](./auto-dealer-system-spec.md)
> 分析日：2026-03-27

---

## 全体サマリー

| カテゴリ | 割合 | 内容 |
|---------|------|------|
| ✅ そのまま実装可能 | 約70% | コアCRUD・在庫管理・販売管理・API |
| ⚠️ 追加実装で対応可能 | 約20% | 請求計算・バッチ・PDF帳票・ステータス制御 |
| ❌ フレームワーク外 | 約10% | メール・SMS・LINE・AI窓口 |

---

## ✅ そのまま実装できる機能

| 機能 | 実装方法 |
|------|---------|
| 顧客管理（CRUD・検索・履歴表示） | エンティティYAML + FK関連 + フィルター定義 |
| 車両在庫管理（一覧・検索・絞り込み） | エンティティYAML + ステータス列 |
| 部品マスタ・在庫照会 | エンティティYAML + 発注点アラート（hook） |
| 見積書・契約書・請求書の基本CRUD | エンティティYAML + FK選択（entity-picker） |
| 作業指示書・サービス受付 | エンティティYAML + ステータス管理 |
| ダッシュボード（統計カード・グラフ） | `dashboard.yml` + SQL集計 |
| CSV・PDF出力 | `exports`セクション + PDFテンプレート |
| ユーザー認証・ロール管理 | 標準機能（admin/user） |
| REST API（全エンティティ） | 自動生成（`/{project}/api/entities/{entity}`） |
| バッチジョブ（月次集計CSV出力等） | `jobs/*.yml` + `sql_to_csv`タイプ |
| 監査ログ（操作履歴） | `audit_log`組み込みフック |
| 入庫・出庫時の在庫数自動更新 | `update_related`・カスタムフック |
| 自動採番（見積番号・契約番号等） | カスタムフック（`BeforeAsync`で採番） |

---

## ⚠️ 一部カスタム実装が必要な機能

| 機能 | 現状 | 必要な作業 |
|------|------|-----------|
| 請求書合計金額の自動計算 | 組み込みなし | カスタムフック（税込金額計算） |
| ステータス遷移制御（逆順禁止） | 組み込みなし | カスタムフック（`BeforeAsync`でチェック） |
| 在庫不足時の発注フラグ | 部分対応 | カスタムフック（在庫減算後にフラグON） |
| 次回点検時期の自動算出 | 組み込みなし | カスタムフック（日数・走行距離計算） |
| PDF帳票（見積書・契約書様式） | テンプレート機能あり | PDFテンプレートYAML定義が必要 |
| バッチ失敗アラート | Webhook送信可 | Webhookで外部Slackに通知（設定のみ） |
| ローン計算・頭金チェック | 組み込みなし | カスタムフック（金額バリデーション） |

---

## ❌ 現フレームワークでは実装できない機能

| 機能 | 理由 |
|------|------|
| **メール通知**（リマインダー・アンケート送信） | SMTPが未実装（TODO状態） |
| **SMS連携**（Twilio等） | HTTP APIクライアント機能なし |
| **LINE連携**（Webhook受信・送信） | LINE Official Account API連携機能なし |
| **電子署名**（顧客署名欄） | ファイルアップロードのみ、署名パッド未対応 |
| **行レベルセキュリティ（RLS）** | エンティティ単位の権限のみ、行単位不可 |
| **一括操作**（複数行まとめて処理） | リスト画面の一括処理UI未対応 |
| **カンバン・ワークフローボード** | カスタムページで部分的に可能だが未実装 |
| **AIチャット窓口・NLU** | 別システムが必要（フレームワーク外） |
| **CTI（電話着信）連携** | 外部システム連携機能なし |
| **A/Bテスト・MLパイプライン** | フレームワークの範囲外 |
| **フィールド単位のアクセス制御** | 全フィールドが同権限で表示 |
| **OR条件フィルター** | フィルターはAND条件のみ |
| **リアルタイム通知**（WebSocket等） | 未実装 |

---

## 実装計画

### フェーズ1：エンティティ定義（YAML設計）

作業量目安：**20〜40時間**

```
projects/auto-dealer/
  entities/
    customers.yml          # 顧客
    vehicles.yml           # 車両在庫
    estimates.yml          # 見積書
    contracts.yml          # 販売契約
    payments.yml           # 入金
    deliveries.yml         # 納車記録
    service_requests.yml   # サービス受付
    work_orders.yml        # 作業指示書
    parts.yml              # 部品マスタ
    part_usage.yml         # 部品使用実績
    part_orders.yml        # 部品発注
    stock_in.yml           # 部品入庫
    stock_out.yml          # 部品出庫
    stock_adjustments.yml  # 在庫調整
    invoices.yml           # 請求書
    leads.yml              # リード
    meetings.yml           # 商談
    followups.yml          # フォローアップ
    inquiries.yml          # 問い合わせ
    support_tickets.yml    # サポートチケット
```

### フェーズ2：カスタムフック実装（C#）

作業量目安：**36〜72時間**

```
projects/auto-dealer/Hooks/
  EstimateCalculationHook.cs      // 見積金額自動計算
  ContractNumberHook.cs           // 契約番号自動採番
  VehicleStatusHook.cs            // 車両在庫ステータス遷移制御
  PaymentValidationHook.cs        // 入金金額・残高チェック
  PartStockUpdateHook.cs          // 在庫数更新・発注フラグ制御
  InvoiceCalculationHook.cs       // 請求書税込金額計算
  ServiceStatusHook.cs            // 作業進捗ステータス遷移制御
  NextInspectionDateHook.cs       // 次回点検日自動算出
  LeadStatusHook.cs               // リードステータス更新
```

### フェーズ3：バッチジョブ定義

作業量目安：**8〜16時間**

```
projects/auto-dealer/jobs/
  monthly_stock_summary.yml       # 月末在庫集計（→CSV出力）
  service_reminder.yml            # 次回点検リマインダー（→Webhook）
  unanswered_inquiry_alert.yml    # 未対応問い合わせエスカレーション
  monthly_sales_report.yml        # 月次売上集計レポート
```

### フェーズ4：ダッシュボード・帳票

作業量目安：**16〜24時間**

```
projects/auto-dealer/
  dashboard.yml                   # 本日受付件数・未対応チケット・売上グラフ等
  templates/
    estimate.yml                  # 見積書PDFテンプレート
    invoice.yml                   # 請求書PDFテンプレート
```

### フェーズ5：メール通知（要判断）

#### 選択肢A：Webhookで外部サービスに委譲（推奨）

- `webhook`フックでSendGrid / AWS SES等のAPIを呼び出す
- フレームワーク変更なし、即実装可能
- 外部サービスの契約が必要

#### 選択肢B：フレームワークにSMTPを追加実装

- `IEmailService`インターフェースを追加
- `SmtpEmailService`を実装
- バッチジョブの`onFailure`フックと連携
- 追加作業量：**24〜40時間**

---

## フェーズ別作業量合計

| フェーズ | 作業内容 | 目安工数 |
|---------|---------|---------|
| Phase 1 | エンティティYAML設計（20エンティティ） | 20〜40時間 |
| Phase 2 | カスタムフック実装（9フック） | 36〜72時間 |
| Phase 3 | バッチジョブ定義（4ジョブ） | 8〜16時間 |
| Phase 4 | ダッシュボード・PDFテンプレート | 16〜24時間 |
| Phase 5 | メール通知（選択肢Aなら0、Bなら追加） | 0〜40時間 |
| **合計** | | **80〜192時間** |

---

## 推奨着手順序

1. `dotnet run -- --init-project --project=auto-dealer` でプロジェクト初期化
2. DBスキーマを先に設計し `--scaffold-entities` でYAMLの骨格を自動生成
3. 各エンティティYAMLにFK関連・フィルター・バリデーションを追記
4. `--scaffold-hook` でフック雛形を生成し、ビジネスロジックを実装
5. `--scaffold-batch-job` でバッチジョブ雛形を生成
6. `dashboard.yml` と PDFテンプレートを追加
