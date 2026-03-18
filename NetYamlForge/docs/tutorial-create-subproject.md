# チュートリアル：NetYamlForge でサブプロジェクトを作成する

このチュートリアルでは、NetYamlForge フレームワーク CLI を使って **タスク管理システム（task-tracker）** をゼロから構築する手順を、主要機能をステップごとに説明します。

---

## 作るもの

| 機能 | 説明 |
|------|------|
| エンティティ | Category / Task / Comment |
| ダッシュボード | 統計カード × 4 ＋ チャート × 2 |
| カスタムページ | 期限切れタスク一覧 |
| 組み込みフック | バリデーション・自動タイムスタンプ |
| カスタムフック | タスク完了時刻の自動設定 |

---

## フレームワーク構造

サブプロジェクトは `projects/<name>/` 以下に配置し、すべて YAML ファイルで設定します。フレームワークは起動時にこれらのファイルを読み込み、フル CRUD 管理画面を自動生成します。コントローラやビューを手書きする必要はありません。

```
projects/<name>/
├── project.yaml          # プロジェクト設定
├── config/
│   ├── dashboard.yml     # ダッシュボードの統計・チャート
│   ├── layout.yml        # ナビゲーションメニュー
│   └── i18n.yml          # 多言語ラベル
├── entities/             # エンティティ YAML 定義
├── pages/                # カスタムページ YAML
├── Hooks/                # プロジェクト固有のフッククラス
├── database/             # SQLite DB ファイル
└── views/                # プロジェクト固有のビュー
```

---

## ステップ 1: プロジェクトを初期化する

`--init-project` コマンドを実行してディレクトリ構造をスキャフォールドし、`project.yaml` を生成します。

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --init-project \
  --project=task-tracker \
  --display-name="タスク管理" \
  --db-type=sqlite \
  --db-path=database/task-tracker.db
```

以下のディレクトリ構造が自動生成されます：

```
projects/task-tracker/
├── project.yaml
├── config/
│   ├── dashboard.yml
│   ├── layout.yml
│   └── i18n.yml
├── entities/
├── pages/
├── Hooks/
├── database/
└── views/
```

### 生成された `project.yaml` を確認する

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

## ステップ 2: データベースを作成する

`projects/task-tracker/database/init.sql` にテーブル定義を作成します：

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

SQLite CLI を使って SQLite データベースを初期化します：

```bash
sqlite3 projects/task-tracker/database/task-tracker.db < projects/task-tracker/database/init.sql
```

---

## ステップ 3: エンティティをスキャフォールドする

データベースが準備できたら、スキャフォールドコマンドを実行してテーブルスキーマから YAML スケルトンを自動生成します：

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --scaffold-entities \
  --project=task-tracker
```

`projects/task-tracker/entities/` 以下に `category.yml`、`task.yml`、`comment.yml` が生成されます。以降のステップでは各ファイルの編集方法を説明します。

---

## ステップ 4: エンティティ YAML を定義する

生成された各ファイルを編集して、カラム・フォーム・フィルター・フックを設定します。

### `entities/category.yml`

```yaml
entities:
  category:
    table: category
    key: id
    displayName: カテゴリ

    paging:
      pageSize: 20
      mode: numbered

    layout:
      forms:
        columns: 1
        order: [name, color]

    columns:
      id:         { type: int,    identity: true, label: ID,         sortable: true }
      name:       { type: string, required: true,  label: 名前,      searchable: true, sortable: true }
      color:      { type: string, label: カラー }
      created_at: { type: string, label: 作成日時, sortable: true }

    forms:
      name:  { type: string, required: true, label: カテゴリ名, editable: true }
      color: { type: string, label: "カラーコード (例: #0d6efd)", editable: true }

    filters: {}

    links:
      tasks:
        label: タスク
        entity: task
        filter:
          category_id: "{id}"

    hooks:
      beforeCreate:
        - trim:name
        - now:created_at
      beforeUpdate:
        - trim:name
```

### `entities/task.yml`（JOIN・外部キー・ソフトデリート付き）

```yaml
entities:
  task:
    table: task
    key: id
    displayName: タスク
    softDelete: true    # DELETE は行を削除せず is_deleted=1 に更新する

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

    confirmation:
      delete: "このタスクを削除してもよろしいですか？"

    joins:
      - table: category
        alias: cat
        on: "task.category_id = cat.id"
        type: left

    columns:
      id:           { type: int,    identity: true, label: ID,           sortable: true }
      title:        { type: string, required: true,  label: タイトル,    searchable: true, sortable: true }
      status:       { type: string, label: ステータス, sortable: true }
      priority:     { type: string, label: 優先度,   sortable: true }
      category_name:
        type: string
        expression: "cat.name"     # expression で JOIN したカラムを参照する
        label: カテゴリ
        sortable: true
      due_date:     { type: string, label: 期限日,     sortable: true }
      completed_at: { type: string, label: 完了日時,   sortable: true }
      created_at:   { type: string, label: 作成日時,   sortable: true }

    forms:
      title:
        type: string
        required: true
        label: タイトル
        editable: true
      description:
        type: string
        label: 説明
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
          displayColumn: name       # ドロップダウンに category.name を表示する
      due_date:
        type: date
        label: 期限日
        editable: true
      completed_at:
        type: string
        label: 完了日時
        editable: false             # フォームでは編集不可。フックが自動的に設定する
      created_at:
        type: string
        label: 作成日時
        editable: false

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
        label: 期限日

    links:
      comments:
        label: コメント
        entity: comment
        filter:
          task_id: "{id}"

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
        - task_complete_timestamp    # ステップ 6 で実装するカスタムフック
```

### `entities/comment.yml`

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
      id:         { type: int,    identity: true, label: ID,         sortable: true }
      task_title:
        type: string
        expression: "t.title"
        label: タスク
        sortable: true
      author:     { type: string, label: 投稿者, searchable: true, sortable: true }
      body:       { type: string, label: 本文,   searchable: true }
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

## ステップ 5: ダッシュボードを設定する

`projects/task-tracker/config/dashboard.yml` を編集します：

```yaml
# 統計カード
stats:
  - label: タスク合計
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

# チャート
charts:
  # 優先度別タスク（ドーナツグラフ）
  - title: 優先度別タスク
    type: doughnut
    entity: task
    valueAggregate: count
    groupExpression: priority
    orderBy: value
    orderDir: desc

  # カテゴリ別タスク（棒グラフ）
  - title: カテゴリ別タスク
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

### ダッシュボード設定オプション

| キー | 説明 |
|------|------|
| `aggregate` | `count` / `sum` / `avg` |
| `filter` | WHERE 句に追加する SQL 条件（検証済み識別子のみ使用可） |
| `type` | `bar` / `doughnut` / `pie` / `line` |
| `groupExpression` | GROUP BY 式（JOIN のエイリアスカラム指定可） |
| `joinClause` | チャート用の JOIN 句 |
| `valueColumn` | `sum` / `avg` 使用時の集計カラム |

---

## ステップ 6: カスタムフックを追加する

CLI を使ってフッククラスとテストファイルをスキャフォールドします：

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

`projects/task-tracker/Hooks/TaskCompleteTimestampHook.cs` を編集します：

```csharp
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TaskTracker.Hooks;

/// <summary>
/// タスクのステータスが "done" に変更されたとき、completed_at を自動設定する。
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
            // まだ設定されていない場合のみ完了時刻を設定する
            if (!ctx.Values.TryGetValue("completed_at", out var existing) ||
                existing == null || string.IsNullOrWhiteSpace(existing.ToString()))
            {
                ctx.Values["completed_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }
        else
        {
            // ステータスが "done" から変更された場合は完了時刻をクリアする
            ctx.Values["completed_at"] = null;
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
```

### `Program.cs` にフックを登録する

`NetYamlForge/Program.cs` の DI 登録セクションに以下の行を追加します：

```csharp
// projects/task-tracker/Hooks
builder.Services.AddSingleton<IEntityHook, TaskCompleteTimestampHook>();
```

> **注意**: フック名（`Name` プロパティ）は、YAML の `hooks` セクションに記述された名前と大文字小文字を区別せずに照合されます。

---

## ステップ 7: カスタムページを作成する

`projects/task-tracker/pages/OverdueTasks.yml` を作成します：

```yaml
title: 期限切れタスク
description: 期限を過ぎたまま完了していないタスク一覧。

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
        t.due_date  AS 期限日,
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
      - 期限日
      - 超過日数
    page_size: 50
    editable: false
    read_only: true
    filters:
      優先度:
        label: 優先度
        type: eq
      カテゴリ:
        label: カテゴリ
        type: like
```

### カスタムページ設定オプション

| キー | 説明 |
|------|------|
| `source_type` | `custom`（任意の SQL）または `table`（テーブル直接参照） |
| `source` | カスタム SQL クエリ（`source_type: custom` 使用時） |
| `editable` | `true` にすると行の編集を許可する |
| `updatable_fields` | 編集可能なフィールドを制限する |
| `page_size` | 1ページあたりの表示件数 |
| `filters` | ページレベルフィルター（`like` / `eq` / `range` / `date-range` / `gte` / `lte`） |

---

## ステップ 8: ナビゲーションを更新する

`projects/task-tracker/config/layout.yml` を編集してメニューリンクを追加します：

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

## ステップ 9: 起動して確認する

アプリケーションを起動します：

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj
```

ブラウザで `http://localhost:5000/task-tracker` を開きます。

- デフォルトログイン: `admin` / `Admin@123`
- ダッシュボード・タスク一覧・カテゴリ一覧・カスタムの「期限切れタスク」ページが正しく表示されることを確認する
- タスクを作成してステータスを `done` に変更し、カスタムフックにより `completed_at` が自動設定されることを確認する

---

## リファレンス: エンティティ YAML 全オプション

```yaml
entities:
  <エンティティ名>:
    table: <DB テーブル名>        # 必須
    key: <主キーカラム>           # 必須
    displayName: <表示名>
    softDelete: true              # true → DELETE は行を削除せず is_deleted=1 に更新する

    paging:
      pageSize: 20                # 1ページあたりの表示件数
      mode: numbered              # numbered（ページ番号）または cursor
      enableCount: true           # レコード総件数を取得する

    confirmation:                 # 確認ダイアログ（任意）
      create: "確認メッセージ"
      update: "確認メッセージ"
      delete: "確認メッセージ"

    layout:
      forms:
        columns: 2                # フォームカラム数（1 または 2）
        order: [field1, field2]   # 表示順
      filters:
        columns: 4
        order: [filter1, filter2]

    joins:                        # テーブル結合
      - table: other_table
        alias: ot
        on: "main_table.fk_id = ot.id"
        type: left                # left / inner

    columns:                      # 一覧ビューのカラム
      <col>:
        type: string | int | decimal | boolean | date | email
        label: 表示名
        required: true
        searchable: true          # 全文検索に含める
        sortable: true
        identity: true            # 自動連番主キー
        expression: "ot.col"      # JOIN したカラムを参照する

    forms:                        # 作成・編集フォームのフィールド
      <col>:
        type: <タイプ>
        label: 表示名
        required: true
        editable: true
        options: [val1, val2]     # ドロップダウンの選択肢
        foreignKey:
          entity: <エンティティ>
          displayColumn: <col>    # 参照エンティティから表示するカラム

    filters:                      # フィルター UI
      <col>:
        type: dropdown | like | range | date-range
        label: 表示名
        options: [val1, val2]
        foreignKey:
          entity: <エンティティ>
          displayColumn: <col>

    links:                        # 詳細ページの関連リンク
      <リンク名>:
        label: リンクラベル
        entity: <エンティティ>
        filter:
          <col>: "{id}"           # {id} は現在のレコードの主キーに置換される

    hooks:
      beforeCreate: [hook1, hook2]
      afterCreate:  [hook1]
      beforeUpdate: [hook1, hook2]
      afterUpdate:  [hook1]
      beforeDelete: [hook1]
      afterDelete:  [hook1]
```

---

## リファレンス: 組み込みフック一覧

| フック名 | 用途 | 使用例 |
|---------|------|--------|
| `validate_required:f1,f2` | 必須フィールドチェック | `validate_required:title,author` |
| `validate_email:f` | メール形式チェック | `validate_email:email` |
| `validate_range:f:min=0,max=100` | 数値範囲チェック | `validate_range:price:min=0` |
| `validate_unique:f` | 重複チェック | `validate_unique:name` |
| `trim:f1,f2` | 前後の空白を除去する | `trim:title,body` |
| `uppercase:f` | 大文字に変換する | `uppercase:code` |
| `lowercase:f` | 小文字に変換する | `lowercase:email` |
| `now:f` | 現在のタイムスタンプを自動設定する | `now:created_at` |
| `current_user:f` | ログインユーザーを設定する | `current_user:author` |
| `audit_log` | 変更ログを記録する | `afterCreate: [audit_log]` |

全一覧は `docs/COMMON_HOOKS.md` を参照してください。

---

## よくある失敗パターン

| 間違い | 正しい対処 |
|-------|-----------|
| `columns` にフィールドを定義したがフォームに表示されない | `forms` にも定義が必要です。`columns` は一覧ビュー、`forms` は入力フォームを制御します |
| JOIN したカラムが一覧に表示されない | `columns` 定義に `expression: "エイリアス.カラム"` を指定してください |
| フックが実行されない | `Program.cs` に `AddSingleton<IEntityHook, XxxHook>()` が追加されているか確認してください |
| `softDelete: true` を設定したが行が物理削除される | データベーステーブルに `is_deleted` カラムが存在するか確認してください |
| カスタムページが 404 になる | ファイル名（大文字小文字含む）が URL と完全に一致しているか確認してください |
| フィルターが効かない | `date-range` のパラメーターは `key_from`/`key_to`、`range` のパラメーターは `key_min`/`key_max` です |

---

## 次のステップ

| ドキュメント | 説明 |
|------------|------|
| `docs/COMMON_HOOKS.md` | 組み込みフック 20 種の詳細 |
| `docs/examples/02-add-validation-hook.md` | バリデーション追加の実践例 |
| `docs/examples/05-add-custom-hook.md` | カスタムフック実装テンプレート |
| `docs/architecture-map-ja.md` | リクエスト処理フローの全体図 |
| `docs/runbook-index-ja.md` | 運用ランブックインデックス |
| `docs/how-to-create-subproject.md` | サブプロジェクト作成時の AI 指示パターン |
| `docs/tutorial-create-project-ja.md` | プロジェクト作成チュートリアル（日本語） |
