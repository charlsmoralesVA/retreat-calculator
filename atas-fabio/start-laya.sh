#!/bin/bash
# Laya on every candle with her dashboard, from Terminal. Keeps the Mac awake while running; Ctrl+C stops everything.
# Start the "Fabio Orderflow Strategy" on an ATAS chart (its bridge listens on localhost:8766); Laya waits for it.
#
#   ./start-laya.sh                      dashboard at http://localhost:8770 + Laya reading every candle
#   ./start-laya.sh --practice           the same on the practice feed instead of ATAS (about 20 Claude calls)
#   ./start-laya.sh --no-runner          dashboard only (start Laya from its button)
#   ./start-laya.sh --no-open            do not open the browser (used by laya-service.sh)
#   LAYA_KEEP_AWAKE=0 ./start-laya.sh    let the Mac sleep (Laya then misses candles while it sleeps)
#   ./start-laya.sh --model sonnet       any desk_server.py option (--skill-command /favio-orderflow-scalp, --port ...)
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/share/dotnet:$HOME/.local/bin:$PATH"
command -v claude >/dev/null 2>&1 || { echo "Claude Code not found: run bootstrap-mac.sh, then 'claude' once to sign in"; exit 1; }
OPEN=(--open); RUNNER=(--start-runner); PRACTICE=0; PASS=()
for a in "$@"; do
  case "$a" in
    --no-runner) RUNNER=() ;;
    --no-open) OPEN=() ;;
    --practice) PRACTICE=1 ;;
    *) PASS+=("$a") ;;
  esac
done
ARGS=(${OPEN[@]+"${OPEN[@]}"} ${RUNNER[@]+"${RUNNER[@]}"})
if [ $PRACTICE = 1 ]; then
  curl -s -m 2 http://localhost:8766/health >/dev/null 2>&1 && { echo "Something already answers on port 8766 (ATAS's strategy?): stop it before practising."; exit 1; }
  echo "starting the practice feed (log: /tmp/fabio-practice-feed.log) ..."
  dotnet run --project tools/BridgeDemo -c Release -- --review-timeout 120 --speed 20 --hold >/tmp/fabio-practice-feed.log 2>&1 &
  FEED=$!
  trap 'kill $FEED 2>/dev/null' EXIT INT TERM
  for _ in $(seq 1 90); do curl -s -m 2 http://localhost:8766/health >/dev/null 2>&1 && break; sleep 2; done
fi
echo "Laya and her dashboard are starting; the Mac stays awake until you press Ctrl+C."
CAF=(); [ "${LAYA_KEEP_AWAKE:-1}" != "0" ] && command -v caffeinate >/dev/null 2>&1 && CAF=(caffeinate -dims)
${CAF[@]+"${CAF[@]}"} python3 "$ROOT/laya/desk_server.py" "${ARGS[@]}" ${PASS[@]+"${PASS[@]}"}
