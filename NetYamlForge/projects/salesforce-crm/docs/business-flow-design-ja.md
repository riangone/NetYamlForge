# Salesforce CRM 業務フロー設計（実装版）

## 目的
- 参照中心のパネル群を、実行導線つきの業務運用へ転換する。
- ログイン直後に「自分が何を実行できるか」を明確化する。

## 権限モデル
- `Admin`: 全ページ閲覧可。承認・ユーザー管理・監査・統合管理を実行可能。
- `User`: 実行系ページ中心。Lead/Case/SLA など日次業務を実行可能。管理者専用ページは非表示/アクセス不可。

## ログイン後ランディング
- 対象プロジェクト: `salesforce-crm`
- 遷移先: `/{project}/Page/OperationWorkbench`
- 画面内容:
  - 現在ユーザーと権限
  - KPIスナップショット
  - 業務フロー別の実行ボタン（Lead→Opportunity、Quote→Order、Service Recovery）
  - 権限マトリクス（User/Adminでの可否）
  - Admin向けユーザー一覧とガバナンス導線

## 閲覧制御
以下は `Admin` のみ閲覧可能:
- `ApprovalInbox`
- `AssignmentRules`
- `DuplicateRules`
- `AutomationRules`
- `DataImportExport`
- `ObjectManager`
- `RoleAccessMatrix`
- `UserRoleProfile`
- `AuditTrail`
- `IntegrationHub`
- `WebhookDeliveryMonitor`

## 実行フロー（推奨）
### 実データ入力の開始点（重要）
- 参照系ページ（Lead/Opportunity/Dashboard）は多くが読み取り中心です。
- 初回データ入力は以下の `DynamicEntity` 作成画面から開始します。
  - 顧客作成: `/salesforce-crm/DynamicEntity/CreatePage?entity=customer`
  - 受注作成: `/salesforce-crm/DynamicEntity/CreatePage?entity=order`
  - 受注明細作成: `/salesforce-crm/DynamicEntity/CreatePage?entity=orderdetail`
- 一覧確認は以下を使用します。
  - 顧客一覧: `/salesforce-crm/DynamicEntity/Index?entity=customer`
  - 受注一覧: `/salesforce-crm/DynamicEntity/Index?entity=order`
  - 明細一覧: `/salesforce-crm/DynamicEntity/Index?entity=orderdetail`

### 業務シナリオ別フロー
1. `Lead Inbox` で対象顧客を優先度順に処理
2. `Lead Detail 360` で状況確認
3. `Opportunity Workspace` / `Quote Builder` で商談化
4. `Order Management` / `Contract Center` で受注執行
5. 遅延時は `Case Queue` / `SlaMonitor` で復旧運用
6. 管理者は `ApprovalInbox` / `UserRoleProfile` / `AuditTrail` で統制

## 補足
- 現在 DB の初期ユーザーは `admin` のみ。追加ユーザーは `Users` 画面または `User Role Profile` から運用する。

## テストユーザー（追加済み）
- 共通パスワード: `Admin@123`
- `admin` : `Admin`（既存）
- `crm_admin_ops` : `Admin`（運用管理）
- `crm_approver` : `Admin`（承認業務）
- `crm_sales_rep` : `User`（営業実行）
- `crm_service_agent` : `User`（サービス対応）
- `crm_marketing_user` : `User`（マーケ連携）
- `crm_readonly_user` : `User`（一般利用者）

## 権限対応（現行実装）
- `Admin`: 管理系ページ（Approval/UserRole/Audit/Integration 等）を含む全ページにアクセス可。
- `User`: 実行系ページ（Lead/Opportunity/Case/SLA/Activity 等）を中心にアクセス可。管理系ページはアクセス不可。

## CRMフック実装（追加済み）
- `crm_order_data_guard`
  - 受注日/希望日整合性、運賃範囲、ステータス妥当性、出荷日の自動補完、非アクティブ顧客への受注禁止
- `crm_order_status_transition`
  - `Open/Delayed/Shipped/Cancelled` の遷移制御
  - 明細なし受注の `Shipped` 遷移禁止
- `crm_order_audit_trail`
  - 受注作成・ステータス変更・削除を `AuditLog` へ記録
- `crm_order_delete_guard`
  - `Shipped/Cancelled` 受注の削除禁止
  - 明細が残る受注の削除禁止
- `crm_orderdetail_guard`
  - 数量/単価/割引の妥当性
  - 在庫超過、販売終了商品の明細追加禁止
  - `Shipped/Cancelled` 受注への明細追加更新削除禁止
- `crm_orderdetail_projection`
  - 明細作成/更新/削除後に `Orders.RelatedProductIds` を自動再計算
- `crm_orderdetail_inventory_sync`
  - 明細作成時に `Products.UnitsInStock` を減算
  - 明細更新時に旧数量との差分、または商品変更差分を在庫へ反映
  - 明細削除時に在庫を戻し込み
- `crm_customer_lifecycle_guard`
  - 顧客必須項目・トリム
  - CompanyName/Country の重複防止
  - `Open/Delayed` 受注が残る顧客の非アクティブ化禁止
- `crm_customer_delete_guard`
  - 受注履歴が残る顧客の削除禁止
- `crm_customer_audit_trail`
  - 顧客作成/更新/削除を `AuditLog` へ記録
- `crm_product_inventory_guard`
  - 商品価格/在庫/再発注レベル妥当性
  - 使用中商品の `Discontinued` 禁止
- `crm_product_delete_guard`
  - 受注明細履歴が残る商品の削除禁止
- `crm_product_audit_trail`
  - 商品作成/更新/削除を `AuditLog` へ記録

## Page操作ガード（追加済み）
- `PageController` で `Orders.Status` 更新時に遷移ルールを強制
- `Shipped` へ更新時は `ShippedDate` を自動設定
- `Shipped/Cancelled` 受注の削除禁止
- `Shipped/Cancelled` 受注に紐づく明細削除禁止
- 受注履歴がある顧客の削除禁止（`Active=0` で無効化）
- 利用履歴がある商品の削除禁止（`Discontinued=1` で販売停止）

## AuditTrail運用（追加済み）
- `AuditTrail` 画面上部に CRMイベントのクイックフィルタを追加
  - `crm_order_event` / `crm_customer_event` / `crm_product_event`
  - `hook_rejected`（業務ルール拒否イベント）
  - `page_update` / `page_delete`
  - `Entity=Orders`（受注関連ミューテーション）
- `Action + Entity` の複合プリセットを追加
  - `page_update + Orders`
  - `page_delete + Orders`
- 期間プリセットを追加
  - `CreatedAt >= 今日`
  - `CreatedAt >= 直近7日`
- 期間レンジ入力（開始日/終了日）を追加
  - `CreatedAt >= from`
  - `CreatedAt <= to`
- クイックフィルタ文言は i18n キー化（多言語表示対応）
- 削除拒否メッセージに `OrderId` / `OrderDetailId` を含め、運用調査を高速化
- `DynamicEntityController` で BeforeHook 中断時に `hook_rejected` を `AuditLog` へ記録
- `hook_rejected` の `Detail` は JSON 構造で記録
  - `operation` / `key` / `hooks` / `reason` / `reasonCode` を保持
- `AuditTrail` に `Hook Rejected Events` セクションを追加
  - JSON から `Operation/RecordKey/Hooks/Reason/ReasonCode` を抽出表示
- `AuditTrail` にユーザー別の保存済みビュー機能を追加
  - 現在フィルタを名前付きで保存
  - デフォルトビュー設定
  - 保存済みビューの適用/削除
