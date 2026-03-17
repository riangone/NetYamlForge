# salesforce-crm 実装ガイド（詳細版）

## 1. 目的と適用範囲
`salesforce-crm` は、既存の Dynamic CRUD 基盤上で Salesforce 風 CRM を業務運用可能な粒度まで段階的に再現するためのサブプロジェクトです。

本ドキュメントは以下を定義します。
- 必須機能とページ一覧
- データモデル対応方針
- 実装優先順位（MVP -> 本番運用）
- 受け入れ基準

## 2. 現状とギャップ
### 2.1 現在実装済み
- ダッシュボード/専用ページ（Sales/Revenue/Service/Marketing/Admin）:
  - Executive Cockpit / Lead Command Center / Lead Inbox / Lead Detail 360
  - Account Workspace / Contact Workspace / Opportunity Workspace / Opportunity Detail / Pipeline Board
  - Activity Console / Quote Builder / Forecast Console
  - Service Desk 360 / Case Queue / Case Detail / Omni-Channel Console / Knowledge Base / SLA Monitor
  - Campaign Planner / Campaign Member / Attribution Dashboard
  - Approval Inbox / Automation Rules / Assignment Rules / Duplicate Rules
  - Object Manager / Role Access Matrix / User Role Profile / Data Import Export / Audit Trail
  - Integration Hub / Webhook Delivery Monitor / Order Management / Contract Center / Invoice & Payment
- CRUD:
  - `order / customer / orderdetail / employee / product / supplier / shipper / category`
- 多言語:
  - `en-US / zh-CN / ja-JP / ko-KR`

### 2.2 主な不足
- 承認・割当・重複判定はページ導線中心で、外部連携を含む高度な自動処理は継続拡張対象
- 自動化ルールはバックグラウンド実行済みだが、`CrmAutomationRule` 完全駆動化は未完
- ロール/権限管理はDBシード済みだが、権限編集の管理UIは未実装

### 2.3 運用トレーサビリティ（実装済み）
- `Page` 経由の主要更新/削除操作は `AuditLog` に `page_update / page_delete` として記録
- `Case Queue / Approval Inbox / Opportunity Detail` などの画面操作で監査痕跡を追跡可能

## 3. 業務ドメイン別 必須ページ一覧

## 3.1 Sales Cloud（営業）
1. `Lead Inbox`（線索受信箱）
- 役割: 新規リード受付、担当者振分、重複確認
- 主要操作: assign / merge / qualify / reject

2. `Lead Detail 360`
- 役割: 線索情報、活動履歴、接触記録を一画面で管理
- 主要操作: activity 追加、次回フォロー設定、添付

3. `Account Workspace`
- 役割: 企業単位で案件・取引・サポート履歴を統合表示
- 主要操作: 担当変更、重要度設定、関係者追加

4. `Contact Workspace`
- 役割: 連絡先管理、役職/意思決定権/影響度管理
- 主要操作: 連絡先更新、優先連絡先設定

5. `Opportunity Board`（Kanban）
- 役割: 商談をステージ別に可視化し、前進停滞を管理
- 主要操作: drag & drop でステージ更新

6. `Opportunity Detail`
- 役割: 金額・確度・競合・次アクションを管理
- 主要操作: stage/probability 更新、リスク登録

7. `Activity Console`
- 役割: タスク/電話/会議/メールの一元管理
- 主要操作: ToDo生成、期限/優先度更新

8. `Quote Builder`
- 役割: 見積生成、版管理、承認申請
- 主要操作: 行追加、割引申請、見積確定

## 3.2 Revenue Cloud（受注・売上）
1. `Order Management`
- 役割: 受注・出荷・キャンセル管理
- 主要操作: ステータス遷移、遅延管理

2. `Contract Center`
- 役割: 契約期間、更新、解約管理
- 主要操作: 更新予定通知、自動更新判定

3. `Invoice & Payment`
- 役割: 請求と回収状況管理
- 主要操作: 入金消込、未収アラート

4. `Forecast Console`
- 役割: 期間/担当/部門別予測
- 主要操作: commit値調整、予測ロック

## 3.3 Service Cloud（サポート）
1. `Case Queue`
- 役割: 問い合わせ受付、優先順位付け、アサイン
- 主要操作: assign / escalate / resolve

2. `Case Detail`
- 役割: 対応履歴、SLA、ナレッジ参照
- 主要操作: ステータス遷移、原因分類

3. `Omni-Channel Console`
- 役割: 待ちキュー配信と負荷平準化
- 主要操作: 自動再割当、応答時間監視

4. `Knowledge Base`
- 役割: FAQ/手順書の作成・承認・公開
- 主要操作: 下書き、レビュー、公開

5. `SLA Monitor`
- 役割: SLA違反予兆、違反一覧、是正
- 主要操作: 期限延長申請、優先度再設定

## 3.4 Marketing（マーケティング）
1. `Campaign Planner`
- 役割: キャンペーン設計、予算管理
2. `Campaign Member`
- 役割: 対象者管理と反応追跡
3. `Attribution Dashboard`
- 役割: 流入 -> 商談 -> 売上の効果測定

## 3.5 Platform / Admin（管理）
1. `Approval Inbox`
2. `Automation Rules`（Flow相当）
3. `Assignment Rules`
4. `Duplicate Rules`
5. `User/Role/Profile`
6. `Object/Field Manager`
7. `Data Import/Export`
8. `Audit Trail`
9. `Integration Hub`（API/Webhook）

## 4. データモデル方針（現DBの暫定マッピング）
`northwind` 再利用時の暫定対応:
- Lead/Account: `Customers`
- Contact: `Customers.ContactName`（将来は独立テーブル化）
- Opportunity: `Orders`
- OpportunityLineItem: `OrderDetails`
- Product/Price: `Products`
- Owner/SalesRep: `Employees`
- Case（暫定）: `Orders.Status in ('Delayed','Cancelled')`

本番運用時の拡張推奨テーブル:
- `Lead`, `Contact`, `Case`, `Contract`, `Quote`, `QuoteLine`, `TaskActivity`, `ApprovalRequest`, `SlaPolicy`, `Notification`

## 5. 実装優先順位（推奨）
### Phase 1: MVP（2-4週間）
- Lead Inbox / Opportunity Board / Opportunity Detail / Case Queue / Approval Inbox
- 目的: 営業とサポートの最低運用

### Phase 2: 運用安定化（4-8週間）
- Activity Console / Quote Builder / Forecast Console / SLA Monitor
- 目的: 売上管理と対応品質の安定化

### Phase 3: 拡張（8週間+）
- Campaign Planner / Attribution / Integration Hub / Object Manager
- 目的: 自動化と分析の高度化

## 6. ページ種別の設計原則
- `DynamicEntity`:
  - マスタメンテ/一覧検索/単票編集
- `Page (YAML)`:
  - 複数データソース統合、業務ワークベンチ
- `Custom Razor View`:
  - 高度UI（承認ボタン、ドラッグ、集約カード）

## 7. 多言語ポリシー
- 対応言語: `en-US / zh-CN / ja-JP / ko-KR`
- 原則:
  - 固定文言を `.cshtml` へ直書きしない
  - `config/i18n.yml` キー経由で解決する
  - `PageView` の `title/description/section/column` もキー化

## 8. 受け入れ基準（Definition of Done）
1. 各必須ページで検索・絞り込み・並び替えが機能する
2. 主要業務操作（assign/approve/stage update）が監査ログに残る
3. `en-US/zh-CN/ja-JP/ko-KR` で表示崩れ・未翻訳キーがない
4. 認可（Admin/一般）で表示/操作が制御される
5. `dotnet build` が成功し、主要導線が手動テスト済み

## 9. 実装完了チェック
1. 必須ページの `pages/*.yaml` は作成済み
2. 主要操作（assign/approve/stage update）は `Page` 経由で操作可能
3. 主要 i18n キー（ナビ・ホーム・ページタイトル/説明・主要列）は整備済み
4. UAT シナリオは `docs/uat-scenarios-ja.md` / `docs/uat-scenarios-zh-CN.md` を参照
