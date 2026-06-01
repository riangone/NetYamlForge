#!/usr/bin/env python3
"""Native messaging host: bridges Chrome extension to antigravity CLI."""

import sys
import json
import struct
import subprocess
import shutil
import os

def read_message():
    raw_length = sys.stdin.buffer.read(4)
    if not raw_length:
        return None
    length = struct.unpack('=I', raw_length)[0]
    data = sys.stdin.buffer.read(length)
    return json.loads(data.decode('utf-8'))

def send_message(msg):
    encoded = json.dumps(msg).encode('utf-8')
    sys.stdout.buffer.write(struct.pack('=I', len(encoded)))
    sys.stdout.buffer.write(encoded)
    sys.stdout.buffer.flush()

def find_antigravity():
    # Check PATH first
    ag = shutil.which('antigravity')
    if ag:
        return ag
    # Common install locations
    candidates = [
        os.path.expanduser('~/.local/bin/antigravity'),
        '/usr/local/bin/antigravity',
        '/opt/homebrew/bin/antigravity',
    ]
    for c in candidates:
        if os.path.isfile(c) and os.access(c, os.X_OK):
            return c
    return None

def handle_chat(msg_id, prompt):
    ag = find_antigravity()
    if not ag:
        send_message({'id': msg_id, 'success': False,
                      'error': 'antigravity CLI not found. Install it and ensure it is in PATH.'})
        return

    try:
        result = subprocess.run(
            [ag, 'chat', '--no-stream', prompt],
            capture_output=True, text=True, timeout=120,
            env={**os.environ, 'PYTHONUNBUFFERED': '1'}
        )
        if result.returncode == 0:
            send_message({'id': msg_id, 'success': True, 'output': result.stdout.strip()})
        else:
            send_message({'id': msg_id, 'success': False,
                          'error': result.stderr.strip() or f'Exit code {result.returncode}'})
    except subprocess.TimeoutExpired:
        send_message({'id': msg_id, 'success': False, 'error': 'antigravity timed out (>120s)'})
    except Exception as e:
        send_message({'id': msg_id, 'success': False, 'error': str(e)})

def main():
    while True:
        msg = read_message()
        if msg is None:
            break

        msg_id = msg.get('id', 0)
        msg_type = msg.get('type', '')

        if msg_type == 'chat':
            handle_chat(msg_id, msg.get('prompt', ''))
        else:
            send_message({'id': msg_id, 'success': False, 'error': f'Unknown type: {msg_type}'})

if __name__ == '__main__':
    main()
