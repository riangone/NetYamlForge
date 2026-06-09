#!/bin/bash
# Run E2E tests for all roles, capture results, and output a bug report

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_FILE="$SCRIPT_DIR/test-results/bug-report-$(date '+%Y%m%d-%H%M%S').md"

mkdir -p "$SCRIPT_DIR/test-results"

echo "================================================"
echo " NetYamlForge E2E Test Run - $(date)"
echo "================================================"

cd "$SCRIPT_DIR"

# Check app is running
if ! curl -s -o /dev/null -w "%{http_code}" http://localhost:5001/ | grep -qE "^[234]"; then
  echo "ERROR: App is not running on port 5001!"
  exit 1
fi

echo "App is running. Starting tests..."
echo ""

# Run playwright tests, capture output
npx playwright test \
  --config=playwright.config.js \
  --reporter=list,json \
  2>&1 | tee /tmp/playwright-output.txt

EXIT_CODE=${PIPESTATUS[0]}

echo ""
echo "================================================"
echo " Test Summary"
echo "================================================"

# Parse results
PASSED=$(grep -c "✓\|passed" /tmp/playwright-output.txt || echo 0)
FAILED=$(grep -c "✗\|failed\|×" /tmp/playwright-output.txt || echo 0)

echo "Exit code: $EXIT_CODE"
grep -E "(passed|failed|skipped)" /tmp/playwright-output.txt | tail -5

# Generate bug report
cat > "$REPORT_FILE" << REPORT
# NetYamlForge E2E Bug Report
**Date**: $(date)
**App URL**: http://localhost:5000

## Test Results Summary

\`\`\`
$(grep -E "(✓|✗|×|passed|failed|skipped|PASS|FAIL)" /tmp/playwright-output.txt | head -50)
\`\`\`

## Failed Tests Detail

\`\`\`
$(grep -A 10 "✗\|FAILED\|Error:" /tmp/playwright-output.txt | head -100)
\`\`\`

## Screenshots
Screenshots are in: $SCRIPT_DIR/test-results/

REPORT

echo ""
echo "Bug report saved to: $REPORT_FILE"
echo ""

if [ $EXIT_CODE -ne 0 ]; then
  echo "FAILURES DETECTED - check $REPORT_FILE for details"
else
  echo "ALL TESTS PASSED"
fi

exit $EXIT_CODE
