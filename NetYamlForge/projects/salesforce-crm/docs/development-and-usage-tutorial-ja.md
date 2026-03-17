# Salesforce CRM サブプロジェクト 開発・利用チュートリアル

最終更新日: 2026-03-05
対象: `NetYamlForge/projects/salesforce-crm`

## 1. この文書の目的
このチュートリアルは、`salesforce-crm` を以下の観点で実運用できる状態にするための手順書です。

- 開発者: 画面/権限/Hook/自動処理を拡張できる
- 運用者: ログイン後の業務フローを実行し、監査・権限管理できる
- テスター: 再現性あるシナリオで受入確認できる

## 2. アーキテクチャ概要
`salesforce-crm` は DynamicCrud 基盤上で次の要素を組み合わせています。

1. エンティティCRUD: `projects/salesforce-crm/entities/*.yml`
2. 業務ページ: `projects/salesforce-crm/pages/*.yaml`
3. カスタムUI: `projects/salesforce-crm/views/*.cshtml`
4. 業務Hook: `projects/salesforce-crm/Hooks/SalesforceCrmHooks.cs`
5. 権限制御: `AppUserRole` / `AppRolePermission` + `IPagePermissionService`
6. 自動処理: `CrmAutomationHostedService`（3分周期）
7. 監査: `AuditLog` + `AuditTrail` + `AuditMetrics`

## 3. 初期セットアップ
作業ディレクトリ: `/home/ubuntu/ws/ccc/NetYamlForge`

```bash
dotnet restore
dotnet build
dotnet run
```

起動URL（既定）:
- `http://localhost:5239`

ポート競合時:
```bash
ASPNETCORE_URLS=http://localhost:5241 dotnet run
```

## 4. ログインユーザーとロール
共通初期パスワード: `Admin@123`

- `admin` : `AdminOps`（管理者）
- `crm_admin_ops` : `AdminOps`（運用管理）
- `crm_approver` : `AdminOps`（承認管理）
- `crm_sales_rep` : `SalesRep`（営業実行）
- `crm_service_agent` : `ServiceAgent`（サポート実行）
- `crm_marketing_user` : `Marketing`（マーケ実行）
- `crm_readonly_user` : `ReadOnly`（閲覧専用）

ログイン後導線（推奨）:
- `/{project}/Page/OperationWorkbench`
- 例: `/salesforce-crm/Page/OperationWorkbench`

## 5. 権限モデル（RBAC）
### 5.1 テーブル
- `AppUserRole`: ユーザーとロールの関連
- `AppRolePermission`: `project + role + resource(page/field)` 単位の read/write

### 5.2 実装ポイント
- `PageController` の `Index/UpdateRow/DeleteRow/SaveView/DeleteView` で権限判定
- `IPagePermissionService` で `CanReadPageAsync/CanWritePageAsync/CanWriteFieldAsync` を提供
- 管理ページは `AdminOnlyPages` でも二重ガード

### 5.3 変更手順（例）
1. `RoleAccessMatrix` で現行権限を確認
2. DBで `AppRolePermission` を更新
3. 対象ユーザーで再ログインしてアクセス可否を確認

## 6. 主要業務フロー（運用）
### 6.1 Lead -> Opportunity
1. `LeadInbox` で対象リードを抽出
2. `LeadDetail360` で活動履歴を確認
3. `OpportunityWorkspace` / `OpportunityDetail` で商談更新
4. `QuoteBuilder` で見積作成

### 6.2 Order -> Service Recovery
1. `OrderManagement` で受注ステータス管理
2. 遅延/キャンセルは `CaseQueue` へ自動投影
3. `CaseDetail` / `SlaMonitor` で復旧処理

### 6.3 Governance
1. `ApprovalInbox` で承認作業
2. `UserRoleProfile` / `RoleAccessMatrix` でアクセス統制
3. `AuditTrail` / `AuditMetrics` で監査確認

## 7. Hookによる業務ルール
主なHook（`SalesforceCrmHooks.cs`）:

- 受注: `crm_order_data_guard`, `crm_order_status_transition`, `crm_order_delete_guard`
- 受注明細: `crm_orderdetail_guard`, `crm_orderdetail_projection`, `crm_orderdetail_inventory_sync`
- 顧客: `crm_customer_lifecycle_guard`, `crm_customer_delete_guard`
- 商品: `crm_product_inventory_guard`, `crm_product_delete_guard`
- 監査: `*_audit_trail` 系

補足:
- Hook拒否時は `AuditLog.Action = hook_rejected`
- `Detail` に `reasonCode` を含むJSONを保存
- 理由分類は `HookRejectReasonClassifier` で共通化

## 8. 自動処理（Flow相当）
`CrmAutomationHostedService` が3分ごとに以下を実行します（SQLite対象）。

1. `Customers` から `CrmLead` 投影
2. 遅延/キャンセル受注から `CrmCase` 投影
3. 受注から `CrmQuote` / `CrmContract` 投影
4. 長期未接触Leadへ `CrmTaskActivity` 生成
5. 高額運賃受注へ `CrmApprovalRequest` 生成
6. 実行結果を `AuditLog(automation_run)` 記録

## 9. CRM専用テーブル
`DbInitializer` が作成/seedする主なテーブル:

- `CrmLead`
- `CrmCase`
- `CrmQuote`
- `CrmContract`
- `CrmTaskActivity`
- `CrmApprovalRequest`
- `CrmSlaPolicy`
- `CrmAutomationRule`

RBAC関連:
- `AppUserRole`
- `AppRolePermission`

## 10. ページ追加の開発手順
### 10.1 YAMLページ追加
1. `projects/salesforce-crm/pages/NewPage.yaml` を作成
2. `config/layout.yml` にナビ追加
3. `config/home-page.yml` にクイック導線追加（必要時）

### 10.2 カスタムビュー追加
1. `views/NewPage.cshtml` を作成
2. YAMLに `template: NewPage` を指定
3. `PageController` の更新/削除経路を使う場合は `target_table` と `updatable_fields` を定義

### 10.3 権限追加
`AppRolePermission` に `resourceType=page` の行を追加。
必要なら `resourceType=field` で項目単位制御を追加。

## 11. テスト手順
ルート: `/home/ubuntu/ws/ccc`

```bash
dotnet build NetYamlForge/NetYamlForge.csproj
dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj
```

重点確認:
- `PagePermissionServiceTests`
- `HookRejectReasonClassifierTests`
- CRM主要導線の手動操作（Lead/Case/Order/Audit）

## 12. 運用監査
### 12.1 AuditTrail
- 主要Action: `crm_order_event`, `crm_customer_event`, `crm_product_event`, `hook_rejected`, `page_update`, `page_delete`, `automation_run`
- 日付範囲、Action、Entityで絞り込み

### 12.2 AuditMetrics
- 拒否件数
- mutation件数
- 拒否率
- 理由コード別集計
- ロール別拒否件数

## 13. トラブルシュート
1. `address already in use`
- 別ポートで起動: `ASPNETCORE_URLS=http://localhost:5241 dotnet run`

2. ログインできない
- `AppUser.IsActive` を確認
- パスワードを再初期化する場合は `PasswordHasher` 経由で更新

3. 画面が `Forbid`
- `AppUserRole` と `AppRolePermission` の対象プロジェクト名を確認
- `salesforce-crm` 以外の projectName で登録されていないか確認

4. 自動処理が動かない
- DBタイプが SQLite か確認
- `logs/app-*.log` で `CRM automation` ログを確認

## 14. リリース前チェックリスト
- `dotnet build` 成功
- `dotnet test` 成功
- 主要ユーザーでログイン確認（AdminOps/SalesRep/ServiceAgent/Marketing/ReadOnly）
- 主要業務導線の監査ログ生成確認
- `AuditMetrics` の集計表示確認
- 必要なDB（`projects/*/database/*.db`）差分がコミットされていること

## 15. 関連ドキュメント
- `docs/business-flow-design-ja.md`
- `docs/crm-implementation-status-ja.md`
- `docs/implementation-guide-ja.md`
- `docs/uat-scenarios-ja.md`
