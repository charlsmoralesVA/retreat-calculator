#!/bin/bash
# The whole loop without ATAS, in one Terminal window: the practice feed (synthetic sessions through the real engine and
# bridge) plus Laya reading every practice candle and deciding the set-up. About 20 Claude calls on your plan.
# Stop with Ctrl+C.
#
#   ./practice.sh              feed speed 1 bar per second (each practice-day candle still waits for Laya's read)
#   ./practice.sh 20           20 bars per second for the warm-up history
ROOT="$(cd "$(dirname "$0")" && pwd)"
SPEED="${1:-1}"
export PATH="/usr/local/share/dotnet:$HOME/.local/bin:$PATH"
cd "$ROOT" || exit 1
command -v dotnet >/dev/null 2>&1 || { echo ".NET not found: run bootstrap-mac.sh first"; exit 1; }
command -v claude >/dev/null 2>&1 || { echo "Claude Code not found: run bootstrap-mac.sh, then 'claude' once to sign in"; exit 1; }

echo "starting the practice feed (log: /tmp/fabio-practice-feed.log) ..."
dotnet run --project tools/BridgeDemo -c Release -- --review-timeout 120 --speed "$SPEED" >/tmp/fabio-practice-feed.log 2>&1 &
FEED=$!
trap 'kill $FEED 2>/dev/null; echo; echo "practice stopped"' EXIT INT TERM
for _ in $(seq 1 60); do
  curl -s -m 2 http://localhost:8765/health >/dev/null 2>&1 && break
  kill -0 $FEED 2>/dev/null || { echo "the practice feed stopped: see /tmp/fabio-practice-feed.log"; exit 1; }
  sleep 2
done
curl -s -m 2 http://localhost:8765/health >/dev/null 2>&1 || { echo "the practice feed did not start: see /tmp/fabio-practice-feed.log"; exit 1; }
echo "feed is up; Laya starts reading (Ctrl+C to stop)"
python3 laya/laya_runner.py --exit-when-bridge-down
