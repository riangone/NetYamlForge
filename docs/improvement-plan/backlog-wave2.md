# 改善バックログ（Wave 2）

Wave 1 完了後の次期改善項目です。

## W2-1: YAML スキーマ検証の拡張
- 種別: feat
- ステータス: Completed (2026-03-03)
- 目的: `project.yaml` だけでなく `layout.yml` / entities ディレクトリ構成の整合も検証
- 受け入れ条件:
1. 不正キーと型不一致を起動時に検出
2. ファイルパス・キー位置をエラーメッセージに含む

## W2-2: Hook エラー分類ログ
- 種別: refactor
- ステータス: Completed (2026-03-03)
- 目的: コンパイル/実行/依存解決エラーを分類して可観測性向上
- 受け入れ条件:
1. ErrorCode 付きログ
2. project/hook 名で検索可能

## W2-3: 設定診断ページの差分表示
- 種別: feat
- ステータス: Completed (2026-03-03)
- 目的: ベース設定とプロジェクト上書き差分を可視化
- 受け入れ条件:
1. base/project/effective の3表示
2. 差分箇所ハイライト

## W2-4: 回帰テスト拡張（一覧/ページング）
- 種別: test
- ステータス: Completed (2026-03-03)
- 目的: 一覧の主要回帰を自動化
- 受け入れ条件:
1. フィルタ適用・clear・count 表示をテスト
2. 既存3テストに加え5ケース以上

## W2-5: Slow Query メトリクス出力
- 種別: feat
- ステータス: Completed (2026-03-03)
- 目的: warning ログだけでなく件数メトリクス化
- 受け入れ条件:
1. 操作別 slow count を記録
2. 閾値変更時に再起動不要（設定化）
- 備考: `DYNAMICCRUD_SLOW_QUERY_MS` / `DYNAMICCRUD_SLOW_QUERY_SUMMARY_MS` は実行中に再読込
