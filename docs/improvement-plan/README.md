# 改善計画ドキュメント

本ディレクトリは、NetYamlForge を「高速開発可能」から「長期運用可能」へ引き上げるための実行計画をまとめたものです。

## ドキュメント一覧

- `roadmap.md`: 12週間の改善ロードマップ（フェーズ、成果物、KPI、完了条件）
- `workflow.md`: 人/AI 共通の進め方（RFC、タスク分割、PR運用、レビュー規約）
- `quality-gates.md`: 品質ゲート（ビルド、テスト、回帰、可観測性、セキュリティ）
- `ai-task-template.md`: AI 実行向けタスクテンプレートとプロンプト規約
- `templates/rfc-template.md`: RFC作成用テンプレート
- `backlog-wave1.md`: 改善着手用の第1波バックログ（10項目）
- `backlog-wave2.md`: Wave 1 完了後の第2波バックログ
- `backlog-wave3.md`: Wave 2 完了後の第3波バックログ
- `backlog-wave4.md`: Wave 3 完了後の第4波バックログ
- `weekly-ops.md`: 週次運用シート（進行/レビュー/KPI記録）
- `documentation-update-guide.md`: 実装変更時の文書更新判定ルール
- `regression-checklist.md`: 標準回帰チェック手順と記録テンプレート
- `release-readiness-2026-03-03.md`: Wave 1/2 実装後のリリース準備チェック
- `hook-diagnostics-runbook.md`: Hook ErrorCode ベースの障害切り分け手順

## 運用ルール

1. 変更前に `workflow.md` の RFC 要件を満たすこと。
2. PR は `quality-gates.md` の必須ゲートを全て満たすこと。
3. AI に作業依頼する場合は `ai-task-template.md` を必ず使うこと。
4. 週次で `roadmap.md` の進捗を更新すること。
