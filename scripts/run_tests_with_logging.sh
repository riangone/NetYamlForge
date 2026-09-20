#!/usr/bin/env bash
# run_tests_with_logging.sh
# Executes dotnet tests for the NetYamlForge project, logs output to a timestamped file,
# and prints the log path. Intended to be used as a reliable background task runner.

set -euo pipefail

# Directory of the repository (assumed script is placed under the repo root)
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Create a logs directory if it doesn't exist
LOGS_DIR="$REPO_ROOT/logs"
mkdir -p "$LOGS_DIR"

# Generate a unique log filename based on datetime and a random suffix
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
RANDOM_SUFFIX=$(head -c4 /dev/urandom | base64 | tr -dc 'a-z0-9')
LOG_FILE="$LOGS_DIR/dotnet_test_${TIMESTAMP}_${RANDOM_SUFFIX}.log"

# Run the tests, tee output to both console and log file
# Using --no-build to speed up when CI already built; remove if needed.
# The script returns the exit code of dotnet test.

echo "Running dotnet test..."

echo "Log: $LOG_FILE"

dotnet test --no-build 2>&1 | tee "$LOG_FILE"
TEST_EXIT_CODE=${PIPESTATUS[0]}

# Print a short summary for the caller
if [ $TEST_EXIT_CODE -eq 0 ]; then
  echo "✅ Tests passed. Log saved to: $LOG_FILE"
else
  echo "❌ Tests failed (exit code $TEST_EXIT_CODE). Log saved to: $LOG_FILE"
fi

exit $TEST_EXIT_CODE
