#!/bin/bash
# Laya on every candle with her dashboard, from Terminal. Keeps the Mac awake while running; Ctrl+C stops everything.
# Start the "Fabio Orderflow Strategy" on an ATAS chart (its bridge listens on localhost:8765); Laya waits for it.
#
#   ./start-laya.sh                      dashboard at http://localhost:8770 + Laya reading every candle
#   ./start-laya.sh --no-runner          dashboard only (start Laya from its button)
#   ./start-laya.sh --model sonnet       any desk_server.py option (--skill-command /favio-orderflow-scalp, --port ...)
ROOT="$(cd "$(dirname "$0")" && pwd)"
export PATH="/opt/homebrew/bin:/usr/local/share/dotnet:$HOME/.local/bin:$PATH"
command -v claude >/dev/null 2>&1 || { echo "Claude Code not found: run bootstrap-mac.sh, then 'claude' once to sign in"; exit 1; }
ARGS=(--open --start-runner)
for a in "$@"; do [ "$a" = "--no-runner" ] && ARGS=(--open); done
PASS=(); for a in "$@"; do [ "$a" != "--no-runner" ] && PASS+=("$a"); done
echo "Laya and her dashboard are starting; the Mac stays awake until you press Ctrl+C."
if command -v caffeinate >/dev/null 2>&1; then
  exec caffeinate -dims python3 "$ROOT/laya/desk_server.py" "${ARGS[@]}" ${PASS[@]+"${PASS[@]}"}
fi
exec python3 "$ROOT/laya/desk_server.py" "${ARGS[@]}" ${PASS[@]+"${PASS[@]}"}
