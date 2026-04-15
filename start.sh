#!/usr/bin/env bash
# NetYamlForge 起動スクリプト
# 主アプリ (5000) と AI サービス (5005) を並列起動し、
# Ctrl+C で両方をクリーンに停止します。

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOG_DIR="$SCRIPT_DIR/logs"
mkdir -p "$LOG_DIR"

# カラー出力
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
CYAN='\033[0;36m'; BOLD='\033[1m'; RESET='\033[0m'

info()  { echo -e "${CYAN}[INFO]${RESET} $*"; }
ok()    { echo -e "${GREEN}[OK]${RESET}   $*"; }
warn()  { echo -e "${YELLOW}[WARN]${RESET} $*"; }
error() { echo -e "${RED}[ERR]${RESET}  $*"; }

# ─────────────────────────────────────────────────────────────
# プロセス管理
# ─────────────────────────────────────────────────────────────
MAIN_PID=""
AI_PID=""

cleanup() {
    echo ""
    info "停止中..."
    [[ -n "$MAIN_PID" ]] && kill "$MAIN_PID" 2>/dev/null && ok "主アプリ停止 (PID $MAIN_PID)"
    [[ -n "$AI_PID"   ]] && kill "$AI_PID"   2>/dev/null && ok "AI サービス停止 (PID $AI_PID)"
    wait "$MAIN_PID" "$AI_PID" 2>/dev/null || true
    info "終了しました"
}
trap cleanup INT TERM

# ─────────────────────────────────────────────────────────────
# ビルド（任意: --build フラグ付き時）
# ─────────────────────────────────────────────────────────────
if [[ "${1:-}" == "--build" ]]; then
    info "ビルド中..."
    dotnet build "$SCRIPT_DIR" -c Release --nologo -q \
        && ok "ビルド成功" \
        || { error "ビルド失敗"; exit 1; }
fi

# ─────────────────────────────────────────────────────────────
# AI サービス起動 (Port 5005)
# ─────────────────────────────────────────────────────────────
info "AI サービスを起動中... (http://localhost:5005)"
dotnet run \
    --project "$SCRIPT_DIR/NetYamlForge.AI.Web/NetYamlForge.AI.Web.csproj" \
    --no-launch-profile \
    -- --urls "http://localhost:5005" \
    > "$LOG_DIR/ai-service.log" 2>&1 &
AI_PID=$!

# AI サービスの起動を待機（最大 30 秒）
info "AI サービスの起動を確認中..."
for i in $(seq 1 30); do
    if curl -sf http://localhost:5005/health > /dev/null 2>&1; then
        ok "AI サービス起動完了 (http://localhost:5005)"
        break
    fi
    if ! kill -0 "$AI_PID" 2>/dev/null; then
        error "AI サービスが異常終了しました。ログ: $LOG_DIR/ai-service.log"
        tail -20 "$LOG_DIR/ai-service.log"
        exit 1
    fi
    printf "."
    sleep 1
done
echo ""

# ─────────────────────────────────────────────────────────────
# 主アプリ起動 (Port 5000)
# ─────────────────────────────────────────────────────────────
info "主アプリを起動中... (http://localhost:5000)"
dotnet run \
    --project "$SCRIPT_DIR/NetYamlForge/NetYamlForge.csproj" \
    --no-launch-profile \
    -- --urls "http://localhost:5000" \
    > "$LOG_DIR/main-app.log" 2>&1 &
MAIN_PID=$!

# 主アプリの起動を待機（最大 30 秒）
info "主アプリの起動を確認中..."
for i in $(seq 1 30); do
    if curl -sf http://localhost:5000 > /dev/null 2>&1; then
        ok "主アプリ起動完了 (http://localhost:5000)"
        break
    fi
    if ! kill -0 "$MAIN_PID" 2>/dev/null; then
        error "主アプリが異常終了しました。ログ: $LOG_DIR/main-app.log"
        tail -20 "$LOG_DIR/main-app.log"
        cleanup
        exit 1
    fi
    printf "."
    sleep 1
done
echo ""

# ─────────────────────────────────────────────────────────────
# 起動完了メッセージ
# ─────────────────────────────────────────────────────────────
echo ""
echo -e "${BOLD}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${RESET}"
echo -e "${GREEN}${BOLD}  起動完了${RESET}"
echo -e "${BOLD}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${RESET}"
echo -e "  主アプリ     ${CYAN}http://localhost:5000${RESET}"
echo -e "  AI サービス  ${CYAN}http://localhost:5005${RESET}"
echo ""
echo -e "  AI ウィジェット（埋め込み）: ${CYAN}http://localhost:5000${RESET} → 右下ボタン"
echo -e "  AI 独立ページ:               ${CYAN}http://localhost:5000/ai/chat${RESET}"
echo -e "  AI 管理ダッシュボード:        ${CYAN}http://localhost:5005${RESET}"
echo ""
echo -e "  ログ出力先: ${LOG_DIR}/"
echo -e "    - main-app.log"
echo -e "    - ai-service.log"
echo ""
echo -e "${BOLD}  Ctrl+C で両プロセスを停止${RESET}"
echo -e "${BOLD}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${RESET}"
echo ""

# ─────────────────────────────────────────────────────────────
# プロセス監視ループ（どちらかが落ちたら警告）
# ─────────────────────────────────────────────────────────────
while true; do
    if [[ -n "$AI_PID" ]] && ! kill -0 "$AI_PID" 2>/dev/null; then
        warn "AI サービスが停止しました (ログ: $LOG_DIR/ai-service.log)"
        AI_PID=""
    fi
    if [[ -n "$MAIN_PID" ]] && ! kill -0 "$MAIN_PID" 2>/dev/null; then
        warn "主アプリが停止しました (ログ: $LOG_DIR/main-app.log)"
        MAIN_PID=""
    fi
    if [[ -z "$AI_PID" && -z "$MAIN_PID" ]]; then
        error "両プロセスが停止しました"
        break
    fi
    sleep 5
done
