# サブプロジェクト作成時のAI指示ガイド

> このドキュメントは、NetYamlForge フレームワーク上で新しいサブプロジェクトを
> AI（Claude Code）に作らせるときの最適な指示パターンをまとめたものです。

---

## 基本形（最小限の指示）

```
/add-entity

プロジェクト名: <name>
DBタイプ: sqlite
概要: <1行で何を管理するシステムか>

エンティティ一覧:
- <entity1>: <列一覧と簡単な説明>
- <entity2>: ...

要件:
- <機能要件 箇条書き>
```

これだけで `/add-entity` スキルがステップバイステップでガイドします。

---

## 実践例

```
/add-entity

プロジェクト名: inventory
DBタイプ: sqlite
概要: 倉庫在庫管理システム（日本語UI）

エンティティ:
- product: id/name/sku/category_id/price/stock_qty
- category: id/name
- stock_movement: id/product_id/quantity/type(in/out)/note/created_at

要件:
- stock_qty が 0 未満にならないようにバリデーション
- stock_movement 登録時に product.stock_qty を自動更新
- category は product から参照（外部キー）
- 認証必要
- 言語: ja-JP
```

---

## 精度を上げたい場合に追記する情報

| 追加情報 | 書き方の例 | 効果 |
|--------|-----------|------|
| 既存DBがある | `既存DB: database/inventory.db（スキャフォールドして）` | AIが `--scaffold-entities` を先に実行する |
| 承認フロー | `stock_movement の type=out は manager 承認必要` | aiHints.approvalWorkflow が設定される |
| 他プロジェクトとDB共有 | `northwind-sqlite3 と同じDBを使う` | sharedDatabase が設定される |
| 保護エンティティ | `stock_movement は直接編集禁止` | protectedEntities が設定される |
| カスタムダッシュボード | `ダッシュボードに在庫切れ件数・入出庫合計を表示` | dashboard.yml も生成される |
| 多言語 | `言語: zh-CN（中国語メイン）` | aiHints.primaryLanguage と displayNameI18n が設定される |

---

## AIへの追加一言（慎重に動かしたい場合）

指示の末尾に以下を付けると、AIがより丁寧に作業します：

```
作業前に CLAUDE.md の決定木を確認し、
各ステップで /framework-check を実行してください。
コードを書く前に計画を提示してください。
```

---

## 避けるべき指示パターン

| NG パターン | 理由 |
|-----------|------|
| 「在庫管理アプリを作って」だけ | 必要情報が不足してAIが推測で埋める |
| 「SQLで直接テーブルを作って」 | DCS003エラー（直接接続生成の禁止）になる可能性がある |
| 「Adminロールのみアクセス可能に」 | DCS004エラー（ロール名ハードコード）になる可能性。`UserRoles.Admin` を使うよう明示する |
| エンティティを後から大量追加 | project.yaml のナビゲーションとの整合が崩れやすい。最初に全エンティティを宣言する |

---

## 利用可能なスキル一覧

| コマンド | 使うタイミング | 内容 |
|---------|-------------|------|
| `/add-entity` | エンティティ追加・サブプロジェクト新規作成時 | YAML作成 → ナビゲーション登録 → フック → ビルドの全手順をガイド |
| `/add-hook` | バリデーション・変換・通知処理を追加するとき | 既存フック確認 → スキャフォールド → 実装 → DI登録 → テストの全手順 |
| `/framework-check` | 変更前・コミット前の確認 | 決定木・DCSビルドエラー・禁止パターン・テストの一括チェック |

---

## AIが自動でやってくれること

- `project.yaml` の生成（name / database / layout / aiHints）
- `entities/<name>.yml` の生成（columns / forms / hooks）
- ナビゲーション登録
- `--scaffold-hook --with-tests` によるフック+テストの雛形生成
- Roslyn アナライザー（DCS001–DCS004）によるビルド時の禁止パターン検出
- `YamlSchemaValidationTests` による YAML 形式の自動検証

## AIがやらないこと（人間が確認・指定すること）

- DB の初期データ（シードデータ）の投入
- 本番環境へのデプロイ設定
- `appsettings.json` の接続文字列変更
- 実際のDBファイルの作成（`dotnet run` で自動初期化される）
