# NetYamlForge 運用チェックリスト（日本語）

## 1. 変更前チェック

1. 対象プロジェクトを明確化（例: `chinook`, `todo`）
2. 変更種別を分類（YAML / Hook / SQL / UI / 認証）
3. 影響範囲の画面 URL を列挙
4. ロールバック方法を決める（設定差し戻し/コード差し戻し）

---

## 2. 変更実施チェック

1. `dotnet build` 成功
2. YAML 変更時は起動時エラーが 0 件
3. Hook 変更時はコンパイルエラーが 0 件
4. SQL 変更時は対象 DB 方言で実行確認
5. 必要なドキュメント更新（Yes/No）を記録

---

## 3. 最小回帰チェック

対象 URL で以下を確認:

1. 一覧初期表示
2. 検索
3. フィルタ適用
4. フィルタクリア
5. ページング
6. 作成
7. 編集
8. 削除
9. FK picker（選択/解除）
10. Hook（before/after）
11. Dashboard（カード/グラフ）
12. 必要時: `Page/<pageName>` カスタムページ

---

## 4. セキュリティ・安全性チェック

1. シークレットをコミットしていない
2. SQL 文字列連結の変更箇所をレビュー済み
3. 管理者機能が非管理者で利用不可
4. 監査ログに主要操作が記録される

---

## 5. リリース前チェック

1. 主要プロジェクトでスモーク実施（最低 2 プロジェクト）
2. `logs/app-YYYYMMDD.log` に重大エラーなし
3. 既知不具合の再発確認
4. 変更内容を `CHANGELOG.md` へ反映

---

## 6. 結果記録テンプレート

```md
### Release Check Result
- Date: YYYY-MM-DD
- Scope: <変更内容>
- Project(s): <project list>
- Build: pass/fail
- Smoke: pass/fail
- Risk: low/medium/high

Checklist:
1. list: pass/fail
2. filter: pass/fail
3. paging: pass/fail
4. create/edit/delete: pass/fail
5. hooks: pass/fail
6. dashboard/page: pass/fail

Notes:
- 
```
