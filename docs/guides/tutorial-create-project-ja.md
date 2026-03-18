# チュートリアル: CLI でサブプロジェクトを一から作成する

このチュートリアルでは **「タスク管理システム（task-tracker）」** を例に、
フレームワークの主要機能を CLI で一から構築する手順を説明します。

---

## 作成するもの

| 機能 | 内容 |
|------|------|
| エンティティ | Category（カテゴリ）/ Task（タスク）/ Comment（コメント） |
| ダッシュボード | 統計カード 4 枚・チャート 2 枚 |
| カスタムページ | 期限切れタスク一覧 |
| 組み込みフック | バリデーション・自動タイムスタンプ |
| カスタムフック | タスク完了時刻の自動セット |

---

## Step 1: プロジェクトを初期化する

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --init-project \
  --project=task-tracker \
  --display-name="タスク管理" \
  --db-type=sqlite \
  --db-path=database/task-tracker.db
```

実行後、以下のディレクトリ構造が自動生成されます。

```
projects/task-tracker/
├── project.yaml          # プロジェクト設定
├── config/
│   ├── dashboard.yml     # ダッシュボード統計設定
│   ├── layout.yml        # ナビゲーション設定
│   └── i18n.yml          # 多言語ラベル
├── entities/             # エンティティ YAML（ここにファイルを追加）
├── pages/                # カスタムページ YAML
├── Hooks/                # プロジェクト固有フック
├── database/             # SQLite DB ファイル
└── views/                # プロジェクト固有ビュー
```

### 生成された `project.yaml` の確認

```yaml
name: task-tracker
displayName: タスク管理
version: "1.0.0"
database:
  type: sqlite
  path: database/task-tracker.db
features:
  multiLanguage: false
  userAuthentication: true
```

---

## Step 2: データベースを作成する

`projects/task-tracker/database/init.sql` を作成し、テーブルを定義します。

```sql
-- カテゴリ
CREATE TABLE IF NOT EXISTS category (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,
    color       TEXT    DEFAULT '#6c757d',
    created_at  TEXT    DEFAULT (datetime('now','localtime'))
);

-- タスク
CREATE TABLE IF NOT EXISTS task (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    title        TEXT    NOT NULL,
    description  TEXT,
    status       TEXT    NOT NULL DEFAULT 'todo',   -- todo / in_progress / done
    priority     TEXT    NOT NULL DEFAULT 'medium', -- low / medium / high
    category_id  INTEGER REFERENCES category(id),
    due_date     TEXT,
    completed_at TEXT,
    created_at   TEXT    DEFAULT (datetime('now','localtime')),
    updated_at   TEXT,
    is_deleted   INTEGER DEFAULT 0
);

-- コメント
CREATE TABLE IF NOT EXISTS comment (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id    INTEGER NOT NULL REFERENCES task(id),
    body       TEXT    NOT NULL,
    author     TEXT    NOT NULL,
    created_at TEXT    DEFAULT (datetime('now','localtime'))
);

-- サンプルデータ
INSERT INTO category(name, color) VALUES ('開発', '#0d6efd'), ('デザイン', '#6f42c1'), ('運用', '#dc3545');
INSERT INTO task(title, status, priority, category_id, due_date)
  VALUES ('トップページ実装', 'in_progress', 'high', 1, date('now', '+3 days')),
         ('ロゴデザイン', 'todo', 'medium', 2, date('now', '+7 days')),
         ('サーバー設定', 'done', 'high', 3, date('now', '-1 day'));
```

SQLite CLI で DB を作成します。

```bash
sqlite3 projects/task-tracker/database/task-tracker.db < projects/task-tracker/database/init.sql
```

---

## Step 3: エンティティ YAML を自動生成する（スキャフォールド）

DB が存在する状態でスキャフォールドを実行すると、テーブル構造から YAML の雛形を自動生成できます。

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --scaffold-entities \
  --project=task-tracker
```

`projects/task-tracker/entities/` に `category.yml`・`task.yml`・`comment.yml` が生成されます。
生成された雛形を以降のステップで編集していきます。

---

## Step 4: Category エンティティを定義する

`projects/task-tracker/entities/category.yml` を編集します。

```yaml
entities:
  category:
    table: category
    key: id
    displayName: カテゴリ

    # ページネーション設定
    paging:
      pageSize: 20
      mode: numbered

    # フォームレイアウト（1列）
    layout:
      forms:
        columns: 1
        order: [name, color]

    # 一覧に表示する列
    columns:
      id:         { type: int,    identity: true, label: ID,         sortable: true }
      name:       { type: string, required: true,  label: カテゴリ名, searchable: true, sortable: true }
      color:      { type: string, label: カラー }
      created_at: { type: string, label: 登録日時, sortable: true }

    # 作成・編集フォームのフィールド
    forms:
      name:  { type: string, required: true, label: カテゴリ名, editable: true }
      color: { type: string, label: カラーコード（例: #0d6efd）, editable: true }

    # フィルターなし
    filters: {}

    # タスク一覧へのリンク（カテゴリ詳細画面から辿れる）
    links:
      tasks:
        label: タスク一覧
        entity: task
        filter:
          category_id: "{id}"

    # 組み込みフック: 前後の空白を除去、登録日時を自動セット
    hooks:
      beforeCreate:
        - trim:name
        - now:created_at
      beforeUpdate:
        - trim:name
```

---

## Step 5: Task エンティティを定義する（JOIN・外部キー・ソフトデリート）

`projects/task-tracker/entities/task.yml` を編集します。

```yaml
entities:
  task:
    table: task
    key: id
    displayName: タスク
    softDelete: true    # 削除は is_deleted=1 に更新（物理削除しない）

    paging:
      pageSize: 25
      mode: numbered
      enableCount: true

    layout:
      forms:
        columns: 2
        order: [title, status, priority, category_id, due_date, description, completed_at]
      filters:
        columns: 4
        order: [status, priority, category_id, due_date]

    # 削除確認ダイアログ
    confirmation:
      delete: "このタスクを削除してもよいですか？"

    # category テーブルを LEFT JOIN して カテゴリ名を取得
    joins:
      - table: category
        alias: cat
        on: "task.category_id = cat.id"
        type: left

    # 一覧表示列
    columns:
      id:           { type: int,    identity: true, label: ID,         sortable: true }
      title:        { type: string, required: true,  label: タイトル,  searchable: true, sortable: true }
      status:       { type: string, label: ステータス, sortable: true }
      priority:     { type: string, label: 優先度,    sortable: true }
      category_name:
        type: string
        expression: "cat.name"     # JOIN した列を expression で参照
        label: カテゴリ
        sortable: true
      due_date:     { type: string, label: 期限,      sortable: true }
      completed_at: { type: string, label: 完了日時,   sortable: true }
      created_at:   { type: string, label: 登録日時,   sortable: true }

    # 作成・編集フォーム
    forms:
      title:
        type: string
        required: true
        label: タイトル
        editable: true
      description:
        type: string
        label: 詳細
        editable: true
      status:
        type: string
        label: ステータス
        editable: true
        options: [todo, in_progress, done]
      priority:
        type: string
        label: 優先度
        editable: true
        options: [low, medium, high]
      category_id:
        type: int
        label: カテゴリ
        editable: true
        foreignKey:
          entity: category
          displayColumn: name       # ドロップダウンに category.name を表示
      due_date:
        type: date
        label: 期限
        editable: true
      completed_at:
        type: string
        label: 完了日時
        editable: false             # フォームで編集不可（フックが自動セット）
      created_at:
        type: string
        label: 登録日時
        editable: false

    # フィルター
    filters:
      status:
        type: dropdown
        label: ステータス
        options: [todo, in_progress, done]
      priority:
        type: dropdown
        label: 優先度
        options: [low, medium, high]
      category_id:
        type: dropdown
        label: カテゴリ
        foreignKey:
          entity: category
          displayColumn: name
      due_date:
        type: date-range            # 日付範囲フィルター（_from / _to）
        label: 期限

    # コメント一覧へのリンク
    links:
      comments:
        label: コメント
        entity: comment
        filter:
          task_id: "{id}"

    # 組み込みフック
    hooks:
      beforeCreate:
        - validate_required:title
        - trim:title
        - now:created_at
        - now:updated_at
      beforeUpdate:
        - validate_required:title
        - trim:title
        - now:updated_at
        - task_complete_timestamp    # ← カスタムフック（Step 8 で実装）
```

---

## Step 6: Comment エンティティを定義する

`projects/task-tracker/entities/comment.yml` を編集します。

```yaml
entities:
  comment:
    table: comment
    key: id
    displayName: コメント

    paging:
      pageSize: 50
      mode: numbered

    layout:
      forms:
        columns: 1
        order: [task_id, author, body]

    joins:
      - table: task
        alias: t
        on: "comment.task_id = t.id"
        type: left

    columns:
      id:         { type: int,    identity: true, label: ID,        sortable: true }
      task_title:
        type: string
        expression: "t.title"
        label: タスク
        sortable: true
      author:     { type: string, label: 投稿者,  searchable: true, sortable: true }
      body:       { type: string, label: 本文,    searchable: true }
      created_at: { type: string, label: 投稿日時, sortable: true }

    forms:
      task_id:
        type: int
        label: タスク
        editable: true
        foreignKey:
          entity: task
          displayColumn: title
      author:
        type: string
        required: true
        label: 投稿者
        editable: true
      body:
        type: string
        required: true
        label: 本文
        editable: true

    filters:
      author:
        type: like
        label: 投稿者

    hooks:
      beforeCreate:
        - validate_required:author,body
        - trim:author,body
        - now:created_at
```

---

## Step 7: ダッシュボードを設定する

`projects/task-tracker/config/dashboard.yml` を編集します。

```yaml
# 統計カード
stats:
  - label: タスク総数
    entity: task
    aggregate: count
    icon: 📋
    color: badge-primary

  - label: 未着手
    entity: task
    aggregate: count
    filter: "status = 'todo'"
    icon: 🔵
    color: badge-info

  - label: 進行中
    entity: task
    aggregate: count
    filter: "status = 'in_progress'"
    icon: 🟡
    color: badge-warning

  - label: 完了
    entity: task
    aggregate: count
    filter: "status = 'done'"
    icon: ✅
    color: badge-success

# グラフ
charts:
  # 優先度別タスク数（ドーナツ）
  - title: 優先度別タスク数
    type: doughnut
    entity: task
    valueAggregate: count
    groupExpression: priority
    orderBy: value
    orderDir: desc

  # カテゴリ別タスク数（棒グラフ）
  - title: カテゴリ別タスク数
    type: bar
    entity: task
    valueAggregate: count
    groupExpression: "cat.name"
    joinClause: "LEFT JOIN category cat ON task.category_id = cat.id"
    orderBy: value
    orderDir: desc
    colorBg: rgba(13, 110, 253, 0.6)
    colorBorder: rgba(13, 110, 253, 1)
```

### ダッシュボード設定の主なオプション

| キー | 説明 |
|------|------|
| `aggregate` | `count` / `sum` / `avg` |
| `filter` | WHERE 句に追加する SQL 条件（安全に検証済みの識別子のみ使用可） |
| `type` | `bar` / `doughnut` / `pie` / `line` |
| `groupExpression` | GROUP BY の式（テーブル別名付き JOIN 列も指定可） |
| `joinClause` | チャートの JOIN 句 |
| `valueColumn` | `sum`/`avg` 時に集計する列名 |

---

## Step 8: カスタムフックをスキャフォールドする

CLI でフッククラスとテストファイルの雛形を生成します。

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --scaffold-hook \
  --name=TaskCompleteTimestamp \
  --project=task-tracker \
  --with-tests
```

生成されるファイル：

```
projects/task-tracker/Hooks/TaskCompleteTimestampHook.cs
NetYamlForge.Tests/Hooks/TaskCompleteTimestampHookTests.cs
```

### フックを実装する

`projects/task-tracker/Hooks/TaskCompleteTimestampHook.cs` を編集します。

```csharp
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TaskTracker.Hooks;

/// <summary>
/// タスクの status が "done" になったとき completed_at を自動セットするフック。
///
/// entities/task.yml での使用例:
///   hooks:
///     beforeUpdate:
///       - task_complete_timestamp
/// </summary>
public class TaskCompleteTimestampHook : IEntityHook
{
    public string Name => "task_complete_timestamp";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("status", out var s) ? s as string : null;

        if (string.Equals(newStatus, "done", StringComparison.OrdinalIgnoreCase))
        {
            // 完了時刻を自動セット（未セットの場合のみ）
            if (!ctx.Values.TryGetValue("completed_at", out var existing) ||
                existing == null || string.IsNullOrWhiteSpace(existing.ToString()))
            {
                ctx.Values["completed_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }
        else
        {
            // done 以外に戻された場合は完了時刻をクリア
            ctx.Values["completed_at"] = null;
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
```

### `Program.cs` に登録する

`NetYamlForge/Program.cs` の DI 登録セクションに追記します。

```csharp
// projects/task-tracker/Hooks
builder.Services.AddSingleton<IEntityHook, TaskCompleteTimestampHook>();
```

> **注意**: フック名（`Name` プロパティ）と YAML の `hooks.beforeUpdate` に書いた名前が
> 大文字小文字を区別せずにマッチします。

---

## Step 9: カスタムページを作成する（期限切れタスク一覧）

`projects/task-tracker/pages/OverdueTasks.yml` を作成します。

```yaml
title: 期限切れタスク
description: 期限が過ぎて完了していないタスクの一覧です。

ui:
  page:
    layout: stack
    density: comfortable

sections:
  - id: overdue_tasks
    title: 期限切れタスク
    source_type: custom
    source: |
      SELECT
        t.id        AS TaskId,
        t.title     AS タイトル,
        t.status    AS ステータス,
        t.priority  AS 優先度,
        cat.name    AS カテゴリ,
        t.due_date  AS 期限,
        CAST(julianday('now') - julianday(t.due_date) AS INTEGER) AS 超過日数
      FROM task t
      LEFT JOIN category cat ON cat.id = t.category_id
      WHERE t.due_date < date('now','localtime')
        AND t.status != 'done'
        AND t.is_deleted = 0
    columns:
      - TaskId
      - タイトル
      - ステータス
      - 優先度
      - カテゴリ
      - 期限
      - 超過日数
    page_size: 50
    editable: false
    read_only: true
    filters:
      priority:
        label: 優先度
        type: eq
      カテゴリ:
        label: カテゴリ
        type: like
```

### ページの主な設定オプション

| キー | 説明 |
|------|------|
| `source_type` | `custom`（任意 SQL）または `table`（テーブル直参照） |
| `source` | カスタム SQL（`source_type: custom` 時） |
| `editable` | `true` にすると行編集が可能 |
| `updatable_fields` | 編集可能なフィールドを限定する |
| `page_size` | 1 ページの行数 |
| `filters` | ページ内フィルター（`like` / `eq` / `range` / `date-range` / `gte` / `lte`） |

---

## Step 10: ナビゲーションに追加する

`projects/task-tracker/config/layout.yml` にページへのメニューリンクを追加します。

```yaml
nav:
  - label: ダッシュボード
    href: /task-tracker/Dashboard
    icon: 🏠

  - label: タスク
    href: /task-tracker/DynamicEntity/task
    icon: 📋

  - label: カテゴリ
    href: /task-tracker/DynamicEntity/category
    icon: 🏷️

  - label: コメント
    href: /task-tracker/DynamicEntity/comment
    icon: 💬

  - label: 期限切れタスク
    href: /task-tracker/Page/OverdueTasks
    icon: ⚠️
```

---

## Step 11: アプリを起動して確認する

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj
```

ブラウザで `http://localhost:5000/task-tracker` を開きます。

- ログイン: `admin` / `Admin@123`
- ダッシュボード、タスク一覧、カスタムページが表示される

---

## 主要機能リファレンス

### エンティティ YAML の全オプション

```yaml
entities:
  <entity_name>:
    table: <DBテーブル名>         # 必須
    key: <主キー列名>              # 必須
    displayName: <表示名>
    softDelete: true              # true → DELETE は is_deleted=1 の UPDATE になる

    paging:
      pageSize: 20                # 1ページの行数
      mode: numbered              # numbered（ページ番号）または cursor
      enableCount: true           # 総件数を取得するか

    confirmation:                 # 確認ダイアログ（省略可）
      create: "確認メッセージ"
      update: "確認メッセージ"
      delete: "確認メッセージ"

    layout:
      forms:
        columns: 2                # フォームの列数（1 or 2）
        order: [field1, field2]   # 表示順
      filters:
        columns: 4
        order: [filter1, filter2]

    joins:                        # テーブル結合
      - table: other_table
        alias: ot
        on: "main_table.fk_id = ot.id"
        type: left                # left / inner

    columns:                      # 一覧表示
      <col>:
        type: string | int | decimal | boolean | date | email
        label: 表示名
        required: true
        searchable: true          # 全文検索対象
        sortable: true
        identity: true            # 自動連番主キー
        expression: "ot.col"      # JOIN 列の参照

    forms:                        # 作成・編集フォーム
      <col>:
        type: <type>
        label: 表示名
        required: true
        editable: true
        options: [val1, val2]     # ドロップダウン選択肢
        foreignKey:
          entity: <entity>
          displayColumn: <col>    # 参照先の表示列

    filters:                      # フィルター UI
      <col>:
        type: dropdown | like | range | date-range
        label: 表示名
        options: [val1, val2]
        foreignKey:
          entity: <entity>
          displayColumn: <col>

    links:                        # 詳細画面の関連リンク
      <link_name>:
        label: リンクラベル
        entity: <entity>
        filter:
          <col>: "{id}"           # {id} は現在レコードの主キー値

    hooks:
      beforeCreate: [hook1, hook2]
      afterCreate:  [hook1]
      beforeUpdate: [hook1, hook2]
      afterUpdate:  [hook1]
      beforeDelete: [hook1]
      afterDelete:  [hook1]
```

---

### 組み込みフック一覧（よく使うもの）

| フック名 | 用途 | 例 |
|---------|------|----|
| `validate_required:f1,f2` | 必須チェック | `validate_required:title,author` |
| `validate_email:f` | メール形式チェック | `validate_email:email` |
| `validate_range:f:min=0,max=100` | 数値範囲チェック | `validate_range:price:min=0` |
| `validate_unique:f` | 重複チェック | `validate_unique:name` |
| `trim:f1,f2` | 前後の空白除去 | `trim:title,body` |
| `uppercase:f` | 大文字変換 | `uppercase:code` |
| `lowercase:f` | 小文字変換 | `lowercase:email` |
| `now:f` | 現在時刻を自動セット | `now:created_at` |
| `current_user:f` | ログインユーザーをセット | `current_user:author` |
| `audit_log` | 変更ログを記録 | `afterCreate: [audit_log]` |

詳細は `docs/COMMON_HOOKS.md` を参照してください。

---

### フィルタータイプ一覧

| タイプ | 説明 | パラメータ |
|--------|------|----------|
| `dropdown` | 完全一致（選択肢あり） | `?<key>=value` |
| `like` | 部分一致（LIKE %val%） | `?<key>=value` |
| `range` | 数値範囲（decimal） | `?<key>_min=0&<key>_max=100` |
| `date-range` | 日付範囲 | `?<key>_from=2026-01-01&<key>_to=2026-12-31` |

---

### カスタムフックの実装パターン

```csharp
public class MyHook : IEntityHook
{
    public string Name => "my_hook_name";   // YAML で参照する名前

    // DB 書き込み前に実行（バリデーション・値変換）
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // ctx.Values  : フォームの入力値（変更可能）
        // ctx.Entity  : エンティティ名
        // ctx.Action  : Create / Update / Delete
        // ctx.RowId   : 既存レコードの主キー（Update/Delete 時）

        if (someConditionFails)
            return Task.FromResult(HookResult.Abort("エラーメッセージ"));

        ctx.Values["my_field"] = "自動セットした値";
        return Task.FromResult(HookResult.Continue());
    }

    // DB 書き込み後に実行（同一トランザクション内）
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 関連テーブルの更新・通知処理など
        return Task.CompletedTask;
    }
}
```

`Program.cs` に DI 登録：

```csharp
builder.Services.AddSingleton<IEntityHook, MyHook>();
```

---

## よくある間違い

| 間違い | 正しい対応 |
|--------|-----------|
| YAML の `columns` でフィールドを定義したのにフォームに出ない | `forms` にも定義が必要（`columns` は一覧表示、`forms` は入力フォーム） |
| JOIN した列が一覧に表示されない | `columns` で `expression: "alias.col"` を指定する |
| フックが動かない | `Program.cs` に `AddSingleton<IEntityHook, XxxHook>()` を追記したか確認 |
| `softDelete: true` にしたのに物理削除される | `is_deleted` 列が DB テーブルに存在するか確認 |
| カスタムページが 404 になる | ファイル名（大文字小文字含む）と URL が一致しているか確認 |
| フィルターが効かない | `date-range` の場合はパラメータが `key_from`/`key_to`、`range` は `key_min`/`key_max` |

---

## 次に読むべきドキュメント

| ドキュメント | 内容 |
|------------|------|
| `docs/COMMON_HOOKS.md` | 組み込みフック全 20 種の詳細 |
| `docs/examples/02-add-validation-hook.md` | バリデーション追加の実例 |
| `docs/examples/05-add-custom-hook.md` | カスタムフック実装テンプレート |
| `docs/architecture-map-ja.md` | リクエスト処理フローの全体図 |
| `docs/runbook-index-ja.md` | 運用ランブック |
