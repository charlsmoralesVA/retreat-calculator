#!/bin/bash
# Laya + her dashboard as a background service (pm2, like your other bots): starts at login, restarts if it crashes,
# and keeps reading every candle whenever the ATAS strategy is running. Costs nothing while ATAS is closed: Laya only
# calls Claude when a candle closes on the bridge.
#
#   ./laya-service.sh start      start it now and at every login (dashboard: http://localhost:8770)
#   ./laya-service.sh status     is it running, is the bridge up
#   ./laya-service.sh logs       the last lines of Laya's log (Ctrl+C to leave)
#   ./laya-service.sh restart    after an update (git pull)
#   ./laya-service.sh stop       stop it (and do not start at login)
#
# The service keeps the Mac awake while it runs (as start-laya.sh does). Closing the lid still sleeps the Mac.
set -u
ROOT="$(cd "$(dirname "$0")" && pwd)"
NAME="fabio-laya"
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/local/share/dotnet:$HOME/.local/bin:$PATH"
if ! command -v pm2 >/dev/null 2>&1; then
  echo "pm2 is not installed. Install it with:  npm install -g pm2   (or just run ./start-laya.sh in a Terminal window)"
  exit 1
fi

case "${1:-status}" in
  start)
    if pm2 describe "$NAME" >/dev/null 2>&1; then pm2 restart "$NAME" --update-env
    else pm2 start "$ROOT/start-laya.sh" --name "$NAME" --interpreter bash --time -- --no-open; fi
    pm2 save >/dev/null
    echo
    echo "Laya runs in the background. Dashboard: http://localhost:8770"
    echo "To start at login as well (once): run the command pm2 prints with:  pm2 startup"
    ;;
  stop)
    pm2 delete "$NAME" >/dev/null 2>&1 && pm2 save >/dev/null
    echo "Laya's service is stopped and removed from the login list."
    ;;
  restart)
    pm2 restart "$NAME" --update-env
    ;;
  logs)
    pm2 logs "$NAME" --lines 60
    ;;
  status)
    if pm2 describe "$NAME" >/dev/null 2>&1; then
      pm2 list | grep -E "name|$NAME" || true
    else
      echo "Laya's service is not installed: ./laya-service.sh start"
    fi
    if curl -s -m 2 http://localhost:8770/local/info >/dev/null 2>&1; then echo "dashboard: up  (http://localhost:8770)"; else echo "dashboard: down"; fi
    if curl -s -m 2 http://localhost:8766/health >/dev/null 2>&1; then echo "ATAS bridge: up (the strategy is running)"; else echo "ATAS bridge: down (start the Fabio Orderflow Strategy on a chart)"; fi
    ;;
  *)
    echo "usage: ./laya-service.sh start | status | logs | restart | stop"; exit 1 ;;
esac
