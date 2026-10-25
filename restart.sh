#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/NetYamlForge"
PORT="${PORT:-5001}"
LOG_FILE="$SCRIPT_DIR/var/log/netyamlforge.log"
PID_FILE="$SCRIPT_DIR/var/run/netyamlforge.pid"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] === Restarting NetYamlForge ==="
# 2026-10-25 改为由 systemd (netyamlforge.service) 管理：先构建，构建成功后再重启服务

# --- Step 2: Rebuild to pick up latest source changes ---
echo "Building latest Release DLL..."
BUILD_LOG=$(dotnet build "$PROJECT_DIR/NetYamlForge.csproj" -c Release 2>&1)
BUILD_EXIT=$?
if echo "$BUILD_LOG" | grep -q "Build succeeded"; then
    echo "Build succeeded."
else
    echo "ERROR: Build failed. Output:"
    echo "$BUILD_LOG" | tail -20
    exit 1
fi

# --- Step 3: Start with the new DLL ---
mkdir -p "$(dirname "$LOG_FILE")" "$(dirname "$PID_FILE")"
echo "[$(date '+%Y-%m-%d %H:%M:%S')] Starting NetYamlForge..." >> "$LOG_FILE"

echo "Restarting netyamlforge.service (systemd, 开机自启)..."
sudo systemctl restart netyamlforge

echo "Waiting for port ${PORT} to listen..."
READY=0
for i in $(seq 1 30); do
    if ss -tlnp | grep -q ":${PORT}"; then
        READY=1
        break
    fi
    sleep 1
done

APP_PID=$(pgrep -f "NetYamlForge.dll" | head -n 1)
[ -n "$APP_PID" ] && echo "$APP_PID" > "$PID_FILE"

if [ $READY -eq 1 ]; then
    echo "NetYamlForge is up and listening on port ${PORT} (PID: ${APP_PID:-unknown})."
else
    echo "WARNING: Port ${PORT} not yet detected after 30s. Check logs at $LOG_FILE."
fi
echo "Log: $LOG_FILE"

