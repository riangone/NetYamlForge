# Framework Showcase Project

NetYamlForge フレームワークの全機能を網羅的に展示するデモプロジェクトです。

## 機能一覧

### 📝 フォーム部品 (30 種類以上)

**テキスト系:**
- `string` - テキストフィールド
- `email` - メールアドレス
- `url` - URL
- `tel` - 電話番号
- `password` - パスワード
- `textarea` - 複数行テキスト
- `richtext` - リッチテキスト
- `markdown` - Markdown
- `code` - コードエディタ

**数値系:**
- `int` - 整数
- `decimal` - 小数
- `money` - 通貨
- `percent` - パーセント
- `range` - スライダー
- `rating` - 星評価

**日付系:**
- `date` - 日付
- `datetime` - 日時
- `datetime-range` - 日付範囲

**選択系:**
- `select` - セレクトボックス
- `radio` - ラジオボタン
- `toggle-group` - トグルグループ
- `multi-select` - マルチセレクト
- `checkbox-group` - チェックボックスグループ
- `switch-group` - スイッチグループ

**ブール系:**
- `bool` - ブールトグル

**ファイル系:**
- `file` - ファイルアップロード
- `image` - 画像アップロード

**その他:**
- `color` - カラーピッカー
- `tags` - タグ入力
- `autocomplete` - 自動補完
- `json` - JSON エディタ
- `signature` - 署名
- `map` - マップ選択
- `sortable-list` - ソート可能リスト

### 🔍 フィルター機能 (10 種類)

- `like` - フリーワード検索
- `eq` - 等値選択
- `dropdown` - ドロップダウン
- `toggle-group` - トグルグループ
- `multi-select` - 複数選択
- `bool-toggle` - ブールトグル
- `date-range` - 日付範囲
- `gte` - 以上
- `lte` - 以下
- `entity-picker` - 実体選択

### 📐 レイアウト機能

- グリッドレイアウト（1-4 カラム）
- タブ表示
- アコーディオン
- ウィザード形式
- カード表示
- 統計カード

### ⚙️ バッチ処理

- Cron スケジューリング
- SQL→CSV エクスポート
- SQL コマンド実行
- ストアドプロシージャ実行
- リトライ機構
- 失敗時通知

### 🎣 フックシステム

**前処理:**
- `before_create` - 作成前
- `before_update` - 更新前
- `before_delete` - 削除前

**後処理:**
- `after_create` - 作成後
- `after_update` - 更新後
- `after_delete` - 削除後

### 📤 エクスポート機能

**形式:**
- CSV
- JSON
- PDF

**PDF カスタマイズ:**
- ページサイズ（A4/A3/横長）
- ヘッダーカラー
- 行の交互色
- ページ番号
- 生成日時
- カラム幅調整

## エンティティ一覧

| エンティティ | 説明 |
|------------|------|
| `form_component` | フォーム部品の展示 |
| `filter_demo` | フィルター機能の展示 |
| `layout_demo` | レイアウト機能の展示 |
| `batch_job_demo` | バッチ処理の展示 |
| `hook_demo` | フック処理の展示 |
| `export_demo` | エクスポート機能の展示 |

## ページ一覧

| ページ | 説明 |
|-------|------|
| `/framework-showcase/Page/Overview` | 概要ページ |
| `/framework-showcase/Page/ComponentGallery` | UI 部品ギャラリー |
| `/framework-showcase/Page/LayoutCollection` | レイアウト集 |

## 使い方

### 1. 初期データ投入

```bash
sqlite3 NetYamlForge/projects/framework-showcase/database/framework-showcase.db < NetYamlForge/projects/framework-showcase/database/seeds/01_demo_data.sql
```

### 2. アプリケーション起動

```bash
dotnet run --project NetYamlForge
```

### 3. ブラウザでアクセス

```
http://localhost:5000/framework-showcase/
```

## 関連リンク

- [プロジェクト定義](project.yaml)
- [エンティティ定義](entities/)
- [ページ定義](pages/)
- [バッチ処理定義](jobs/)
- [初期データ](database/seeds/)
