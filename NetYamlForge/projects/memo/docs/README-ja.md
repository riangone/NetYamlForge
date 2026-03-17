# Memo Notebook Starter

`memo` は新規サブプロジェクト作成時の参照用スターターです。

## できること

- Notebook（メモ帳）管理
- Memo（メモ）CRUD
- Status / Pinned / Notebook での絞り込み
- Dashboard で件数可視化

## 主なURL

- `/memo` : プロジェクトホーム
- `/memo/Dashboard` : ダッシュボード
- `/memo/DynamicEntity/Index?entity=memo` : メモ一覧
- `/memo/DynamicEntity/Index?entity=notebook` : メモ帳一覧

## DB再初期化

```bash
sqlite3 projects/memo/database/memo.db < projects/memo/database/init.sql
```

## 参考ポイント

- `project.yaml` : 最小プロジェクト定義
- `entities/` : 実践的な CRUD/Filter/Link 設定
- `dashboard.yml` : すぐ使える KPI/グラフ定義
- `config/home-page.yml` : プロジェクト専用ホーム構成
