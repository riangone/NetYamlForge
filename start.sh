#!/bin/bash

PROJECT_DIR="/home/ubuntu/ws/NetYamlForge/NetYamlForge"
LOG_FILE="/home/ubuntu/ws/NetYamlForge/logs/netyamlforge.log"
PID_FILE="/home/ubuntu/ws/NetYamlForge/netyamlforge.pid"

# Check if already running
if [ -f "$PID_FILE" ]; then
    PID=$(cat "$PID_FILE")
    if kill -0 "$PID" 2>/dev/null; then
        echo "NetYamlForge is already running (PID: $PID)"
        exit 1
    else
        echo "Stale PID file found. Removing."
        rm -f "$PID_FILE"
    fi
fi

mkdir -p "$(dirname "$LOG_FILE")"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] Starting NetYamlForge..." >> "$LOG_FILE"

cd "$PROJECT_DIR" || { echo "Cannot change to project directory: $PROJECT_DIR"; exit 1; }

ASPNETCORE_URLS=http://localhost:5001 nohup /home/ubuntu/.dotnet/dotnet bin/Release/net10.0/NetYamlForge.dll >> "$LOG_FILE" 2>&1 &

APP_PID=$!
echo $APP_PID > "$PID_FILE"

echo "NetYamlForge started (PID: $APP_PID)"
echo "Log: $LOG_FILE"
