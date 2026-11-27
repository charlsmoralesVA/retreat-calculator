#!/usr/bin/env bash
# Backtest Fabio's engine on years of data, outside ATAS (macOS / Linux). Needs the .NET 10 SDK.
#
#   ./backtest.sh demo                                              practise on SYNTHETIC data
#   ./backtest.sh import --databento ~/Downloads/GLBX-XXXX --instrument ES
#   ./backtest.sh run   --bars data/ES.bars.gz --from 2022-01-01
#   ./backtest.sh sweep --bars data/ES.bars.gz --from 2022-01-01 --split 2024-07-01
#   ./backtest.sh run   --bars ~/Documents/FabioBacktest/ESZ5_5m_....bars.gz      (history exported from ATAS)
#   ./backtest.sh --help
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"
if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET SDK not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download (or: brew install --cask dotnet-sdk)"
  exit 1
fi
BIN="$ROOT/out/backtest/fabio-backtest"
# build once (and again whenever the source is newer than the tool)
if [ ! -x "$BIN" ] || [ -n "$(find "$ROOT/src/Core" "$ROOT/tools/Backtest" -name '*.cs' -newer "$BIN" 2>/dev/null | head -1)" ]; then
  echo "building fabio-backtest ..."
  dotnet build "$ROOT/tools/Backtest/Backtest.csproj" -c Release -o "$ROOT/out/backtest" --nologo -v q >/dev/null
fi
exec "$BIN" "$@"
