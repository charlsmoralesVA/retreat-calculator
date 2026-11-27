#!/bin/bash
# Laya on every candle, from Terminal: keeps the Mac awake while she runs (caffeinate), stops with Ctrl+C.
# Start the "Fabio Orderflow Strategy" on an ATAS chart first (its bridge listens on localhost:8766).
#
#   ./start-laya.sh                      defaults (plan at 09:00 New York, reads from 09:30)
#   ./start-laya.sh --model sonnet       any laya_runner.py option
ROOT="$(cd "$(dirname "$0")" && pwd)"
export PATH="$HOME/.local/bin:$PATH"
command -v claude >/dev/null 2>&1 || { echo "Claude Code not found: run bootstrap-mac.sh, then 'claude' once to sign in"; exit 1; }
echo "Laya is starting; the Mac stays awake until you press Ctrl+C."
if command -v caffeinate >/dev/null 2>&1; then exec caffeinate -dims python3 "$ROOT/laya/laya_runner.py" "$@"; fi
exec python3 "$ROOT/laya/laya_runner.py" "$@"
