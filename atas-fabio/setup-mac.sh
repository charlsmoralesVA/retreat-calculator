#!/bin/bash
# One-command setup of everything that does not need ATAS (setup guide Part 1), then the ATAS plug-in if ATAS X is
# already installed. Safe to run again: it skips what is already done and ends with a checklist.
#
#   ~/retreat-calculator/atas-fabio/setup-mac.sh
#
# It never asks for your password itself. Installers that need it (Xcode tools, Homebrew, .NET) are printed for you
# to run, then run this script again.
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT" || exit 1
OK=(); TODO=()
green() { printf '\033[32m%s\033[0m\n' "$*"; }
red()   { printf '\033[31m%s\033[0m\n' "$*"; }
step()  { printf '\n\033[1m== %s\033[0m\n' "$*"; }
ok()    { OK+=("$1"); green "ok: $1"; }
todo()  { TODO+=("$1"); red "to do: $1"; }

step "Mac and command-line tools"
if [ "$(uname)" != "Darwin" ]; then echo "(not macOS: Mac-only checks skipped)"; fi
if command -v git >/dev/null 2>&1 && command -v python3 >/dev/null 2>&1; then ok "git and python3"
else todo "Apple command-line tools: run  xcode-select --install  then run this script again"; fi

step "Homebrew"
BREW=""
for b in /opt/homebrew/bin/brew /usr/local/bin/brew "$(command -v brew 2>/dev/null)"; do [ -x "$b" ] && BREW="$b" && break; done
if [ -n "$BREW" ]; then ok "Homebrew ($BREW)"; eval "$("$BREW" shellenv)"
else todo 'Homebrew: run  /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"  then run this script again'; fi

step ".NET 10 SDK"
export PATH="$PATH:/usr/local/share/dotnet:$HOME/.dotnet"
MAX_SDK=$(dotnet --list-sdks 2>/dev/null | sed -E 's/^([0-9]+)\..*/\1/' | sort -n | tail -1)
if [ -n "$MAX_SDK" ] && [ "$MAX_SDK" -ge 10 ]; then ok ".NET SDK $MAX_SDK"
else
  if [ -n "$BREW" ]; then echo "installing the .NET SDK with Homebrew (it may ask for your Mac password) ..."; "$BREW" install --cask dotnet-sdk; fi
  MAX_SDK=$(dotnet --list-sdks 2>/dev/null | sed -E 's/^([0-9]+)\..*/\1/' | sort -n | tail -1)
  if [ -n "$MAX_SDK" ] && [ "$MAX_SDK" -ge 10 ]; then ok ".NET SDK $MAX_SDK"
  else todo ".NET 10 SDK: brew install --cask dotnet-sdk  (or the installer from https://dotnet.microsoft.com/download), then open a new Terminal"; fi
fi

step "zstd (to read Databento files)"
if command -v zstd >/dev/null 2>&1; then ok "zstd"
elif [ -n "$BREW" ] && "$BREW" install zstd >/dev/null 2>&1; then ok "zstd (installed)"
else todo "zstd: brew install zstd"; fi

HAVE_DOTNET=0; [ -n "$MAX_SDK" ] && [ "$MAX_SDK" -ge 10 ] && HAVE_DOTNET=1

step "Engine checks (the automated tests, about a minute)"
if [ $HAVE_DOTNET = 1 ]; then
  RES=$(dotnet run --project tests/FabioOrderflow.Tests 2>&1 | tail -1)
  case "$RES" in *" 0 failed"*) ok "tests: $RES" ;; *) todo "tests did not all pass: $RES (send this line to Claude)" ;; esac
else echo "skipped: needs .NET"; fi

step "Backtest tool"
if [ $HAVE_DOTNET = 1 ]; then
  if ./backtest.sh demo --days 200 >/tmp/fabio-setup-demo.log 2>&1 && ./backtest.sh run --bars data/DEMO.bars.gz --out reports/demo-run >>/tmp/fabio-setup-demo.log 2>&1; then
    ok "fabio-backtest built; practice report: $ROOT/reports/demo-run/report.html (synthetic data)"
  else todo "backtest tool: see /tmp/fabio-setup-demo.log"; fi
else echo "skipped: needs .NET"; fi

step "Practice feed for Laya"
if [ $HAVE_DOTNET = 1 ]; then
  if dotnet build tools/BridgeDemo -c Release --nologo -v q >/tmp/fabio-setup-bridge.log 2>&1; then ok "BridgeDemo built (setup guide 1.4)"
  else todo "BridgeDemo build: see /tmp/fabio-setup-bridge.log"; fi
else echo "skipped: needs .NET"; fi

step "Claude Code (Laya)"
if command -v claude >/dev/null 2>&1; then ok "claude found ($(claude --version 2>/dev/null | head -1))"
else todo "Claude Code: install from https://code.claude.com/docs/en/setup, then run  claude  once to log in"; fi
if [ -n "${ANTHROPIC_API_KEY:-}" ]; then todo "ANTHROPIC_API_KEY is set: Laya would bill that key instead of your plan (remove it from ~/.zshrc)"
else ok "no ANTHROPIC_API_KEY (Laya uses your Claude plan)"; fi

step "ATAS X plug-in"
if [ -f "/Applications/ATAS X.app/Contents/MonoBundle/ATAS.Indicators.dll" ]; then
  if [ $HAVE_DOTNET = 1 ] && ./install-mac.sh; then ok "indicator and strategy installed into ATAS X"
  else todo "ATAS plug-in build failed: send the red lines to Claude"; fi
else echo "ATAS X is not installed yet: after installing it (trial day 1), run  ./install-mac.sh"; fi

step "Summary"
for x in "${OK[@]}"; do green "  [x] $x"; done
for x in "${TODO[@]}"; do red "  [ ] $x"; done
echo
if [ ${#TODO[@]} -eq 0 ]; then
  echo "All set. Next: SETUP_GUIDE.md 1.3 (python3 laya/laya_runner.py --check-skill, one Claude call),"
  echo "1.4 (practice the whole loop) and BACKTEST_GUIDE.md (real data)."
else
  echo "Do the items above, then run this script again."
fi
