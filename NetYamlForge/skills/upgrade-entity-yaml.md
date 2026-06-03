---
name: エンティティ更新
icon: 🔄
description: DB スキーマ変更をエンティティ YAML にマージ
needsInput: true
inputPlaceholder: プロジェクト名を入力...
order: 5
---

DB スキーマの変更（列追加・型変更など）を既存のエンティティ YAML に安全にマージしてください。

## コマンド

```bash
dotnet run -- --upgrade-entity-yaml --project=<name>
```

## 動作

1. DB スキーマを再解析して `entities.generated/` を更新
2. `entities/` の既存 YAML に**新しい列・フォーム項目のみ**を追記
3. カスタム設定（バリデーション・ラベル・フック設定など）は**上書きしない**

## 使いどころ

- テーブルに新しいカラムを追加した後
- カラムの型や `NOT NULL` 制約を変更した後
- `--scaffold-entities` を使うと既存の設定が消えてしまう場合

## 注意

- `entities/` 内の手動編集は保持されます
- 削除されたカラムは YAML から自動削除されません（手動で削除してください）
- 実行後に `dotnet build` でエラーがないか確認してください

プロジェクト名:
