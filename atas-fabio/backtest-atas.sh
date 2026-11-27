#!/bin/bash
# Backtest the history exported by the ATAS indicator ("Export history for backtest"): the default settings, a sweep,
# Laya's review (one Claude call), then the report opens. Also started by the dashboard's Backtests page.
#
#   ./backtest-atas.sh                  newest export in ~/Documents/FabioBacktest
#   ./backtest-atas.sh FILE.bars.gz     a given export
#   ./backtest-atas.sh --no-open ...    do not open the report
set -u
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/share/dotnet:$HOME/.local/bin:$PATH"
OPEN=1; FILE=""
for a in "$@"; do case "$a" in --no-open) OPEN=0 ;; *) FILE="$a" ;; esac; done
if [ -z "$FILE" ]; then
  FILE=$(ls -t "$HOME"/Documents/FabioBacktest/*.bars.gz 2>/dev/null | head -1)
  [ -n "$FILE" ] || { echo "No ATAS export found in ~/Documents/FabioBacktest: in ATAS, turn on the indicator's 'Export history for backtest'."; exit 1; }
fi
[ -f "$FILE" ] || { echo "not found: $FILE"; exit 1; }
NAME=$(basename "$FILE" .bars.gz)
echo "== $NAME"
./backtest.sh info --bars "$FILE" || exit 1
echo; echo "== 1/4 the default settings"
./backtest.sh run --bars "$FILE" --out "reports/$NAME-defaults" || exit 1
echo; echo "== 2/4 each of Fabio's models alone, then all together"
./backtest.sh models --bars "$FILE" --out "reports/$NAME-models" || exit 1
echo; echo "== 3/4 sweep (ranked on the first 70% of the sessions, checked on the rest)"
./backtest.sh sweep --bars "$FILE" --min-trades 10 --out "reports/$NAME-sweep" || exit 1
echo; echo "== 4/4 Laya's review"
if command -v claude >/dev/null 2>&1; then
  python3 laya/review_backtest.py "reports/$NAME-sweep" ${LAYA_CLAUDE:+--claude "$LAYA_CLAUDE"} || echo "(Laya's review failed: the reports are still there)"
else
  echo "Claude Code not found: skipped Laya's review"
fi
echo; echo "Reports in $ROOT/reports: $NAME-defaults, $NAME-models, $NAME-sweep (also on the dashboard's Backtests page)"
[ $OPEN = 1 ] && command -v open >/dev/null 2>&1 && open "reports/$NAME-sweep/report.html"
exit 0
