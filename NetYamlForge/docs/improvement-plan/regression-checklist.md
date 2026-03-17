# 回帰チェックリスト（標準）

## 実行タイミング

1. CRUDロジック変更時
2. フィルタ/一覧/UI変更時
3. Hook/設定ローダー変更時
4. リリース前

## 最小チェック項目

1. 一覧初期表示
2. 検索（キーワード）
3. フィルタ適用（dropdown/range/date-range）
4. フィルタクリア（hidden/display/chips の同期）
5. ページング（Prev/Next/番号）
6. 総件数表示（Total）
7. 新規作成（page/modal）
8. 編集（page/modal）
9. 削除（確認ダイアログ経由）
10. FK picker 選択/解除
11. Hook 実行（before/after）

## プロジェクト別スモーク

- `chinook`: 一覧 + 編集 + Hook 対象エンティティ
- `blog`: 投稿作成 + スラグ生成 Hook
- `northwind-sqlite3-ops`: 受注/明細の Hook バリデーション

## 記録テンプレート

```md
### Regression result
- Scope: <変更機能>
- Project: <project>
- Commit: <sha>

1. list: pass/fail
2. filter: pass/fail
3. clear: pass/fail
4. paging: pass/fail
5. total: pass/fail
6. create/edit/delete: pass/fail
7. picker: pass/fail
8. hooks: pass/fail

Notes:
- 
```
