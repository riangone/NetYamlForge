# AI User Simulation — P0 骨架

設計本体: [`../ai-user-simulation.md`](../ai-user-simulation.md)

外部 3 CLI（antigravity / claude code / opencode）が persona を演じ、REST で
`task-management` を「使う」→ 三層観測 → 不変量で Finding を出す実験の最小実行系。

## 前提（実測済み）
- 活躍サービス: `http://127.0.0.1:5001`（dotnet, cwd=`NetYamlForge/`）
- 認証: `Authorization: Bearer <api_token>`（`system.db` 真身 = `NetYamlForge/var/data/system.db`）
- 既存専属アカウントを流用（新規作成なし）:

| persona | CLI | user_name |
|---|---|---|
| PM（俯瞰/建て/期限優先度/エクスポート） | claude code | `taskmgr_manager` |
| DEV（自分のタスクを進める/状態機） | antigravity | `taskmgr_worker1`(+`worker2`) |
| OBS（読み+境界/畸形入力） | opencode | `taskmgr_viewer` |

- `task`/`comment` に `api: readwrite` を付与済み（REST 開放）。
- **既知の設計限界**: `ApiEntityAccessGuard` は entity 級のみ判定。役割/所有者フィルタ無し・
  `app_user_role` は 0 行。→ 越権断言は REST 層では出ない（`no_role_isolation` として回帰監視に留める）。

## 使い方
```bash
cd docs/experiments/ai-user-simulation
bash bin/provision.sh              # 専属アカウントに api_token を発行 -> .tokens.env
bash bin/run_session.sh DEV 5      # Persona=DEV で 1 セッション(5手) 実行（P1 の対象）
python3 bin/check_invariants.py    # 三層突合 -> logs/findings.jsonl
```
外部 CLI に判断させる場合:
```bash
export BRAIN_CMD_DEV='antigravity --stdin'   # プロンプトを stdin で受け {"op":..} を返すCLI
bash bin/run_session.sh DEV 5 --brain cli
```

## 出力
- `logs/observe.jsonl` — 全リクエストの三層観測（status/latency/req/resp）
- `logs/findings.jsonl` — 不変量違反の Finding（severity/layer/rule/detail/evidence）

## 段階
- **P0**（本骨架）: provision + 動作派生 + 単会話 + 不変量チェック。決定論 brain でリンク疎通。
- **P1**: DEV 単角色を CLI brain で回し初回 Finding。
- **P2**: 3 CLI 並走 + jitter 調度（cron/loop）+ 役割衝突。
- **P3**: 評審 AI（Code Reviewer 役）で定級 → 回帰不変量/テスト登録。
