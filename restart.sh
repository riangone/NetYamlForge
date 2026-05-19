#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/NetYamlForge"
LOG_FILE="$SCRIPT_DIR/logs/netyamlforge.log"
PID_FILE="$SCRIPT_DIR/netyamlforge.pid"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] === Restarting NetYamlForge ==="

# --- Step 1: Force stop any running instance ---
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
    ss -tlnp | grep -q ':5001' || break
    sleep 1
done
if ss -tlnp | grep -q ':5001'; then
    echo "ERROR: Port 5001 still in use after 10s. Aborting."
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
mkdir -p "$(dirname "$LOG_FILE")"
echo "[$(date '+%Y-%m-%d %H:%M:%S')] Starting NetYamlForge..." >> "$LOG_FILE"

cd "$PROJECT_DIR" || { echo "Cannot cd to $PROJECT_DIR"; exit 1; }

ASPNETCORE_URLS=http://localhost:5001 /home/ubuntu/.dotnet/dotnet bin/Release/net10.0/NetYamlForge.dll >> "$LOG_FILE" 2>&1 &

APP_PID=$!
echo $APP_PID > "$PID_FILE"

echo "NetYamlForge started (PID: $APP_PID)"
echo "Log: $LOG_FILE"
