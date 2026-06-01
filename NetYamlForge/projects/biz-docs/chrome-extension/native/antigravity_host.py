#!/usr/bin/env python3
"""
BizDocs AI Assistant – Native Messaging Host
Bridges Chrome extension ↔ antigravity CLI via Chrome's native messaging protocol.
Messages are length-prefixed JSON (4-byte unsigned LE int + UTF-8 JSON body).
"""

import sys
import json
import struct
import subprocess
import os
import logging
import shutil

# Log to stderr only (stdout is reserved for native messaging)
logging.basicConfig(
    stream=sys.stderr,
    level=logging.DEBUG if os.environ.get('BIZDOCS_DEBUG') else logging.WARNING,
    format='%(levelname)s [bizdocs-host] %(message)s',
)

# ─── Config ──────────────────────────────────────────────────────────────────

# Read optional config from ~/.config/bizdocs-ai/config.json
def load_config():
    config_path = os.path.expanduser('~/.config/bizdocs-ai/config.json')
    defaults = {
        'command': 'antigravity',
        # Usage: antigravity chat --no-stream <prompt>
        'args': ['chat', '--no-stream'],
        'extra_args': [],
        'timeout': 120,
    }
    if os.path.exists(config_path):
        try:
            with open(config_path) as f:
                return {**defaults, **json.load(f)}
        except Exception as e:
            logging.warning(f'Failed to load config: {e}')
    return defaults

CONFIG = load_config()

# ─── Native Messaging Protocol ───────────────────────────────────────────────

def read_message():
    raw = sys.stdin.buffer.read(4)
    if len(raw) < 4:
        return None
    length = struct.unpack('<I', raw)[0]
    if length > 10 * 1024 * 1024:  # 10 MB sanity cap
        raise ValueError(f'Message too large: {length} bytes')
    data = sys.stdin.buffer.read(length)
    return json.loads(data.decode('utf-8'))

def write_message(obj):
    payload = json.dumps(obj, ensure_ascii=False).encode('utf-8')
    sys.stdout.buffer.write(struct.pack('<I', len(payload)))
    sys.stdout.buffer.write(payload)
    sys.stdout.buffer.flush()

# ─── antigravity Integration ─────────────────────────────────────────────────

def find_command():
    """Return the resolved path to the configured AI CLI command."""
    cmd = CONFIG['command']
    resolved = shutil.which(cmd)
    if resolved:
        return resolved

    # Common install locations
    candidates = [
        os.path.expanduser(f'~/.local/bin/{cmd}'),
        f'/usr/local/bin/{cmd}',
        f'/opt/homebrew/bin/{cmd}',
    ]
    for path in candidates:
        if os.path.isfile(path) and os.access(path, os.X_OK):
            return path

    raise FileNotFoundError(
        f"'{cmd}' not found. Install it or set \"command\" in ~/.config/bizdocs-ai/config.json"
    )

def call_antigravity(prompt: str) -> str:
    """Run antigravity CLI with the given prompt and return its stdout."""
    cmd_path = find_command()
    args = CONFIG.get('args', ['chat', '--no-stream'])
    extra = CONFIG.get('extra_args', [])
    timeout = int(CONFIG.get('timeout', 120))

    full_cmd = [cmd_path] + list(args) + [prompt] + list(extra)
    logging.debug('Running: %s', full_cmd)

    result = subprocess.run(
        full_cmd,
        capture_output=True,
        text=True,
        timeout=timeout,
        env={**os.environ},
    )

    if result.returncode != 0:
        err = result.stderr.strip() or f'Exit code {result.returncode}'
        raise RuntimeError(f'antigravity error: {err}')

    return result.stdout.strip()

# ─── Message Dispatch ────────────────────────────────────────────────────────

def handle(message):
    msg_id = message.get('id')
    msg_type = message.get('type', '')
    prompt = message.get('prompt', '')

    if msg_type not in ('chat', 'fill_form', 'process_file'):
        return {'id': msg_id, 'success': False, 'error': f'Unknown type: {msg_type}'}

    if not prompt:
        return {'id': msg_id, 'success': False, 'error': 'Empty prompt'}

    output = call_antigravity(prompt)
    return {'id': msg_id, 'success': True, 'output': output}

# ─── Main Loop ───────────────────────────────────────────────────────────────

def main():
    logging.debug('Native host started')
    while True:
        try:
            msg = read_message()
            if msg is None:
                break
            logging.debug('Received: type=%s id=%s', msg.get('type'), msg.get('id'))
            try:
                response = handle(msg)
            except Exception as e:
                logging.exception('Handler error')
                response = {'id': msg.get('id'), 'success': False, 'error': str(e)}
            write_message(response)
        except EOFError:
            break
        except Exception as e:
            logging.exception('Fatal loop error')
            break
    logging.debug('Native host exiting')

if __name__ == '__main__':
    main()
