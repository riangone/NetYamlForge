---
name: new-project
description: Create a new NetYamlForge sub-project with YAML config, entities, dashboard, and SQLite schema. Usage: /new-project <project-name> [description]
argument-hint: <project-name> [description]
allowed-tools: Read, Write, Edit, Glob, Grep, Bash
---

# NetYamlForge 新規プロジェクト作成

あなたは NetYamlForge フレームワークのエキスパートです。
引数 `$ARGUMENTS` を元に新しいサブプロジェクトをゼロから構築してください。

## 入力解析

- **プロジェクト名**: `$ARGUMENTS[0]` (英小文字・数字・ハイフン。例: `shop`, `hr-system`)
- **説明**: `$ARGUMENTS[1]` 以降（省略可）

引数が空の場合はユーザーに確認してください。

---

## 作業手順

### Step 1 — 要件の確認

まずユーザーに以下を確認してください（引数から推測できる場合はスキップ可）。

1. 管理したいデータの種類（エンティティ）は何か？
2. エンティティ間のリレーション（1対多など）はあるか？
3. ダッシュボードに表示したい集計指標は何か？
4. 必要なカスタムアクションはあるか？（一括処理、CSVインポートなど）

### Step 2 — ディレクトリ構造の作成

```
NetYamlForge/projects/<project-name>/
├── project.yaml
├── dashboard.yml
├── database/          ← 空ディレクトリ（SQLite DB が自動生成される）
├── entities/
│   └── <entity>.yml
├── pdf-templates/     ← 帳票 YAML テンプレート（必要な場合のみ）
│   └── <template>.yaml
├── Hooks/             ← カスタムハンドラー（必要な場合のみ）
├── jobs/              ← バッチジョブ（必要な場合のみ）
│   └── sql/
└── exports/           ← カスタムエクスポート SQL（必要な場合のみ）
    └── sql/
```

`Bash` で `mkdir -p` を使って作成してください。

### Step 3 — project.yaml の作成

以下のスキーマに従って `NetYamlForge/projects/<project-name>/project.yaml` を作成します。

```yaml
name: <project-name>
displayName: "<表示名>"
description: "<説明>"
version: "1.0.0"

# AI エージェント向けヒント（省略可）
aiHints:
  primaryLanguage: ja-JP
  notes:
    - "<エンティティ固有の値の説明>"

database:
  type: sqlite
  path: database/<project-name>.db   # SQLite 以外: sqlserver / mysql / postgres

features:
  multiLanguage: true
  userAuthentication: true
  dashboard: true      # dashboard.yml が必要
  pages: false         # カスタム YAML ページが必要な場合 true

layout:
  dashboardTheme: workspace   # workspace / minimal / cards
  header:
    title: "<ヘッダータイトル>"
  navigation:
    showDashboard: true
    entities:
      - <entity1>
      - <entity2>
    items:
      - label: ダッシュボード
        controller: Dashboard
        action: Index
        icon: 📊
        section: Overview
      - label: <エンティティ表示名>
        url: /<project-name>/DynamicEntity/Index?entity=<entity>
        icon: <emoji>
        section: <セクション名>

home_page:
  icon: "<emoji>"
  tagline: "<キャッチコピー>"
  tags: [<tag1>, <tag2>]
```

### Step 4 — エンティティ YAML の作成

各エンティティを `NetYamlForge/projects/<project-name>/entities/<entity>.yml` に作成します。

#### 完全なエンティティ YAML テンプレート

```yaml
imports: []
entities:
  <entity-key>:
    table: <TableName>        # DB テーブル名（PascalCase 推奨）
    key: Id                   # 主キー列名
    displayName: <表示名>
    softDelete: false         # true にすると削除フラグ方式になる
    isPublic: false           # true にすると認証なしでアクセス可
    # pdfTemplate: <template> # pdf-templates/<template>.yaml を使った帳票ボタン

    # テーブル結合（外部キー参照時に必要）
    joins:
      - type: left            # left / inner / right
        table: <OtherTable>
        alias: <alias>
        on: <TableName>.<ForeignKey> = <alias>.Id

    # フォーム定義（登録・編集画面）
    forms:
      <FieldName>:
        type: <type>          # string / int / decimal / bool / date / datetime / textarea / file / image
        required: true
        label: <ラベル>
        editable: true
        # placeholder: "<プレースホルダー>"
        # precision: 0         # decimal の小数点以下桁数
        # options: [value1, value2]   # select/dropdown の選択肢
        # foreignKey:                 # 外部キー参照
        #   entity: <entity-key>
        #   displayColumn: <column>
        #   picker: false
        #   multiPicker: false

    # 一覧表示列定義
    columns:
      Id:
        type: int
        identity: true        # 自動採番（表示のみ）
        label: ID
        sortable: true
      <ColumnName>:
        type: <type>
        label: <ラベル>
        searchable: true      # 全文検索対象
        sortable: true        # ソート可能
        hidden: false         # true で一覧非表示（エクスポートも除外）
        # expression: <alias>.<Column>  # JOIN したテーブルの列を使う場合
        # precision: 0                  # decimal 列の小数桁数
        # optionLabels:                 # 表示名マッピング
        #   value1: 表示1
        #   value2: 表示2

    # フィルター定義
    filters:
      <FilterName>:
        type: <filter-type>   # like / dropdown / multi-select / toggle-group / date-range / bool / number-range
        label: <ラベル>
        # expression: <TableName>.<Column>  # JOIN 列をフィルタ対象にする場合
        # options: [value1, value2]          # dropdown / multi-select 用
        # optionLabels:                      # toggle-group の表示名マッピング
        #   value1: "🟢 表示1"
        #   value2: "🔴 表示2"
        # foreignKey:                        # ドロップダウンを別エンティティから動的生成
        #   entity: <entity-key>
        #   displayColumn: <column>

    # フック定義（組み込みフック名または Hooks/<File>.cs 内のクラス名）
    hooks:
      beforeCreate: []        # 例: [normalize_title, validate_due_date]
      afterCreate:  []        # 例: [audit_log]
      beforeUpdate: []
      afterUpdate:  []

    # エンティティ間ナビゲーション
    links:
      <link-key>:
        label: <リンクラベル>
        targetEntity: <entity-key>
        filter:
          <targetColumn>: <sourceColumn>  # 行の値でフィルタリング

    # カスタムアクション
    actions:
      <action-key>:
        label: <ボタンラベル>
        scope: row            # row（行ごと）/ header（一覧上部）
        confirm: <確認メッセージ>   # 省略可
        handler: <handler-name>
        inputs:               # 省略可（入力フォームが必要な場合）
          - name: <FieldName>
            type: string      # string / text / textarea / date / number / dropdown / file
            label: <ラベル>
            required: true
            # allowedExtensions: ".csv,.xlsx"   # type: file の場合
            # maxSizeBytes: 5242880             # type: file の場合

    # カスタムエクスポート（ツールバーにボタンが追加される）
    exports:
      <export-key>:
        label: <ボタンラベル>
        format: csv           # csv / tsv / json / pdf
        filename: "<entity>_{date:yyyyMMdd}.csv"
        columns:              # 省略時は非表示でない全列
          - <ColumnName>
        # sqlFile: exports/sql/<query>.sql     # カスタム SQL の場合
        # sqlQuery: "SELECT ..."               # インライン SQL の場合
        # pdf:                                 # format: pdf の場合
        #   title: "レポートタイトル"
        #   pageSize: A4                       # A4 / A3 / LETTER
        #   orientation: portrait              # portrait / landscape
        #   headerColor: "#1E3A5F"
        #   oddRowColor: "#F0F4F8"
        #   showPageNumbers: true
        #   showGeneratedAt: true
        #   columns:
        #     - key: <ColumnName>
        #       width: 20                      # 列幅（%）
        #       align: left                    # left / center / right

    # フォーム・フィルターのレイアウト設定
    layout:
      forms:
        columns: 2            # フォームの列数（1 / 2 / 3）
        order:                # 表示順（省略時は forms の定義順）
          - <FieldName1>
          - <FieldName2>
      filters:
        columns: 4            # フィルターの列数
        order:
          - <FilterName1>
          - <FilterName2>

    # ページング設定
    paging:
      pageSize: 20
      mode: numbered          # numbered / keyset
      enableCount: true
```

#### フォームフィールドタイプ一覧

| type | 用途 | 補足 |
|------|------|------|
| `string` | 短いテキスト（1行） | |
| `textarea` | 長いテキスト（複数行） | |
| `int` | 整数 | |
| `decimal` | 小数 | `precision` で小数桁数指定 |
| `bool` | チェックボックス | |
| `date` | 日付（年月日） | |
| `datetime` | 日時（年月日時分） | |
| `file` | ファイル添付 | `uploadPath` 指定必要 |
| `image` | 画像 | `uploadPath` 指定必要 |

#### フィルタータイプ一覧

| type | 用途 | 補足 |
|------|------|------|
| `like` | 部分一致テキスト検索 | |
| `dropdown` | 単一選択 | `options` または `foreignKey` |
| `multi-select` | 複数選択 | `options` または `foreignKey` |
| `toggle-group` | ボタングループ切り替え | `optionLabels` で絵文字付き表示 |
| `date-range` | 日付範囲（From/To） | |
| `bool` | true/false トグル | |
| `number-range` | 数値範囲（Min/Max） | |

### Step 5 — dashboard.yml の作成

```yaml
stats:
  - label: <指標名>
    entity: <entity-key>
    aggregate: count          # count / sum / avg / max / min
    # column: <ColumnName>    # sum/avg/max/min の場合必須
    # filter: <SQL WHERE 句>  # 例: Status='active'
    icon: <emoji>
    color: badge-primary      # badge-primary/secondary/success/warning/error/info/neutral

charts:
  - label: <グラフ名>
    type: bar                 # bar / line / pie / doughnut
    entity: <entity-key>
    groupBy: <ColumnName>     # X 軸 / 凡例
    aggregate: count
    # column: <ColumnName>    # sum/avg の場合

recent:
  - label: <最近のレコード名>
    entity: <entity-key>
    limit: 5
    sort: CreatedAt
    dir: desc
```

### Step 6 — SQLite スキーマの初期化

**重要**: SQLite データベースは `DbInitializer` が起動時に自動生成するため、
`database/` ディレクトリのみ作成すれば十分です（DB ファイルは空で OK）。

ただし、スキーマをあらかじめ作成したい場合は以下を実行できます。

```bash
# database ディレクトリだけ作成（DB は自動生成される）
mkdir -p NetYamlForge/projects/<project-name>/database
```

もしシードデータが必要な場合、プロジェクトの `Hooks/` フォルダに
`IEntityHook` を実装したシードフックを作成することもできます。

### Step 7 — ビルド確認

```bash
dotnet build NetYamlForge/NetYamlForge.csproj 2>&1 | tail -8
```

エラーが出た場合は修正してから再確認してください。

---

## 実装チェックリスト

作業完了前に以下を確認してください。

- [ ] `project.yaml` が作成されている
- [ ] `dashboard.yml` が作成されている
- [ ] 全エンティティの `.yml` が `entities/` に存在する
- [ ] `database/` ディレクトリが存在する
- [ ] エンティティの `table` 名が一意（他のプロジェクトと重複しても OK、SQLite は独立）
- [ ] `project.yaml` の `navigation.entities` リストにすべてのエンティティが含まれている
- [ ] `dotnet build` が成功する

---

## 既存プロジェクトの参照

迷った場合は既存のプロジェクトを参考にしてください。

```
NetYamlForge/projects/todo-app/
├── project.yaml              ← navigation, features, layout の例
├── dashboard.yml             ← stats, charts の例
├── entities/task.yml         ← joins, toggle-group, hooks, actions, exports, layout の例
├── entities/project.yml      ← 外部キーの例
└── Hooks/TaskActionHandlers.cs  ← カスタムアクションの例

NetYamlForge/projects/biz-docs/
├── entities/jp_invoice.yml   ← pdfTemplate, date 型, precision の例
├── entities/jp_contract.yml  ← シンプルな帳票エンティティの例
└── pdf-templates/invoice.yaml  ← 帳票テンプレート（5プリミティブ: line/paragraph/row/labelTable/dataTable）
```

---

## カスタムアクションハンドラーが必要な場合

`Hooks/` に C# ファイルを作成します。ファイルは起動時に Roslyn でコンパイルされ、
`ICustomActionHandler` を実装したクラスが自動登録されます。

```csharp
// NetYamlForge/projects/<project-name>/Hooks/MyHandlers.cs
using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;

public class MyHandler : ICustomActionHandler
{
    public string Name => "my_handler";  // YAML の handler: で参照

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // ctx.RecordId  — 対象レコードのID（row スコープ）
        // ctx.Inputs    — フォーム入力値
        // ctx.Files     — アップロードされたファイルの一時パス
        // ctx.UserName  — 実行ユーザー

        await db.ExecuteAsync(
            "UPDATE MyTable SET Status = 'done' WHERE Id = @id",
            new { id = ctx.RecordId }, tx);

        return ActionHandlerResult.Success();
    }
}
```

---

## 出力形式

作業完了後、以下を報告してください。

1. 作成したファイルの一覧（ツリー形式）
2. 定義したエンティティとその列数
3. アクセス URL: `http://localhost:5000/<project-name>/DynamicEntity/Index?entity=<entity>`
4. ビルド結果
