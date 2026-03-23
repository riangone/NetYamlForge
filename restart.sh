#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Restarting NetYamlForge..."

# Stop if running (ignore errors if not running)
bash "$SCRIPT_DIR/stop.sh" 2>/dev/null || true

# Brief pause to ensure port is released
sleep 2

# Start
bash "$SCRIPT_DIR/start.sh"
