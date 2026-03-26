---
name: new-project
description: Create a new NetYamlForge sub-project with YAML config, entities, dashboard, and SQLite schema. Usage: /new-project <project-name> [description]
argument-hint: <project-name> [description]
allowed-tools: Read, Write, Edit, Glob, Grep, Bash
---

# NetYamlForge 新規プロジェクト作成

あなたは NetYamlForge フレームワークのエキスパートです。
引数 `$ARGUMENTS` を元に新しいサブプロジェクトをゼロから構築してください。

## 方針

**すべての機能を網羅した「フル機能スターター」を生成します。**
プロジェクトの用途に応じたエンティティを設計しつつ、フレームワークの全機能を
参考コードとして含めることで、開発者がすぐに全機能を確認・利用できるようにします。

含めるべき機能:
- 全フォームフィールドタイプ (string / textarea / int / decimal / bool / date / datetime)
- 全フィルタータイプ (like / dropdown / multi-select / toggle-group / date-range / bool / number-range)
- テーブル結合 (joins) と外部キー参照 (foreignKey)
- カスタムアクション (row スコープ + header スコープ + ファイルアップロード)
- 全エクスポート形式 (CSV / JSON / PDF)
- 組み込みフック (normalize_title / validate_due_date / audit_log / set_completed_at)
- カスタムアクションハンドラー C# (Hooks/ ディレクトリ)
- バッチジョブ (sql_to_csv)
- ダッシュボード (stats + 複数チャート)
- SQLite テストデータ (database/init_seed.sql)
- フォーム・フィルターレイアウト設定
- links (エンティティ間ナビゲーション)
- paging 設定

---

## 入力解析

- **プロジェクト名**: `$ARGUMENTS[0]` (英小文字・数字・ハイフン。例: `shop`, `hr-system`)
- **説明**: `$ARGUMENTS[1]` 以降（省略可）

引数が空の場合はユーザーに確認してください。

---

## 作業手順

### Step 1 — ディレクトリ構造の作成

```bash
mkdir -p NetYamlForge/projects/<project-name>/{config,database,entities,Hooks,jobs/sql,jobs/output,exports/sql,pages}
```

作成するディレクトリ:
```
NetYamlForge/projects/<project-name>/
├── project.yaml
├── dashboard.yml
├── config/
│   ├── layout.yml    ← 不要（project.yaml に統合）
│   └── i18n.yml
├── database/
│   └── init_seed.sql   ← SQLite スキーマ + テストデータ（冪等 SQL）
├── entities/
│   ├── <main-entity>.yml    ← 全機能デモ（メインエンティティ）
│   └── <sub-entity>.yml     ← 外部キー参照元（カテゴリ/プロジェクトなど）
├── Hooks/
│   └── <ProjectName>Handlers.cs   ← カスタムアクションハンドラー
├── jobs/
│   ├── <job-name>.yml
│   └── sql/
│       └── <job-name>.sql
└── exports/
    └── sql/
        └── <export-name>.sql
```

### Step 2 — project.yaml の作成

```yaml
name: <project-name>
displayName: "<表示名>"
description: "<説明>"
version: "1.0.0"

aiHints:
  primaryLanguage: ja-JP
  notes:
    - "<プロジェクト固有の補足>"

database:
  type: sqlite
  path: database/<project-name>.db

features:
  multiLanguage: true
  userAuthentication: true
  dashboard: true
  pages: false

layout:
  dashboardTheme: workspace
  header:
    title: "<ヘッダータイトル>"
  navigation:
    showDashboard: true
    entities:
      - <main-entity>
      - <sub-entity>
    items:
      - label: ダッシュボード
        controller: Dashboard
        action: Index
        icon: 📊
        section: Overview
      - label: <メインエンティティ表示名>
        url: /<project-name>/DynamicEntity/Index?entity=<main-entity>
        icon: <emoji>
        section: <セクション名>
      - label: <サブエンティティ表示名>
        url: /<project-name>/DynamicEntity/Index?entity=<sub-entity>
        icon: <emoji>
        section: <セクション名>

home_page:
  icon: "<emoji>"
  tagline: "<キャッチコピー>"
  tags: [<tag1>, <tag2>]
```

### Step 3 — メインエンティティ YAML（全機能デモ）

`entities/<main-entity>.yml` は以下の全機能を含めます。

```yaml
imports: []
entities:
  <main-entity>:
    table: <MainTable>
    key: Id
    displayName: <表示名>
    softDelete: false
    isPublic: false

    # ── テーブル結合 ──────────────────────────────────────────
    joins:
      - type: left
        table: <SubTable>
        alias: sub
        on: <MainTable>.<ForeignKeyColumn> = sub.Id

    # ── フォーム（全フィールドタイプのデモ） ──────────────────
    forms:
      Title:
        type: string
        required: true
        label: タイトル
        editable: true
      Description:
        type: textarea
        label: 説明
        editable: true
      Status:
        type: string
        required: true
        label: ステータス
        editable: true
        options:
          - pending
          - in_progress
          - done
          - cancelled
      Priority:
        type: string
        required: true
        label: 優先度
        editable: true
        options:
          - low
          - medium
          - high
          - urgent
      <ForeignKeyColumn>:
        type: int
        label: <サブエンティティ表示名>
        editable: true
        foreignKey:
          entity: <sub-entity>
          displayColumn: Name
          picker: false
          multiPicker: false
      DueDate:
        type: date
        label: 期限日
        editable: true
      Amount:
        type: decimal
        label: 金額
        editable: true
        precision: 2
      Quantity:
        type: int
        label: 数量
        editable: true
      IsActive:
        type: bool
        label: 有効
        editable: true
      AssignedTo:
        type: string
        label: 担当者
        editable: true
      Tags:
        type: string
        label: タグ
        editable: true
      CompletedAt:
        type: date
        label: 完了日
        editable: true

    # ── 一覧表示列 ────────────────────────────────────────────
    columns:
      Id:
        type: int
        identity: true
        label: ID
        sortable: true
      Title:
        type: string
        required: true
        label: タイトル
        searchable: true
        sortable: true
      Status:
        type: string
        required: true
        label: ステータス
        sortable: true
        optionLabels:
          pending: "📋 待機中"
          in_progress: "🔄 進行中"
          done: "✅ 完了"
          cancelled: "❌ キャンセル"
      Priority:
        type: string
        required: true
        label: 優先度
        sortable: true
      <ForeignKeyColumn>:
        type: int
        label: <FK表示名> ID
        hidden: true
      <SubDisplayColumn>:
        type: string
        label: <サブエンティティ表示名>
        expression: sub.Name
        searchable: true
        sortable: true
      DueDate:
        type: date
        label: 期限日
        sortable: true
      Amount:
        type: decimal
        label: 金額
        sortable: true
        precision: 2
      Quantity:
        type: int
        label: 数量
        sortable: true
      IsActive:
        type: bool
        label: 有効
        sortable: true
      AssignedTo:
        type: string
        label: 担当者
        searchable: true
        sortable: true
      Tags:
        type: string
        label: タグ
        searchable: true
      CompletedAt:
        type: date
        label: 完了日
        sortable: true

    # ── フィルター（全タイプのデモ） ─────────────────────────
    filters:
      Status:
        type: toggle-group
        label: ステータス
        optionLabels:
          pending: "📋 待機中"
          in_progress: "🔄 進行中"
          done: "✅ 完了"
          cancelled: "❌ キャンセル"
      Priority:
        type: dropdown
        label: 優先度
        options:
          - low
          - medium
          - high
          - urgent
      <ForeignKeyColumn>:
        type: dropdown
        label: <サブエンティティ表示名>
        expression: <MainTable>.<ForeignKeyColumn>
        foreignKey:
          entity: <sub-entity>
          displayColumn: Name
      AssignedTo:
        type: like
        label: 担当者
      DueDate:
        type: date-range
        label: 期限日
        expression: <MainTable>.DueDate
      IsActive:
        type: bool
        label: 有効のみ
      Amount:
        type: number-range
        label: 金額範囲

    # ── 組み込みフック ────────────────────────────────────────
    hooks:
      beforeCreate:
        - normalize_title
        - validate_due_date
      afterCreate:
        - audit_log
      beforeUpdate:
        - normalize_title
        - validate_due_date
        - set_completed_at
      afterUpdate:
        - audit_log

    # ── エンティティ間ナビゲーション ─────────────────────────
    links:
      <sub-entity>:
        label: <サブエンティティ表示名>
        targetEntity: <sub-entity>
        filter:
          Id: <ForeignKeyColumn>

    # ── カスタムアクション（row + header + ファイルアップロード）
    actions:
      mark_done:
        label: "完了にする"
        scope: row
        confirm: "このレコードを完了にしますか？"
        handler: <project_name>_mark_done
      reopen:
        label: "再オープン"
        scope: row
        handler: <project_name>_reopen
        inputs:
          - name: Reason
            type: string
            label: 再オープン理由
            required: true
      bulk_cancel:
        label: "期限切れを一括キャンセル"
        scope: header
        confirm: "期限切れのレコードをすべてキャンセルにしますか？"
        handler: <project_name>_bulk_cancel
      import_csv:
        label: "CSV インポート"
        scope: header
        handler: <project_name>_import_csv
        inputs:
          - name: CsvFile
            type: file
            label: CSV ファイル
            required: true
            allowedExtensions: ".csv"
            maxSizeBytes: 5242880

    # ── エクスポート（CSV / JSON / PDF 全形式のデモ） ─────────
    exports:
      filtered_csv:
        label: "フィルタ結果 CSV"
        format: csv
        filename: "<main-entity>_{date:yyyyMMdd}.csv"
        columns:
          - Title
          - Status
          - Priority
          - DueDate
          - AssignedTo
          - Amount

      all_json:
        label: "全件 JSON"
        format: json
        filename: "<main-entity>_{date:yyyyMMdd}.json"

      report_pdf:
        label: "PDF レポート"
        format: pdf
        filename: "<main-entity>_{date:yyyyMMdd}.pdf"
        columns:
          - Title
          - Status
          - Priority
          - DueDate
          - AssignedTo
          - Amount
        pdf:
          title: "<メインエンティティ表示名>一覧レポート"
          pageSize: A4
          orientation: landscape
          headerColor: "#1E3A5F"
          oddRowColor: "#F0F4F8"
          showPageNumbers: true
          showGeneratedAt: true
          columns:
            - key: Title
              width: 30
            - key: Status
              width: 14
              align: center
            - key: Priority
              width: 12
              align: center
            - key: DueDate
              width: 14
              align: center
            - key: AssignedTo
              width: 16
            - key: Amount
              width: 14
              align: right

      overdue_pdf:
        label: "期限切れ PDF"
        format: pdf
        filename: "<main-entity>_overdue_{date:yyyyMMdd}.pdf"
        sqlFile: exports/sql/overdue.sql
        pdf:
          title: "期限切れ一覧"
          pageSize: A4
          orientation: portrait
          headerColor: "#B91C1C"
          oddRowColor: "#FEF2F2"
          showPageNumbers: true
          showGeneratedAt: true

    # ── フォーム・フィルターレイアウト ────────────────────────
    layout:
      forms:
        columns: 2
        order:
          - Title
          - Status
          - Priority
          - <ForeignKeyColumn>
          - AssignedTo
          - DueDate
          - Amount
          - Quantity
          - IsActive
          - Tags
          - Description
          - CompletedAt
      filters:
        columns: 4
        order:
          - Status
          - Priority
          - <ForeignKeyColumn>
          - AssignedTo
          - DueDate
          - IsActive
          - Amount

    # ── ページング ────────────────────────────────────────────
    paging:
      pageSize: 20
      mode: numbered
      enableCount: true
```

### Step 4 — サブエンティティ YAML

`entities/<sub-entity>.yml` はシンプルなマスターデータエンティティ:

```yaml
imports: []
entities:
  <sub-entity>:
    table: <SubTable>
    key: Id
    displayName: <表示名>
    softDelete: false
    isPublic: false

    forms:
      Name:
        type: string
        required: true
        label: 名前
        editable: true
      Description:
        type: textarea
        label: 説明
        editable: true
      SortOrder:
        type: int
        label: 表示順
        editable: true

    columns:
      Id:
        type: int
        identity: true
        label: ID
        sortable: true
      Name:
        type: string
        required: true
        label: 名前
        searchable: true
        sortable: true
      Description:
        type: string
        label: 説明
        searchable: true
      SortOrder:
        type: int
        label: 表示順
        sortable: true

    filters:
      Name:
        type: like
        label: 名前

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true
```

### Step 5 — dashboard.yml の作成

```yaml
stats:
  - label: 全件数
    entity: <main-entity>
    aggregate: count
    icon: 📋
    color: badge-primary
  - label: 進行中
    entity: <main-entity>
    aggregate: count
    filter: Status='in_progress'
    icon: 🔄
    color: badge-info
  - label: 完了
    entity: <main-entity>
    aggregate: count
    filter: Status='done'
    icon: ✅
    color: badge-success
  - label: 期限切れ
    entity: <main-entity>
    aggregate: count
    filter: "DueDate < date('now') AND Status NOT IN ('done','cancelled')"
    icon: ⚠️
    color: badge-error
  - label: 合計金額
    entity: <main-entity>
    aggregate: sum
    column: Amount
    icon: 💰
    color: badge-accent
  - label: <サブエンティティ表示名>数
    entity: <sub-entity>
    aggregate: count
    icon: 📂
    color: badge-secondary

charts:
  - title: ステータス別分布
    type: doughnut
    entity: <main-entity>
    valueAggregate: count
    groupExpression: Status
    orderBy: value
    orderDir: desc
    limit: 10
    colors:
      - rgba(245, 158, 11, 0.85)
      - rgba(59, 130, 246, 0.85)
      - rgba(16, 185, 129, 0.85)
      - rgba(107, 114, 128, 0.85)

  - title: 優先度別件数
    type: bar
    entity: <main-entity>
    valueAggregate: count
    groupExpression: Priority
    orderBy: value
    orderDir: desc
    limit: 10
    colorBg: rgba(99, 102, 241, 0.7)
    colorBorder: rgba(99, 102, 241, 1)

  - title: <サブエンティティ>別件数
    type: bar
    entity: <main-entity>
    valueAggregate: count
    labelJoinEntity: <sub-entity>
    labelJoinKey: <ForeignKeyColumn>
    labelJoinDisplay: Name
    orderBy: value
    orderDir: desc
    limit: 10
    colorBg: rgba(16, 185, 129, 0.7)
    colorBorder: rgba(16, 185, 129, 1)

  - title: 月別件数推移
    type: line
    entity: <main-entity>
    valueAggregate: count
    groupExpression: "strftime('%Y-%m', CreatedAt)"
    orderBy: label
    orderDir: asc
    limit: 12
    colorBg: rgba(59, 130, 246, 0.2)
    colorBorder: rgba(59, 130, 246, 1)

recent:
  - label: 最近の<メインエンティティ表示名>
    entity: <main-entity>
    limit: 5
    sort: CreatedAt
    dir: desc
```

### Step 6 — カスタムアクションハンドラー C# の作成

`Hooks/<ProjectName>Handlers.cs` を作成します。
**namespace は必ず `NetYamlForge.Projects.<PascalCaseProjectName>.Hooks;` にすること。**

```csharp
// <project-name> プロジェクト カスタムアクションハンドラー
//
// YAML での参照例:
//   actions:
//     mark_done:
//       handler: <project_name>_mark_done
//     reopen:
//       handler: <project_name>_reopen
//     bulk_cancel:
//       handler: <project_name>_bulk_cancel
//     import_csv:
//       handler: <project_name>_import_csv

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.<PascalCaseProjectName>.Hooks;

/// <summary>
/// レコードを「完了」ステータスに更新するアクションハンドラー。
/// YAML: handler = "<project_name>_mark_done"（scope: row）
/// </summary>
public class <PascalCase>MarkDoneHandler : ICustomActionHandler
{
    public string Name => "<project_name>_mark_done";

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("レコード ID が指定されていません。");
        if (!int.TryParse(ctx.RecordId, out var id))
            return ActionHandlerResult.Failure("無効なレコード ID です。");

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var affected = await db.ExecuteAsync(
            "UPDATE <MainTable> SET Status = 'done', CompletedAt = @today WHERE Id = @id",
            new { today, id }, tx);

        return affected <= 0
            ? ActionHandlerResult.Failure("対象レコードが見つかりません。")
            : ActionHandlerResult.Success();
    }
}

/// <summary>
/// レコードを「pending」ステータスに戻すアクションハンドラー。
/// YAML: handler = "<project_name>_reopen"（scope: row）
/// inputs: Reason（必須）
/// </summary>
public class <PascalCase>ReopenHandler : ICustomActionHandler
{
    public string Name => "<project_name>_reopen";

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("レコード ID が指定されていません。");
        if (!int.TryParse(ctx.RecordId, out var id))
            return ActionHandlerResult.Failure("無効なレコード ID です。");

        var reason = ctx.Inputs.TryGetValue("Reason", out var r) ? r?.ToString() : null;
        if (string.IsNullOrWhiteSpace(reason))
            return ActionHandlerResult.Failure("再オープン理由を入力してください。");

        var note = $"[再オープン: {reason}]";
        var affected = await db.ExecuteAsync(
            @"UPDATE <MainTable>
              SET Status = 'pending',
                  CompletedAt = NULL,
                  Description = CASE WHEN Description IS NULL OR Description = ''
                                     THEN @note
                                     ELSE Description || ' ' || @note END
              WHERE Id = @id",
            new { note, id }, tx);

        return affected <= 0
            ? ActionHandlerResult.Failure("対象レコードが見つかりません。")
            : ActionHandlerResult.Success();
    }
}

/// <summary>
/// 期限切れのレコードをすべてキャンセルにするヘッダーアクションハンドラー。
/// YAML: handler = "<project_name>_bulk_cancel"（scope: header）
/// </summary>
public class <PascalCase>BulkCancelHandler : ICustomActionHandler
{
    public string Name => "<project_name>_bulk_cancel";

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        await db.ExecuteAsync(
            @"UPDATE <MainTable>
              SET Status = 'cancelled'
              WHERE DueDate < @today
                AND Status NOT IN ('done', 'cancelled')",
            new { today }, tx);
        return ActionHandlerResult.Success();
    }
}

/// <summary>
/// CSV ファイルをアップロードしてレコードを一括インポートするハンドラー。
/// YAML: handler = "<project_name>_import_csv"（scope: header）
/// CSV フォーマット（1 行目ヘッダー）: Title,Priority,DueDate,AssignedTo
/// </summary>
public class <PascalCase>ImportCsvHandler : ICustomActionHandler
{
    public string Name => "<project_name>_import_csv";

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Files.TryGetValue("CsvFile", out var csvPath) || string.IsNullOrWhiteSpace(csvPath))
            return ActionHandlerResult.Failure("CSV ファイルが見つかりません。");

        var lines = await File.ReadAllLinesAsync(csvPath);
        if (lines.Length < 2)
            return ActionHandlerResult.Failure("CSV にデータ行がありません。");

        var headers = lines[0].Split(',');
        int colTitle    = Array.IndexOf(headers, "Title");
        int colPriority = Array.IndexOf(headers, "Priority");
        int colDueDate  = Array.IndexOf(headers, "DueDate");
        int colAssigned = Array.IndexOf(headers, "AssignedTo");

        if (colTitle < 0)
            return ActionHandlerResult.Failure("CSV に 'Title' 列が見つかりません。");

        int inserted = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = lines[i].Split(',');
            var title = colTitle < cells.Length ? cells[colTitle].Trim() : "";
            if (string.IsNullOrWhiteSpace(title)) continue;

            await db.ExecuteAsync(
                @"INSERT INTO <MainTable> (Title, Status, Priority, DueDate, AssignedTo, IsActive, CreatedAt, UpdatedAt)
                  VALUES (@title, 'pending', @priority, @dueDate, @assignedTo, 1, @now, @now)",
                new
                {
                    title,
                    priority  = colPriority >= 0 && colPriority < cells.Length ? cells[colPriority].Trim() : "medium",
                    dueDate   = colDueDate  >= 0 && colDueDate  < cells.Length ? (object?)cells[colDueDate].Trim()  : null,
                    assignedTo = colAssigned >= 0 && colAssigned < cells.Length ? (object?)cells[colAssigned].Trim() : null,
                    now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }, tx);
            inserted++;
        }

        return inserted == 0
            ? ActionHandlerResult.Failure("インポートできる行がありませんでした。")
            : ActionHandlerResult.Success();
    }
}
```

### Step 7 — バッチジョブの作成

`jobs/overdue_report.yml`:
```yaml
jobs:
  overdue_report:
    displayName: 期限切れレポート
    description: "期限を過ぎた未完了レコードを CSV に出力します。毎日 08:00 (JST) に実行。"
    enabled: true

    schedule:
      cron: "0 8 * * *"
      timezone: "Asia/Tokyo"

    type: sql_to_csv
    settings:
      sqlFile: jobs/sql/overdue_report.sql
      outputFile: "jobs/output/overdue_{date:yyyyMMdd}.csv"
      includeHeader: true
      delimiter: ","

    onFailure:
      retryCount: 1
      retryInterval: 300
      logError: true
```

`jobs/sql/overdue_report.sql`:
```sql
SELECT
    Id,
    Title,
    Status,
    Priority,
    DueDate,
    AssignedTo,
    CAST(julianday('now') - julianday(DueDate) AS INTEGER) AS OverdueDays
FROM <MainTable>
WHERE DueDate < date('now')
  AND Status NOT IN ('done', 'cancelled')
ORDER BY DueDate ASC;
```

`exports/sql/overdue.sql` (エクスポート用):
```sql
SELECT
    t.Id,
    t.Title,
    t.Status,
    t.Priority,
    t.DueDate,
    t.AssignedTo,
    t.Amount,
    CAST(julianday('now') - julianday(t.DueDate) AS INTEGER) AS OverdueDays
FROM <MainTable> t
WHERE t.DueDate < date('now')
  AND t.Status NOT IN ('done', 'cancelled')
ORDER BY t.DueDate ASC;
```

### Step 8 — SQLite テストデータ (init_seed.sql) の作成

**重要**: このファイルは起動時に自動実行されます（`ProjectSpecificInitializer` の汎用フォールバック）。
全 SQL 文は冪等に書いてください（`CREATE TABLE IF NOT EXISTS` + `INSERT OR IGNORE`）。

`database/init_seed.sql` テンプレート:

```sql
-- <project-name> 初期スキーマ + テストデータ
-- すべての文は冪等（CREATE TABLE IF NOT EXISTS / INSERT OR IGNORE）
-- 起動時に ProjectSpecificInitializer から自動実行されます

-- ── サブテーブル ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS <SubTable> (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT    NOT NULL,
    Description TEXT,
    SortOrder   INTEGER NOT NULL DEFAULT 0,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedAt   TEXT    NOT NULL DEFAULT (datetime('now'))
);

-- ── メインテーブル ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS <MainTable> (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Title           TEXT    NOT NULL,
    Description     TEXT,
    Status          TEXT    NOT NULL DEFAULT 'pending',
    Priority        TEXT    NOT NULL DEFAULT 'medium',
    <ForeignKeyColumn> INTEGER,
    DueDate         TEXT,
    Amount          REAL    NOT NULL DEFAULT 0,
    Quantity        INTEGER NOT NULL DEFAULT 1,
    IsActive        INTEGER NOT NULL DEFAULT 1,
    AssignedTo      TEXT,
    Tags            TEXT,
    CompletedAt     TEXT,
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (<ForeignKeyColumn>) REFERENCES <SubTable>(Id)
);

-- ── サブテーブル テストデータ ──────────────────────────────────
INSERT OR IGNORE INTO <SubTable> (Id, Name, Description, SortOrder) VALUES
(1, '<SubItem1>', '<SubItem1説明>', 10),
(2, '<SubItem2>', '<SubItem2説明>', 20),
(3, '<SubItem3>', '<SubItem3説明>', 30),
(4, '<SubItem4>', '<SubItem4説明>', 40);

-- ── メインテーブル テストデータ ───────────────────────────────
-- 様々なステータス・優先度・日付のデータを含む
INSERT OR IGNORE INTO <MainTable>
    (Id, Title, Description, Status, Priority, <ForeignKeyColumn>, DueDate, Amount, Quantity, IsActive, AssignedTo, Tags)
VALUES
(1,  'サンプルA',     '説明A。機能確認用サンプルデータ。', 'pending',     'high',   1, date('now', '+7 days'),  15000.00, 2, 1, 'alice',   'サンプル,テスト'),
(2,  'サンプルB',     '説明B。',                           'in_progress', 'medium', 1, date('now', '+3 days'),  8500.50,  1, 1, 'bob',     'サンプル'),
(3,  'サンプルC',     '説明C。',                           'done',        'low',    2, date('now', '-5 days'),  3200.00,  3, 1, 'alice',   '完了,アーカイブ'),
(4,  'サンプルD',     '説明D。',                           'in_progress', 'urgent', 2, date('now', '+1 days'),  42000.00, 1, 1, 'charlie', '緊急'),
(5,  'サンプルE',     '説明E。',                           'cancelled',   'medium', 3, date('now', '-10 days'), 6750.00,  2, 0, 'bob',     'キャンセル'),
(6,  'サンプルF',     '説明F。',                           'pending',     'high',   3, date('now', '+14 days'), 22500.00, 4, 1, 'alice',   'サンプル'),
(7,  'サンプルG',     '説明G。',                           'done',        'medium', 4, date('now', '-2 days'),  9800.00,  1, 1, 'charlie', '完了'),
(8,  'サンプルH',     '説明H。',                           'in_progress', 'high',   4, date('now', '+5 days'),  35000.00, 2, 1, 'bob',     'サンプル,重要'),
(9,  '期限切れサンプル1', '期限切れフィルター確認用。',    'pending',     'urgent', 1, date('now', '-3 days'),  12000.00, 1, 1, 'alice',   '期限切れ'),
(10, '期限切れサンプル2', '期限切れフィルター確認用2。',   'in_progress', 'high',   2, date('now', '-7 days'),  18000.00, 2, 1, 'charlie', '期限切れ,緊急');
```

実際のプロジェクト内容に合わせてテーブル名・列名・データを適切に変更してください。

### Step 9 — ビルド確認

```bash
dotnet build NetYamlForge/NetYamlForge.csproj 2>&1 | tail -12
```

エラーが出た場合は修正してから再確認してください。

---

## 実装チェックリスト

- [ ] `project.yaml` が作成されている
- [ ] `dashboard.yml` が作成されている（stats + charts + recent）
- [ ] メインエンティティ `.yml` が全機能（joins / forms / columns / filters / hooks / links / actions / exports / layout / paging）を含む
- [ ] サブエンティティ `.yml` が作成されている
- [ ] `Hooks/<ProjectName>Handlers.cs` が作成されている（4ハンドラー）
- [ ] `jobs/overdue_report.yml` と `jobs/sql/overdue_report.sql` が作成されている
- [ ] `exports/sql/overdue.sql` が作成されている
- [ ] `database/init_seed.sql` が冪等な SQL で作成されている（CREATE TABLE IF NOT EXISTS + INSERT OR IGNORE）
- [ ] `database/` ディレクトリが存在する
- [ ] `project.yaml` の `navigation.entities` にすべてのエンティティが含まれている
- [ ] `dotnet build` が成功する

---

## 既存プロジェクトの参照

迷った場合は以下を参考にしてください。

```
NetYamlForge/projects/todo-app/
├── project.yaml              ← navigation, features, layout の例
├── dashboard.yml             ← stats, charts の例
├── entities/task.yml         ← joins, toggle-group, hooks, actions, exports, layout の全機能例
├── entities/project.yml      ← 外部キーの例
├── Hooks/TaskActionHandlers.cs  ← カスタムアクションの例
└── database/init_seed.sql    ← 冪等 SQLite 初期化の例

NetYamlForge/projects/biz-docs/
├── entities/jp_invoice.yml   ← pdfTemplate, date 型, precision の例
└── pdf-templates/invoice.yaml  ← 帳票テンプレートの例
```

---

## 出力形式

作業完了後、以下を報告してください。

1. 作成したファイルの一覧（ツリー形式）
2. 定義したエンティティとその主要機能
3. テストデータのサマリ（テーブル名・件数）
4. アクセス URL: `http://localhost:5000/<project-name>/DynamicEntity/Index?entity=<entity>`
5. ビルド結果
