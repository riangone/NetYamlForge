# Salesforce CRM 実装ステータス（詳細）

最終更新日: 2026-03-05

## 1. 目的と到達点
本プロジェクトは、参照中心のCRM画面構成から、業務実行・統制・監査まで含む運用可能なCRMへ拡張することを目的に実装を進めた。
現時点で以下を満たす。

- ログイン後に業務導線が明確なオペレーションハブを提供
- 権限（Admin + ロール別RBAC）ごとに操作可能領域を制御
- 受注・受注明細・顧客・商品の業務ルールを Hook で強制
- 在庫同期・関連商品投影などの後処理を自動化
- 監査ログを実操作レベルで蓄積し、AuditTrail画面で可視化
- フック拒否（業務ルール違反）イベントを構造化ログとして追跡可能
- CRM専用テーブル（Lead/Case/Quote/Contract/Task/Approval/SLA/Automation）をSQLite初期化で自動整備
- Flow相当のバックグラウンド自動化（3分周期）を実装
- 監査KPI（拒否率・理由内訳・ロール別拒否）を `AuditMetrics` で可視化

## 2. 認証・権限・導線

### 2.1 テストユーザー
共通パスワード: `Admin@123`

- `admin`（Admin）
- `crm_admin_ops`（Admin）
- `crm_approver`（Admin）
- `crm_sales_rep`（User）
- `crm_service_agent`（User）
- `crm_marketing_user`（User）
- `crm_readonly_user`（User）

### 2.2 権限制御
- Admin: 管理系ページ（Approval/UserRole/Audit/Integration等）を含む全体アクセス
- RBAC: `AppUserRole` / `AppRolePermission` により page/field 単位アクセス制御を実施
- 代表ロール:
  - `AdminOps`: 全ページ read/write
  - `SalesRep`: Sales系ページ中心に read/write
  - `ServiceAgent`: Service系ページ中心に read/write
  - `Marketing`: Marketing系ページ中心に read/write
  - `ReadOnly`: 全ページ read only

### 2.3 ログイン後導線
- `OperationWorkbench` を提供し、役割別に次アクションと実行リンクを明示

## 3. 業務ルール（Hook）

### 3.1 受注（Order）
- `crm_order_data_guard`
  - 日付整合（RequiredDate >= OrderDate）
  - Freight範囲チェック
  - Status妥当性チェック
  - ShippedDate自動補完
  - 非アクティブ顧客への受注禁止
- `crm_order_status_transition`
  - ステータス遷移制御（Open/Delayed/Shipped/Cancelled）
  - 明細なし受注のShipped禁止
- `crm_order_delete_guard`
  - Shipped/Cancelled受注の削除禁止
  - 明細残存受注の削除禁止
- `crm_order_audit_trail`
  - 作成/更新/削除を監査記録

### 3.2 受注明細（OrderDetail）
- `crm_orderdetail_guard`
  - 数量/単価/割引チェック
  - 在庫超過禁止
  - 販売終了商品の利用禁止
  - Shipped/Cancelled配下明細の追加/更新/削除禁止
- `crm_orderdetail_projection`
  - 明細変更後に `Orders.RelatedProductIds` を再計算
- `crm_orderdetail_inventory_sync`
  - 作成時在庫減算
  - 更新時差分在庫反映（商品変更含む）
  - 削除時在庫戻し

### 3.3 顧客（Customer）
- `crm_customer_lifecycle_guard`
  - 必須項目・トリム
  - CompanyName/Country重複防止
  - Open/Delayed受注残存時の非アクティブ化禁止
- `crm_customer_delete_guard`
  - 受注履歴がある顧客の削除禁止
- `crm_customer_audit_trail`
  - 作成/更新/削除を監査記録

### 3.4 商品（Product）
- `crm_product_inventory_guard`
  - 価格/在庫/再発注レベルの妥当性
  - 使用中商品のDiscontinued禁止
- `crm_product_delete_guard`
  - 受注明細履歴がある商品の削除禁止
- `crm_product_audit_trail`
  - 作成/更新/削除を監査記録

## 4. PageController側の操作ガード
画面直接更新経路でも業務ルールを維持するため、ページ操作にも検証を実装。

- Order status 更新時の遷移ルール強制
- Shipped化時の明細存在チェック
- Order/OrderDetail/Customer/Product削除ガード
- 違反時メッセージに `OrderId` / `OrderDetailId` / 件数情報を含め、運用調査性を向上

## 5. AuditTrail強化

### 5.1 クイックフィルタ
- イベント種別: `crm_order_event` / `crm_customer_event` / `crm_product_event` / `hook_rejected` / `page_update` / `page_delete`
- 複合プリセット: `page_update + Orders`, `page_delete + Orders`
- 期間プリセット: 今日 / 直近7日 / 直近30日
- 期間レンジ: 開始日・終了日（`CreatedAt` の下限/上限）

### 5.2 Hook拒否の可視化
- `hook_rejected` を JSON 構造で記録
- 専用セクション `Hook Rejected Events` で以下を抽出表示
  - Operation
  - RecordKey
  - HookNames
  - Reason
  - ReasonCode

### 5.3 保存済みビュー
- `AuditTrail` にユーザー別 `Saved Views` を実装
  - 現在のフィルタ状態（Action/Entity/CreatedAt範囲）を保存
  - デフォルトビュー指定
  - 保存済みビューの適用・削除
- 永続化テーブル: `AppUserSavedView`
  - `ProjectName, PageName, UserName, ViewName` で一意
  - `FiltersJson` にクエリ条件を保持

## 6. 実装ファイル（主要）
- `Controllers/DynamicEntityController.cs`
- `Controllers/PageController.cs`
- `projects/salesforce-crm/Hooks/SalesforceCrmHooks.cs`
- `projects/salesforce-crm/entities/order.yml`
- `projects/salesforce-crm/entities/orderdetail.yml`
- `projects/salesforce-crm/entities/customer.yml`
- `projects/salesforce-crm/entities/product.yml`
- `projects/salesforce-crm/pages/AuditTrail.yaml`
- `projects/salesforce-crm/views/AuditTrail.cshtml`
- `Data/DbInitializer.cs`（`AppUserSavedView` テーブル追加）
- `projects/salesforce-crm/views/OperationWorkbench.cshtml`
- `projects/salesforce-crm/pages/OperationWorkbench.yaml`
- `projects/salesforce-crm/config/layout.yml`
- `projects/salesforce-crm/config/home-page.yml`
- `projects/salesforce-crm/config/i18n.yml`

## 7. 検証結果
- `dotnet build` 成功（0 errors / 0 warnings）
- 起動時に `salesforce-crm` のプロジェクトHookが読み込まれることを確認
- 監査系UIでクイックフィルタと期間フィルタが有効なことを確認

## 8. 既知の今後改善候補
- SQL Server/PostgreSQL/MySQL 向けに CRM専用テーブル群の初期化を拡張（現状はSQLite中心）
- `CrmAutomationHostedService` のルール駆動化（`CrmAutomationRule` から動的実行）
- 主要業務フロー（Lead->Opportunity->Quote->Order、Case->SLA）の自動E2Eテスト整備
- `AppRolePermission` の管理UI（登録/変更/監査）を画面化
