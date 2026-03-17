# ドキュメント更新ガイド

## 1. 目的

実装変更と文書更新のズレを防ぎ、第三者（人/AI）が差分意図を追跡できる状態を維持する。

## 2. 更新判定ルール

以下のいずれかに該当する場合、ドキュメント更新を必須とする。

1. 公開挙動が変わる（UI、API、設定キー、既定値）
2. 運用手順が変わる（起動、デプロイ、監視、障害対応）
3. 品質ゲートや作業フローが変わる（PR要件、回帰手順）
4. 新しい制約/既知制限を導入した

## 3. どの文書を更新するか

- 仕様変更: `docs/` の対象機能ガイド
- 運用変更: `docs/improvement-plan/workflow.md` / `quality-gates.md`
- 計画変更: `docs/improvement-plan/roadmap.md` / `backlog-wave1.md`
- AI依頼方式変更: `docs/improvement-plan/ai-task-template.md`

## 4. PRでの記載ルール

PR本文に以下を必ず記載する。

1. `Documentation impact: Yes/No`
2. `Updated docs:` 更新ファイルパス一覧
3. `Reason:` 更新不要の場合の理由

## 5. レビュー観点

1. 実装差分と文書差分が対応しているか
2. 旧手順が残っていないか
3. 新規参入者が再現可能な記述か
4. 日本語文書として意味が一意か

## 6. 最小更新テンプレート

```md
## Documentation impact
- Yes

## Updated docs
- docs/improvement-plan/quality-gates.md
- docs/improvement-plan/documentation-update-guide.md

## Reason
- slow query 閾値環境変数 `DYNAMICCRUD_SLOW_QUERY_MS` を追加したため
```
