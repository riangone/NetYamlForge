# 改善バックログ（Wave 4）

Wave 3 完了後の「運用可観測性とフロント状態整合」フェーズ。

## W4-1: TraceId のリクエスト統一
- 種別: feat
- ステータス: Completed (2026-03-03)
- 目的: エラー/問い合わせ時に同一IDでログと画面を突合できるようにする
- 受け入れ条件:
1. すべてのレスポンスに `X-Trace-Id` を付与
2. サーバ受信時に `X-Trace-Id` があれば優先採用
3. request logging に `TraceId` を出力

## W4-2: DynamicEntity 一覧の状態URL固定
- 種別: feat
- ステータス: Completed (2026-03-03)
- 目的: filter/sort/page の状態を URL で再現可能にする
- 受け入れ条件:
1. 再読み込み後も一覧状態を復元
2. 「戻る」操作で直前状態を復元

## W4-3: Clear/再検索の E2E 回帰追加
- 種別: test
- ステータス: Completed (2026-03-03)
- 目的: 既知のクリア不具合（picker/foreignKey）を自動検知する
- 受け入れ条件:
1. Clear 実行で picker hidden 値が空になる
2. count 表示が再検索後も維持される
