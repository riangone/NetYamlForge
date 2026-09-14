# Memo App

> `--init-project` で自動生成されたサブプロジェクトです。

## ディレクトリ構成

```
memo-app/
├── project.yaml          # プロジェクト設定（DB接続・機能フラグ）
├── dashboard.yml         # ダッシュボード（統計カード・チャート）
├── config/
│   ├── home-page.yml     # ホームページのヒーロー・クイックアクション
│   ├── layout.yml        # ナビゲーション・ヘッダー・フッター
│   └── i18n.yml          # 翻訳キー（en-US / zh-CN / ja-JP / ko-KR）
├── entities/             # ★ エンティティ定義（手動編集 or scaffold 生成）
├── entities.generated/   # scaffold 自動生成（上書き可）
├── pages/                # カスタムページ定義（YAML）
├── database/             # SQLite DB ファイル
├── views/                # Razor カスタムビュー（.cshtml）
└── docs/
    └── README-ja.md      # このファイル
```

## クイックスタート

```bash
# 1. DB からエンティティ YAML を自動生成
dotnet run -- --scaffold-entities --project=memo-app

# 2. ブラウザで確認
# → http://localhost:5000/memo-app
# → http://localhost:5000/memo-app/Dashboard
```

## エンティティ定義の書き方

`entities/my_entity.yml` の最小構成:

```yaml
entities:
  my_entity:
    table: MyTable
    key: Id
    displayName: My Entity
    columns:
      Id:   { type: int, identity: true, label: ID, sortable: true }
      Name: { type: string, required: true, label: Name, searchable: true }
    forms:
      Name: { type: string, required: true, label: Name, editable: true }
    filters:
      Name: { type: like, label: Name }
```

## カスタマイズ例

### ナビに項目を追加（`config/layout.yml`）

```yaml
navigation:
  items:
    - label: My Page
      url: /memo-app/Page/MyPage
      icon: 📋
```

### ダッシュボードにチャートを追加（`dashboard.yml`）

```yaml
charts:
  - id: monthly
    title: Monthly Trend
    type: bar
    source: "SELECT strftime('%Y-%m', CreatedAt) AS Month, COUNT(*) AS Count FROM MyTable GROUP BY Month ORDER BY Month DESC LIMIT 12"
    xAxis: Month
    yAxis: Count
```

### i18n キーを追加（`config/i18n.yml`）

```yaml
translations:
  my.custom.label:
    en-US: My Label
    zh-CN: 我的标签
    ja-JP: 私のラベル
    ko-KR: 내 레이블
```

## トラブルシューティング

| 症状 | 対処 |
|------|------|
| entities/ が空 | `--scaffold-entities` を実行 |
| YAML 構文エラー | `dotnet test` でバリデーション確認 |
| 画面が反映されない | サーバー再起動 (`dotnet run`) |
| フックを追加したい | `dotnet run -- --scaffold-hook --name=MyHook --project=memo-app` |

詳細ドキュメント: `docs/developer-tutorial-ja.md` / `docs/COMMON_HOOKS.md`
