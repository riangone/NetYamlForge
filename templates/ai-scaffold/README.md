# --ai-scaffold: 検証ゲート付きサブプロジェクト生成パイプライン

## これは何のためにあるか

「26個のAI生成子プロジェクトのうち完璧に動くものが一つも無い」という問題への対策。
原因は AI が賢くないことではなく、**生成の粒度が大きすぎて、生成→検証→是正のループが無かったこと**。
このコマンドは、その反省を直接コード化したものです。

設計原則（このまま実装されています）:

1. **AI にコードを書かせない。書かせるのは YAML（構造化 Spec）だけ。**
   自由記述のコード生成はバグ面が無限大だが、Spec は宣言的・有限フィールド・機械検証可能。
2. **Spec → 実 DB スキーマ → entities YAML、の順で決定的に変換する。**
   entities YAML は既存の `EntityYamlScaffolder` が実 DB スキーマから逆生成するため、
   「YAML とスキーマがズレる」というクラスのバグがそもそも起こり得ない。
3. **各ステップの後に強制ゲートを置く。** どこかで失敗したら即座に非ゼロ終了し、
   生成物を「完成」扱いにしない。
4. **AI の自己レビューはあれば良いおまけ（`--ai-review`）。** 判定根拠を自己申告だけに置かない。

## 使い方

```bash
dotnet run --project NetYamlForge.Tooling -- --ai-scaffold \
  --spec=templates/ai-scaffold/golden-template.spec.yaml
```

オプション:

- `--ai-review` : 生成後、ローカルにインストール済みの CLI チェーン（opencode / antigravity / claude 等、
  `CliChainService` が対応するもの）に一度だけ短いセルフレビューを依頼する。CLI が無い/失敗しても
  パイプラインは止まらない（非致命的なおまけステップ）。
- `--json` : 結果を `CliScaffoldResult` の JSON として stdout に出力する（CI 連携用）。

## パイプラインの中身（ゲート）

| # | ステップ | 何をするか | 失敗したら |
|---|---------|-----------|-----------|
| gate 1 | spec 静的検証 | プロジェクト名・テーブル名重複・PK有無・列型許可リスト・FK参照先の実在性を **DB/ファイルに触れる前に** 検証 | 即終了、生成物ゼロ |
| step 1 | `init-project` | プロジェクト雛形（project.yaml / config / views / StarterOverview ページ）を生成 | 既存プロジェクトなら雛形再生成をスキップ |
| step 2 | スキーマ投入 | spec の列定義から `CREATE TABLE` を機械的に組み立てて sqlite に投入（AI にも人間にも SQL を書かせない） | DDL 生成失敗で終了 |
| gate 2 | `scaffold-entities` | 実 DB スキーマ（PRAGMA table_info / foreign_key_list）から entities YAML を逆生成。FK は自動でエンティティ参照になる | 逆生成失敗で終了 |
| step 3 | `scaffold-hook` | spec の `hooks[]` ごとに hook 雛形 + テストを生成 | 個別失敗で終了 |
| step 4 | `scaffold-missing-hooks` | entities に対する CRUD hook の抜け漏れを自動補完 | 非致命（警告メッセージのみ） |
| step 5 | `scaffold-batch-job` | spec の `batchJobs[]` ごとに BatchJob 雛形（yml + SQL）を生成 | 個別失敗で終了 |
| gate 3 | `validate-project` | nav リンク切れ・seed データ有無・home-page プレースホルダー・重複 nav item を静的検証 | 検証失敗で終了（人手のレビュー待ちにしない） |
| gate 4 | 受け入れ基準の機械照合 | spec の `acceptanceCriteria[]` を、生成済みファイル一覧との文字列一致で機械的に ✅/⚠️ 判定 | 常に完走（⚠️ は人手確認を促すだけ） |
| optional | AI セルフレビュー | `--ai-review` 指定時のみ、CLI チェーンに簡潔なレビューを依頼 | 失敗しても非致命 |

## Spec ファイルの書き方

`golden-template.spec.yaml` を参照。最低限必要なもの:

```yaml
project: my-project        # ケバブケース
displayName: My Project
dbType: sqlite              # 現時点では sqlite のみ対応
entities:
  - table: <table_name>
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
      - { name: <col>, type: text|integer|real|numeric|blob|boolean|datetime, notNull: true }
      - name: <fk_col>
        type: integer
        foreignKey: { table: <other_table>, column: id }
hooks:
  - { name: <PascalCaseHookName>, entity: <table_name> }
batchJobs:
  - { name: <snake_case_job_name> }
acceptanceCriteria:
  - "<人手/CIが最終確認すべき項目を自然文で>"
```

## 新しい子プロジェクトを作るときの手順

1. `templates/ai-scaffold/golden-template.spec.yaml` をコピーして、entities/hooks/batchJobs/
   acceptanceCriteria を書き換える（AI に手伝わせる場合も、書かせるのはこの Spec だけに限定する）。
2. `--ai-scaffold --spec=<your-spec>.yaml` を実行し、4つのゲートを全て通過することを確認する。
3. 生成された hook のビジネスロジック（`Hooks/*.cs`）と、entities YAML の `hooks:` セクションへの
   配線（`beforeCreate` / `beforeUpdate` 等）だけを人手で埋める。DB スキーマや CRUD 定義そのものは
   生成物をそのまま使い、手で書き直さない。
4. `dotnet build` → `dotnet test --filter <ProjectName>` → アプリを起動して目視確認、の順で仕上げる。

## 金標サンプル: golden-template（Mini Helpdesk）

`golden-template.spec.yaml` を実行すると `NetYamlForge/projects/golden-template` に
`ticket` / `ticket_comment` の 2 エンティティ + `TicketStatusGuard` フック雛形 +
`stale_ticket_reminder` バッチジョブ雛形が生成され、`validate-project` まで通過した状態になります。
以後の子プロジェクトは、これを「一番安定して動く型」として差分ベースで作ることを推奨します
（`entities/*.yml` の書式・hook の配線方法・batchJob の SQL テンプレートの参考実装として使う）。
