# CLI を使用したサブプロジェクト作成チュートリアル

NetYamlForge の CLI スキャフォールド機能を使用して、新しいサブプロジェクトをゼロから作成する手順を解説します。

---

## 目次

1. [概要](#概要)
2. [事前準備](#事前準備)
3. [基本コマンド](#基本コマンド)
4. [ステップバイステップ手順](#ステップバイステップ手順)
5. [生成されるファイル構造](#生成されるファイル構造)
6. [次のステップ](#次のステップ)
7. [トラブルシューティング](#トラブルシューティング)

---

## 概要

`--init-project` コマンドを使用すると、データベース接続設定から YAML 定義ファイルまでを自動生成できます。

**主な機能**:
- 最小構成のプロジェクト雛形を生成
- SQLite / SQL Server / MySQL / PostgreSQL に対応
- 自動でエンティティ定義をスキャフォールド
- ダッシュボードとスターターページを自動設定

---

## 事前準備

### 1. 開発環境の確認

```bash
# .NET 10.0 SDK がインストールされていることを確認
dotnet --version
```

### 2. プロジェクトルートの確認

コマンドは以下のいずれかのディレクトリから実行します:

- `NetYamlForge/` ディレクトリ直下
- `NetYamlForge/NetYamlForge/` ディレクトリ

```bash
# プロジェクトルートの確認 (projects ディレクトリが存在するか)
ls -la projects/
```

---

## 基本コマンド

### コマンド構文

```bash
dotnet run -- --init-project \
  --project=<プロジェクト名> \
  --display-name="<表示名>" \
  --db-type=<データベースタイプ> \
  [--db-path=<DB ファイルパス>] \
  [--db-connection="<接続文字列>"] \
  [--no-auto-scaffold] \
  [--force] \
  [--json]
```

### パラメータ説明

| パラメータ | 必須 | 説明 |
|-----------|------|------|
| `--project` | ✅ | プロジェクト名 (英小文字・数字・ハイフン、2-63 文字) |
| `--display-name` | ❌ | 表示名 (省略時は project 名から自動生成) |
| `--db-type` | ❌ | DB タイプ: `sqlite` (既定), `sqlserver`, `postgresql`, `mysql` |
| `--db-path` | ❌ | SQLite の DB ファイルパス (既定：`database/<project>.db`) |
| `--db-connection` | ✅ (SQLite 以外) | 接続文字列 |
| `--no-auto-scaffold` | ❌ | 自動エンティティ生成をスキップ |
| `--force` | ❌ | 既存ディレクトリを上書き |
| `--json` | ❌ | JSON 形式で結果を出力 (CI 連携用) |

---

## ステップバイステップ手順

### ステップ 1: SQLite プロジェクトの作成

#### 例 1: 最小構成のプロジェクト

```bash
dotnet run -- --init-project \
  --project=my-app \
  --display-name="My Application"
```

**出力例**:
```
[ok] project template created: /path/to/projects/my-app
next: dotnet run -- --scaffold-entities --project=my-app
```

#### 例 2: 既存の SQLite データベースを使用

```bash
dotnet run -- --init-project \
  --project=shop \
  --display-name="店舗管理システム" \
  --db-path=data/shop.db
```

#### 例 3: 上書き実行

```bash
# 既存のプロジェクトを上書き
dotnet run -- --init-project \
  --project=shop \
  --force
```

---

### ステップ 2: SQL Server / MySQL / PostgreSQL の場合

#### SQL Server の例

```bash
dotnet run -- --init-project \
  --project=enterprise-app \
  --display-name="Enterprise System" \
  --db-type=sqlserver \
  --db-connection="Server=localhost;Database=EnterpriseDB;User Id=sa;Password=YourPassword;TrustServerCertificate=true"
```

#### PostgreSQL の例

```bash
dotnet run -- --init-project \
  --project=analytics \
  --display-name="Analytics Dashboard" \
  --db-type=postgresql \
  --db-connection="Host=localhost;Database=analytics;Username=postgres;Password=secret"
```

#### MySQL の例

```bash
dotnet run -- --init-project \
  --project=inventory \
  --display-name="Inventory Management" \
  --db-type=mysql \
  --db-connection="Server=localhost;Database=inventory;Uid=root;Pwd=secret"
```

---

### ステップ 3: 自動スキャフォールドの確認

`--init-project` はデフォルトで以下の処理を自動実行します:

1. **DB スキーマからエンティティ YAML を生成**
   - `entities.generated/` - 自動生成ファイル
   - `entities/` - 編集用ファイル

2. **ダッシュボード設定を生成**
   - 各エンティティの統計カードを自動設定

3. **スターターページを生成**
   - `pages/StarterOverview.yaml`
   - `views/StarterOverview.cshtml`

4. **i18n 設定を生成**
   - `config/i18n.yml` - 多言語定義

---

### ステップ 4: 動作確認

```bash
# アプリケーションを起動
dotnet run --project NetYamlForge

# ブラウザでアクセス
# http://localhost:5000/<project-name>
# http://localhost:5000/<project-name>/Dashboard
```

---

## 生成されるファイル構造

```
projects/<project-name>/
├── project.yaml              # プロジェクト定義
├── dashboard.yml             # ダッシュボード設定
│
├── config/
│   ├── home-page.yml         # ホームページ設定
│   ├── layout.yml            # レイアウト定義
│   └── i18n.yml              # 多言語設定
│
├── database/
│   └── <project>.db          # SQLite DB ファイル
│
├── entities/
│   ├── <entity1>.yml         # エンティティ定義 (編集用)
│   ├── <entity2>.yml
│   └── .gitkeep
│
├── entities.generated/
│   ├── <entity1>.yml         # エンティティ定義 (自動生成)
│   └── <entity2>.yml
│
├── pages/
│   ├── StarterOverview.yaml  # スターターページ
│   └── .gitkeep
│
├── views/
│   ├── _ViewImports.cshtml   # View 共通設定
│   ├── _ViewStart.cshtml     # View 開始設定
│   ├── _Layout.cshtml        # プロジェクトレイアウト
│   ├── StarterOverview.cshtml
│   └── .gitkeep
│
└── docs/
    └── README-ja.md          # プロジェクトドキュメント
```

---

## 次のステップ

### 1. エンティティ YAML の編集

生成された `entities/*.yml` を編集して、表示名やバリデーションを追加:

```yaml
entities:
  product:
    table: product
    key: id
    displayName: 商品
    displayColumn: name
    
    columns:
      name:
        type: string
        label: 商品名
        required: true
      price:
        type: number
        label: 価格
```

### 2. フックの追加

ビジネスロジックを追加:

```bash
# フックをスキャフォールド
dotnet run -- --scaffold-hook \
  --name=ValidateProductPrice \
  --project=<project-name> \
  --with-tests
```

### 3. カスタムページの作成

`pages/` ディレクトリに YAML ページを追加:

```bash
# テンプレートをコピー
cp projects/_templates/page-crud.yml projects/<project>/pages/ProductList.yaml
```

### 4. ダッシュボードのカスタマイズ

`dashboard.yml` を編集して統計カードやチャートを設定:

```yaml
stats:
  - label: 総商品数
    entity: product
    aggregate: count
    icon: 📦
    color: badge-primary

charts:
  - title: 商品カテゴリ別件数
    type: bar
    entity: product
    groupBy: category_id
```

---

## トラブルシューティング

### Q1: "projects ディレクトリが見つかりません"

**原因**: 誤ったディレクトリから実行している

**解決**:
```bash
# projects ディレクトリが存在するか確認
ls -la projects/

# または NetYamlForge サブディレクトリから実行
cd NetYamlForge
dotnet run -- --init-project ...
```

---

### Q2: "既に存在します" エラー

**原因**: 同名のプロジェクトが既に存在

**解決**:
```bash
# 上書き実行
dotnet run -- --init-project \
  --project=<name> \
  --force
```

---

### Q3: SQLite DB ファイルが作成されない

**原因**: `database/` ディレクトリの権限問題

**解決**:
```bash
# ディレクトリ権限を確認
ls -la projects/<project>/database/

# 必要に応じて権限付与
chmod 755 projects/<project>/database/
```

---

### Q4: SQL Server/MySQL 接続エラー

**確認点**:

1. 接続文字列の形式が正しいか
2. データベースサーバーが起動しているか
3. ユーザー権限はあるか
4. ファイアウォールは許可されているか

**SQL Server 接続文字列の例**:
```
Server=localhost;Database=MyDB;User Id=sa;Password=YourPassword;TrustServerCertificate=true
```

**PostgreSQL 接続文字列の例**:
```
Host=localhost;Database=mydb;Username=postgres;Password=secret
```

---

### Q5: エンティティが自動生成されない

**確認点**:

1. DB にテーブルが存在するか
2. DB 接続設定は正しいか
3. エラーメッセージを確認

**手動再実行**:
```bash
dotnet run -- --scaffold-entities --project=<project-name>
```

---

## CI/CD 連携 (JSON 出力)

`--json` フラグを使用すると、構造化された結果を出力できます:

```bash
dotnet run -- --init-project \
  --project=ci-test \
  --json
```

**出力例**:
```json
{
  "command": "init-project",
  "project": "ci-test",
  "success": true,
  "exitCode": 0,
  "generatedFiles": [
    "/path/to/projects/ci-test"
  ],
  "nextSteps": [
    "dotnet run -- --scaffold-entities --project=ci-test"
  ],
  "errors": []
}
```

---

## 関連ドキュメント

- [スキャフォールド運用ガイド](entity-scaffold-workflow-ja.md)
- [フックスキャフォールドガイド](../hooks/project-hooks-guide.md)
- [YAML 実例集](../examples/chinook-yaml-examples.md)

---

## コマンドクイックリファレンス

```bash
# 基本 (SQLite)
dotnet run -- --init-project --project=my-app --display-name="My App"

# 既存 DB を使用
dotnet run -- --init-project --project=shop --db-path=data/shop.db

# SQL Server
dotnet run -- --init-project --project=ent --db-type=sqlserver --db-connection="..."

# 上書き
dotnet run -- --init-project --project=my-app --force

# 自動スキャフォールドをスキップ
dotnet run -- --init-project --project=my-app --no-auto-scaffold

# JSON 出力 (CI 用)
dotnet run -- --init-project --project=my-app --json
```
