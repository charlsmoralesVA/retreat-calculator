#!/usr/bin/env bash
# macOS installer for ATAS X: builds the Fabio Orderflow indicator + strategy against the ATAS X app bundle and copies
# them to ~/Library/Application Support/ATAS/{Indicators,Strategies} (docs.atas.net -> ATAS X and macOS).
# ATAS X reloads changed DLLs by itself about a second after they are copied.
#
#   ./install-mac.sh                                         ATAS X in /Applications
#   ./install-mac.sh "/Applications/ATAS X.app/Contents/MonoBundle"
#
# Needs the .NET 10 SDK: https://dotnet.microsoft.com/download  (or: brew install --cask dotnet-sdk)
set -euo pipefail

ATAS_BASE="${1:-/Applications/ATAS X.app/Contents/MonoBundle}"
ROOT="$(cd "$(dirname "$0")" && pwd)"
DEST="${FABIO_DEST:-$HOME/Library/Application Support/ATAS}"
red() { printf '\033[31m%s\033[0m\n' "$*"; }

if [ ! -f "$ATAS_BASE/ATAS.Indicators.dll" ]; then
  red "ATAS.Indicators.dll not found in '$ATAS_BASE'."
  echo "Pass the MonoBundle folder of ATAS X, e.g.: ./install-mac.sh \"/Applications/ATAS X.app/Contents/MonoBundle\""
  exit 1
fi
if ! command -v dotnet >/dev/null 2>&1; then
  red ".NET SDK not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download (or: brew install --cask dotnet-sdk)"
  exit 1
fi
MAX_SDK=$(dotnet --list-sdks 2>/dev/null | sed -E 's/^([0-9]+)\..*/\1/' | sort -n | tail -1)
if [ -z "$MAX_SDK" ] || [ "$MAX_SDK" -lt 10 ]; then
  red "ATAS X needs the .NET 10 SDK (found: ${MAX_SDK:-none}). Install it: https://dotnet.microsoft.com/download"
  exit 1
fi

echo "ATAS X : $ATAS_BASE"
for part in Indicator Strategy; do
  echo; echo "Building $part ..."
  if ! dotnet build "$ROOT/src/Atas/$part/FabioOrderflow.$part.csproj" -c Release \
        -p:ATAS_BASE="$ATAS_BASE" -p:AtasTarget=net10.0 -o "$ROOT/out/$part" --nologo -v q; then
    red "Build of the $part failed. Send the error lines above so the code can be adjusted to your ATAS X version."
    exit 1
  fi
done

mkdir -p "$DEST/Indicators" "$DEST/Strategies"
cp -f "$ROOT/out/Indicator/FabioOrderflow.Indicator.dll" "$DEST/Indicators/"
cp -f "$ROOT/out/Strategy/FabioOrderflow.Strategy.dll" "$DEST/Strategies/"

printf '\n\033[32mInstalled:\033[0m\n  indicator -> %s\n  strategy  -> %s\n' "$DEST/Indicators" "$DEST/Strategies"
echo
echo "Next: in ATAS X open a 5-minute ES or NQ chart with at least 20 days loaded (60+ for the 3-month value area),"
echo "add the indicator 'Fabio Orderflow Monitor'. The strategy 'Fabio Orderflow Strategy' starts with trading OFF;"
echo "its Laya bridge listens on http://localhost:8765 once the strategy is on a chart (see laya/README.md)."
