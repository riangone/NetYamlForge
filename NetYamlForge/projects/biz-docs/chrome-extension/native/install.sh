#!/usr/bin/env bash
# BizDocs AI Assistant – Native Messaging Host Installer
# Usage: ./install.sh <EXTENSION_ID>
#
# Your extension ID is shown at chrome://extensions (Developer mode must be ON).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_PY="$SCRIPT_DIR/antigravity_host.py"
MANIFEST_TEMPLATE="$SCRIPT_DIR/com.bizdocs.aiassistant.json"
MANIFEST_OUT="$SCRIPT_DIR/com.bizdocs.aiassistant.installed.json"

# ── Extension ID ─────────────────────────────────────────────────────────────
EXTENSION_ID="${1:-}"
if [[ -z "$EXTENSION_ID" ]]; then
  echo "Usage: $0 <EXTENSION_ID>"
  echo ""
  echo "Find your extension ID at: chrome://extensions  (enable Developer mode)"
  exit 1
fi

# ── Python check ─────────────────────────────────────────────────────────────
PYTHON=$(command -v python3 || command -v python || true)
if [[ -z "$PYTHON" ]]; then
  echo "ERROR: Python 3 is required but not found."
  exit 1
fi
echo "Using Python: $PYTHON"

# ── Make host executable ──────────────────────────────────────────────────────
chmod +x "$HOST_PY"

# Create a wrapper script so we can use the correct Python interpreter
WRAPPER="$SCRIPT_DIR/run_host.sh"
cat > "$WRAPPER" <<WRAP
#!/usr/bin/env bash
exec "$PYTHON" "$HOST_PY"
WRAP
chmod +x "$WRAPPER"

# ── Build manifest ────────────────────────────────────────────────────────────
sed \
  -e "s|__PLACEHOLDER__|$WRAPPER|g" \
  -e "s|__EXTENSION_ID__|$EXTENSION_ID|g" \
  "$MANIFEST_TEMPLATE" > "$MANIFEST_OUT"

echo "Generated manifest: $MANIFEST_OUT"

# ── Install manifest ──────────────────────────────────────────────────────────
if [[ "$OSTYPE" == darwin* ]]; then
  DEST="$HOME/Library/Application Support/Google/Chrome/NativeMessagingHosts"
else
  DEST="$HOME/.config/google-chrome/NativeMessagingHosts"
fi

mkdir -p "$DEST"
cp "$MANIFEST_OUT" "$DEST/com.bizdocs.aiassistant.json"

echo ""
echo "✅  Installed to: $DEST/com.bizdocs.aiassistant.json"
echo ""
echo "Next steps:"
echo "  1. Reload the Chrome extension at chrome://extensions"
echo "  2. Open any biz-docs page (e.g. http://localhost:5001/biz-docs/Dashboard)"
echo "  3. Click the 🤖 button to open the AI sidebar"
echo ""
echo "Optional: configure antigravity command in ~/.config/bizdocs-ai/config.json"
echo "  Example:"
echo '    {'
echo '      "command": "antigravity",'
echo '      "args": ["run", "--prompt"],'
echo '      "timeout": 90'
echo '    }'
