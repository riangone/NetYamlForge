# 改善バックログ（Wave 3）

Wave 2 完了後の運用安定化フェーズ。

## W3-1: Config diagnostics の差分フィルタ
- 種別: feat
- ステータス: Completed (2026-03-03)
- 目的: 差分のない項目を非表示にし、診断速度を上げる
- 受け入れ条件:
1. `Only changed` トグルを追加
2. 差分件数をヘッダ表示

## W3-2: Slow query メトリクスの定期サマリ
- 種別: feat
- ステータス: Completed (2026-03-03)
- 目的: 累積カウンタを定期的に可視化
- 受け入れ条件:
1. 一定間隔で operation/entity 別集計ログを出力
2. 0件時はログ抑制

## W3-3: count/paging の回帰テスト（統合）
- 種別: test
- ステータス: Completed (2026-03-03)
- 目的: 一覧周りの主要退行を自動検知
- 受け入れ条件:
1. count=true 伝播テスト
2. pageSize / sort / cursor の組合せテスト

## W3-4: Hook 診断ドキュメント
- 種別: docs
- ステータス: Completed (2026-03-03)
- 目的: ErrorCode ベースの調査手順を標準化
- 受け入れ条件:
1. ErrorCode 一覧と対処手順を記載
2. runbook 形式で再現手順を添付

## W3-5: 手動スモーク実施記録の定着
- 種別: ops
- ステータス: Completed (2026-03-03)
- 目的: リリース前に必ず3プロジェクトで手動確認する運用を固定
- 受け入れ条件:
1. 週次で `release-readiness` 文書を更新
2. pass/fail と根拠 URL を残す
