#!/bin/bash

PID_FILE="/home/ubuntu/ws/NetYamlForge/var/run/netyamlforge.pid"
LOG_FILE="/home/ubuntu/ws/NetYamlForge/var/log/netyamlforge.log"

if [ ! -f "$PID_FILE" ]; then
    echo "PID file not found. NetYamlForge may not be running."
    exit 1
fi

PID=$(cat "$PID_FILE")

if ! kill -0 "$PID" 2>/dev/null; then
    echo "Process $PID is not running. Removing stale PID file."
    rm -f "$PID_FILE"
    exit 1
fi

echo "Stopping NetYamlForge (PID: $PID)..."
kill "$PID"

# Wait up to 10 seconds for graceful shutdown
for i in $(seq 1 10); do
    if ! kill -0 "$PID" 2>/dev/null; then
        break
    fi
    sleep 1
done

# Force kill if still running
if kill -0 "$PID" 2>/dev/null; then
    echo "Process did not stop gracefully, sending SIGKILL..."
    kill -9 "$PID"
fi

rm -f "$PID_FILE"
echo "[$(date '+%Y-%m-%d %H:%M:%S')] NetYamlForge stopped." >> "$LOG_FILE"
echo "NetYamlForge stopped."
