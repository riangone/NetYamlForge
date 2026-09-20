#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/NetYamlForge"
PORT="${PORT:-5001}"
LOG_FILE="$SCRIPT_DIR/var/log/netyamlforge.log"
PID_FILE="$SCRIPT_DIR/var/run/netyamlforge.pid"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] === Restarting NetYamlForge ==="

# --- Step 1: Force stop any running instance ---
systemctl --user stop netyamlforge 2>/dev/null || true

# Via PID file
if [ -f "$PID_FILE" ]; then
    PID=$(cat "$PID_FILE")
    if kill -0 "$PID" 2>/dev/null; then
        echo "Stopping PID $PID (from PID file)..."
        kill "$PID"
        for i in $(seq 1 10); do
            kill -0 "$PID" 2>/dev/null || break
            sleep 1
        done
        kill -0 "$PID" 2>/dev/null && kill -9 "$PID" 2>/dev/null
    fi
    rm -f "$PID_FILE"
fi

# Also kill any remaining dotnet process running NetYamlForge.dll (belt-and-suspenders)
LEFTOVER_PIDS=$(pgrep -f "NetYamlForge.dll" 2>/dev/null)
if [ -n "$LEFTOVER_PIDS" ]; then
    echo "Force-killing leftover processes: $LEFTOVER_PIDS"
    kill -9 $LEFTOVER_PIDS 2>/dev/null
fi

# Wait for port 5001 to be released
for i in $(seq 1 10); do
    ss -tlnp | grep -q ":${PORT}" || break
    sleep 1
done
if ss -tlnp | grep -q ":${PORT}"; then
    echo "ERROR: Port ${PORT} still in use after 10s. Aborting."
    exit 1
fi

echo "All processes stopped."

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

cd "$PROJECT_DIR" || { echo "Cannot cd to $PROJECT_DIR"; exit 1; }

if systemctl --user status >/dev/null 2>&1; then
    echo "Starting via systemd-run --user (netyamlforge.service)..."
    systemd-run --user --unit=netyamlforge \
      --setenv=ASPNETCORE_URLS=http://localhost:${PORT} \
      --setenv=ASPNETCORE_CONTENTROOT="$PROJECT_DIR" \
      --working-directory="$PROJECT_DIR" \
      /home/ubuntu/.dotnet/dotnet "$PROJECT_DIR/bin/Release/net10.0/NetYamlForge.dll" >> "$LOG_FILE" 2>&1
else
    export ASPNETCORE_URLS=http://localhost:${PORT}
    export ASPNETCORE_CONTENTROOT="$PROJECT_DIR"
    nohup /home/ubuntu/.dotnet/dotnet "$PROJECT_DIR/bin/Release/net10.0/NetYamlForge.dll" >> "$LOG_FILE" 2>&1 &
    APP_PID=$!
    echo $APP_PID > "$PID_FILE"
    disown $APP_PID 2>/dev/null || true
fi

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

