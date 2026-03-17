# NetYamlForge フレームワーク整理・詳細チュートリアル（2026-03-04）

## 1. フレームワークの要点

NetYamlForge は、**YAML設定中心で CRUD / Dashboard / カスタムページを構築できる ASP.NET Core MVC フレームワーク**です。  
`projects/<project>/` ごとに DB・エンティティ定義・画面定義を分離し、1つの実行バイナリで複数業務を運用できます。

---

## 2. 機能特性（整理版）

### 2.1 コア機能

1. マルチプロジェクト実行
- ルート: `/{project}/...`
- プロジェクト単位で DB 接続・エンティティ・ダッシュボード・ページを切替

2. YAML駆動 CRUD
- `entities/*.yml` の定義だけで一覧/検索/作成/編集/削除
- フィルタ、フォーム、列、外部キー表示、ページングを設定可能

3. Dashboard（集計カード + グラフ）
- `dashboard.yml` で `count/sum/avg` 集計
- Chart.js グラフ（bar/line/doughnut/pie）

4. カスタム Page 機能
- `pages/*.yaml` で任意 SQL セクション画面を構築
- 業務オペレーション向けの複合ビュー（例: 出荷優先度キュー）

5. Hook 拡張
- CRUD 前後フック（before/after）
- 複数フック順次実行、プロジェクト固有フックの動的読み込み

6. 自動生成・移行
- `--scaffold-entities`: DB スキーマから YAML 生成
- `--upgrade-entity-yaml`: YAML 形式の更新補助

### 2.2 運用機能

1. 認証・認可
- Cookie 認証、`AdminOnly` ポリシー
- ユーザー管理画面（作成/編集）

2. 監査・ログ
- Serilog（コンソール + ローテートファイル）
- `AuditLog` 記録

3. 多言語
- `en-US / zh-CN / ja-JP`
- `displayNameI18n`, `labelI18n` による YAML 側 i18n

4. DB 方言対応
- SQLite / SQL Server / PostgreSQL / MySQL

5. 安全性・診断
- 起動時 YAML スキーマ検証
- DB スキーマ整合チェック
- ConfigDiagnostics 画面で有効設定差分を確認

---

## 3. ディレクトリ構造（実務で重要な部分）

```text
NetYamlForge/
  Program.cs
  config/                      # 共通定義（ベース）
  projects/
    <project>/
      project.yaml             # プロジェクト定義（DB/レイアウト/ナビ）
      entities.generated/      # 自動生成（DB由来）
      entities/                # 手編集（業務定義）
      pages/                   # カスタムページ
      dashboard.yml            # プロジェクトダッシュボード
      Hooks/                   # プロジェクト固有フック
  docs/
```

---

## 4. 詳細チュートリアル（最短導入 -> 実運用）

## 4.1 起動

```bash
cd NetYamlForge
dotnet restore
dotnet build
dotnet run
```

アクセス例:
- `http://localhost:5239/chinook/Dashboard`
- `http://localhost:5239/todo/DynamicEntity/Index?entity=task`

---

## 4.2 新規プロジェクトを作る

### Step 1: `project.yaml` を作成

例: `projects/demo-ops/project.yaml`

```yaml
name: demo-ops
displayName: Demo Operations
version: "1.0.0"
database:
  type: sqlite
  path: database/demo.db
features:
  multiLanguage: true
  userAuthentication: true
layout:
  header:
    title: Demo Operations
  navigation:
    showDashboard: true
    entities:
      - order
      - customer
```

### Step 2: YAML 自動生成

```bash
dotnet run -- --scaffold-entities --project=demo-ops
```

生成先:
- `projects/demo-ops/entities.generated/*.yml`

### Step 3: 業務定義を `entities/` に上書き

`entities.generated` は再生成前提のため、編集は `entities/` 側で行います。

---

## 4.3 エンティティ定義（実用例）

例: `order.yml` で JOIN + picker + range/date-range フィルタ

```yaml
entities:
  order:
    table: Orders
    key: OrderId
    joins:
      - type: left
        table: Customers
        alias: c
        on: Orders.CustomerId = c.CustomerId
    columns:
      OrderId: { type: int, identity: true, label: ID, sortable: true }
      CustomerName:
        type: string
        expression: c.CompanyName
        searchable: true
        sortable: true
        label: Customer
      OrderDate: { type: date, sortable: true, label: Order Date }
      Freight: { type: decimal, sortable: true, label: Freight }
    forms:
      CustomerId:
        type: int
        required: true
        foreignKey:
          entity: customer
          displayColumn: Id
          displayColumns: [CompanyName, ContactName]
          picker: true
      OrderDate: { type: date, required: true }
      Freight: { type: decimal }
    filters:
      OrderDate: { type: date-range }
      Freight: { type: range }
    paging:
      pageSize: 20
      mode: numbered
      enableCount: true
```

---

## 4.4 Dashboard 設定

例: `projects/demo-ops/dashboard.yml`

```yaml
stats:
  - label: Open Orders
    entity: order
    aggregate: count
    filter: "Status = 'Open'"

charts:
  - title: Monthly Revenue
    type: line
    entity: order
    valueAggregate: sum
    valueColumn: TotalAmount
    groupExpression: "strftime('%Y-%m', OrderDate)"
    orderBy: label
    orderDir: asc
    limit: 12
```

---

## 4.5 カスタム業務ページ（Page 機能）

例: `pages/FulfillmentQueue.yaml` のように、複数テーブル集計 SQL を 1 画面に配置できます。

最小例:

```yaml
title: 受注監視
description: 納期遅延リスクを一覧化
main_table: Orders
sections:
  - id: risk_orders
    title: 遅延リスク
    source_type: custom
    source: |
      SELECT OrderId, RequiredDate, Status
      FROM Orders
      WHERE Status IN ('Open','Delayed')
    columns: [OrderId, RequiredDate, Status]
    page_size: 30
    read_only: true
```

URL:
- `/{project}/Page/FulfillmentQueue`

---

## 4.6 Hook 追加（業務ルールの実装）

### Step 1: `projects/<project>/Hooks/*.cs` を追加

```csharp
public class ValidateOrderTotalHook : IEntityHook
{
    public string Name => "validate_order_total";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("TotalAmount", out var v) && decimal.TryParse(v?.ToString(), out var amount))
        {
            if (amount < 0) return Task.FromResult(HookResult.Abort("TotalAmount は 0 以上である必要があります。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}
```

### Step 2: YAML で関連付け（共通は presets、特殊は C#）

```yaml
hooks:
  presets:
    common_before:
      - validate_required
      - trim
  beforeCreate:
    - "@common_before"
    - validate_order_total
  beforeUpdate:
    - "@common_before"
    - validate_order_total
  afterUpdate:
    - audit_log
```

`@common_before` が YAML 側の共通ロジック、`validate_order_total` が C# 側の特殊ロジックです。

---

## 4.7 診断・トラブルシュート

1. 設定診断
- `/{project}/DynamicEntity/ConfigDiagnostics?entity=<name>`

2. 定義一覧
- `/{project}/DynamicEntity/AllDefinitions`

3. 起動時エラー
- project/entity/key 単位で YAML エラー集約表示

4. ログ
- `logs/app-YYYYMMDD.log`

---

## 5. 代表ユースケース例

1. Blog CMS
- `post` エンティティで記事 CRUD
- ステータス（draft/published/archived）管理

2. TODO 管理
- `task` + `project` の外部キー連携
- 優先度/進捗/期限で運用

3. Northwind Ops
- `order` で複雑 JOIN 一覧
- `pages/*.yaml` で運用ダッシュボード画面を追加

---

## 6. 改善すべき部分（優先度付き）

## 6.1 高優先度（短期）

1. Dashboard SQL の安全化
- 現状は `filter/groupExpression` が SQL 文字列連結中心
- 対策: 許可式ホワイトリスト、式パーサ、パラメータ化可能箇所の統一

2. 自動テストの拡充
- 現在テストは一部ユーティリティ中心
- 対策: CRUD 主経路、Hook 実行順、Dashboard クエリ、Page 描画の統合テストを追加

3. エラー UX の統一
- フック失敗/バリデーション失敗/設定不備の表示形式を統一
- 対策: 共通エラーモデルとユーザー向けメッセージ規約を導入

## 6.2 中優先度（中期）

1. 設定の型安全化
- YAML の自由度が高く、運用者依存になりやすい
- 対策: スキーマ厳格化 + 生成 CLI の補完強化

2. 可観測性強化
- クエリ時間・フック時間の比較がしづらい
- 対策: TraceId ベースで CRUD 単位の構造化ログを標準化

3. フロント状態整合
- HTMX 部分更新時の filter/sort/page 連携は回帰余地あり
- 対策: 状態パラメータビルダーを共通化し、UI 回帰テストを導入

## 6.3 低優先度（継続改善）

1. CLI の運用性
- `scaffold`/`upgrade` の出力を JSON 化し CI 連携しやすくする

2. ドキュメントの一本化
- 機能別ドキュメントが増えたため入口が分散
- 対策: 本ドキュメントを目次ハブとして維持

---

## 7. 推奨運用フロー

1. DB 変更
2. `--scaffold-entities` 実行
3. `entities/` で業務定義上書き
4. `dotnet build` + 重点画面の手動確認
5. 必要なら Hook/Page/Dashboard 追加
6. ConfigDiagnostics で差分確認

この流れを固定すると、設定駆動開発の速度と安全性を両立できます。
