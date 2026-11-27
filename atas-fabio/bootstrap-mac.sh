#!/bin/bash
# One paste in Terminal sets up the whole Fabio / Laya / ATAS project on a Mac:
#
#   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/charlsmoralesVA/retreat-calculator/ccr-fc3e3057-0ka152/atas-fabio/bootstrap-mac.sh)"
#
# What it does (each step is skipped when already done; safe to run again):
#   Homebrew (it also installs Apple's command-line tools), .NET 10 SDK, Python, zstd, Claude Code,
#   the project in ~/retreat-calculator, a Python environment for Laya's tools (~/laya-venv),
#   Laya's connector for Claude Code / Claude Desktop, then setup-mac.sh (tests, backtest tool, practice feed,
#   ATAS plug-in if ATAS X is installed).
# You type: your Mac password once (for the installers). At the end: sign in to Claude once.
# Log: ~/fabio-setup.log
set -u
BRANCH="ccr-fc3e3057-0ka152"
REPO="https://github.com/charlsmoralesVA/retreat-calculator.git"
DIR="$HOME/retreat-calculator"
LOG="$HOME/fabio-setup.log"

exec > >(tee -a "$LOG") 2>&1
bold()  { printf '\n\033[1m== %s\033[0m\n' "$*"; }
green() { printf '\033[32m%s\033[0m\n' "$*"; }
red()   { printf '\033[31m%s\033[0m\n' "$*"; }
die()   { red "STOPPED: $*"; red "Send the last lines of $LOG to Claude."; exit 1; }

echo "Fabio / Laya setup  $(date)"
[ "$(uname)" = "Darwin" ] || die "this script is for macOS"
MACOS=$(sw_vers -productVersion)
MAJOR=${MACOS%%.*}
echo "macOS $MACOS on $(uname -m)"
[ "$MAJOR" -ge 13 ] || red "macOS 13 or later is needed for Claude Code (you have $MACOS); the rest still installs."

bold "Your Mac password (once, for the installers)"
echo "Type your Mac login password and press Return (nothing shows while you type)."
sudo -v || die "this account needs administrator rights (System Settings -> Users & Groups)"
# keep the password valid while the installers run
( while true; do sudo -n true 2>/dev/null; sleep 50; kill -0 "$$" 2>/dev/null || exit; done ) &
KEEPALIVE=$!
disown "$KEEPALIVE" 2>/dev/null     # no "Terminated" line when it is stopped at the end
trap 'kill $KEEPALIVE 2>/dev/null' EXIT

bold "Homebrew (and Apple's command-line tools)"
BREW=""
for b in /opt/homebrew/bin/brew /usr/local/bin/brew; do [ -x "$b" ] && BREW="$b" && break; done
if [ -z "$BREW" ]; then
  NONINTERACTIVE=1 /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)" || die "Homebrew install failed"
  for b in /opt/homebrew/bin/brew /usr/local/bin/brew; do [ -x "$b" ] && BREW="$b" && break; done
fi
[ -n "$BREW" ] || die "Homebrew not found after install"
eval "$("$BREW" shellenv)"
grep -qs 'brew shellenv' "$HOME/.zprofile" || echo "eval \"\$($BREW shellenv)\"" >> "$HOME/.zprofile"
green "Homebrew $(brew --version | head -1)"
xcode-select -p >/dev/null 2>&1 && green "command-line tools ok" || die "Apple command-line tools missing: run  xcode-select --install  then this command again"

bold ".NET 10 SDK, Python, zstd"
export PATH="/usr/local/share/dotnet:$PATH"
SDK=$(dotnet --list-sdks 2>/dev/null | sed -E 's/^([0-9]+)\..*/\1/' | sort -n | tail -1)
if [ -z "$SDK" ] || [ "$SDK" -lt 10 ]; then brew install --cask dotnet-sdk || die ".NET SDK install failed"; fi
SDK=$(dotnet --list-sdks 2>/dev/null | sed -E 's/^([0-9]+)\..*/\1/' | sort -n | tail -1)
[ -n "$SDK" ] && [ "$SDK" -ge 10 ] || die ".NET 10 SDK not found after install (found: ${SDK:-none})"
green ".NET SDK $SDK"
brew list python@3.12 >/dev/null 2>&1 || brew install python@3.12 || die "Python install failed"
brew list zstd >/dev/null 2>&1 || brew install zstd || die "zstd install failed"
PY="$(brew --prefix)/bin/python3.12"
[ -x "$PY" ] || die "python3.12 not found at $PY"
green "$("$PY" --version), zstd ok"

bold "Claude Code (Laya runs through it)"
export PATH="$HOME/.local/bin:$PATH"
if ! command -v claude >/dev/null 2>&1; then
  curl -fsSL https://claude.ai/install.sh | bash || die "Claude Code install failed"
fi
grep -qs '.local/bin' "$HOME/.zshrc" || echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.zshrc"
command -v claude >/dev/null 2>&1 || die "claude not found after install"
green "Claude Code $(claude --version 2>/dev/null | head -1)"

bold "The project in $DIR"
if [ -d "$DIR/.git" ]; then
  git -C "$DIR" fetch origin "$BRANCH" || die "git fetch failed"
  if git -C "$DIR" rev-parse --verify -q "$BRANCH" >/dev/null; then git -C "$DIR" checkout "$BRANCH" || die "checkout failed (local changes?)"
  else git -C "$DIR" checkout -b "$BRANCH" --track "origin/$BRANCH" || die "checkout failed"; fi
  git -C "$DIR" pull --ff-only origin "$BRANCH" || die "update failed (local changes in $DIR?)"
elif [ -d "$DIR" ] && [ -n "$(ls -A "$DIR" 2>/dev/null)" ]; then
  die "$DIR exists and is not empty: rename it, then run this command again"
else
  git clone -b "$BRANCH" "$REPO" "$DIR" || die "git clone failed"
fi
A="$DIR/atas-fabio"
chmod +x "$A"/*.sh 2>/dev/null
green "project at $A ($(git -C "$DIR" log --oneline -1))"

bold "Python environment for Laya's tools (~/laya-venv)"
[ -x "$HOME/laya-venv/bin/python" ] || "$PY" -m venv "$HOME/laya-venv" || die "venv failed"
"$HOME/laya-venv/bin/pip" install -q --upgrade pip >/dev/null 2>&1
"$HOME/laya-venv/bin/pip" install -q -r "$A/laya/requirements.txt" databento || die "pip install failed"
green "MCP connector and Databento client installed"

bold "Laya's connector (fabio-atas) for Claude Code and Claude Desktop"
if claude mcp get fabio-atas >/dev/null 2>&1; then green "already in Claude Code"
elif OUT=$(claude mcp add --scope user fabio-atas -e FABIO_BRIDGE_URL=http://localhost:8765 -- \
           "$HOME/laya-venv/bin/python" "$A/laya/fabio_atas_mcp.py" 2>&1); then green "added to Claude Code (all projects)"
else echo "Claude Code connector not added (optional): $OUT"; fi
CFG_DIR="$HOME/Library/Application Support/Claude"
if [ -d "$CFG_DIR" ]; then
  "$PY" - "$CFG_DIR/claude_desktop_config.json" "$HOME/laya-venv/bin/python" "$A/laya/fabio_atas_mcp.py" <<'PYEOF' && green "added to Claude Desktop (restart it to load)"
import json, os, shutil, sys
path, py, script = sys.argv[1:4]
cfg = {}
if os.path.exists(path):
    shutil.copy(path, path + ".backup-fabio")
    with open(path) as f:
        cfg = json.load(f) if os.path.getsize(path) else {}
cfg.setdefault("mcpServers", {})["fabio-atas"] = {"command": py, "args": [script], "env": {"FABIO_BRIDGE_URL": "http://localhost:8765"}}
with open(path, "w") as f:
    json.dump(cfg, f, indent=2)
PYEOF
else echo "Claude Desktop not installed: skipped (optional)"; fi

bold "Build, tests, backtest tool, practice feed, ATAS plug-in"
"$A/setup-mac.sh"

kill $KEEPALIVE 2>/dev/null
bold "Done"
cat <<EOF
Everything is installed in $A  (log: $LOG)

Two things only you can do:
  1. Sign in to Claude once:   claude      (a browser page opens; sign in with your Claude account, then type /exit)
  2. Open a NEW Terminal window (so the new commands are found).

Then, all from Terminal:
  cd ~/retreat-calculator/atas-fabio
  python3 laya/laya_runner.py --check-skill          does Laya see the Fabio skill?  (1 Claude call)
  ./practice.sh                                      the whole loop on practice data   (about 20 Claude calls)
  ./backtest.sh demo && ./backtest.sh run --bars data/DEMO.bars.gz && open reports/es-run/report.html
  ~/laya-venv/bin/python tools/databento_fetch.py --symbol ES --start 2022-01-01 --end $(date +%Y-%m-%d)   real data price
  ./start-laya.sh                                    Laya + her dashboard at http://localhost:8770
  ./backtest-atas.sh                                 backtest ATAS's exported history (after ATAS)

To continue this conversation with Claude on the Mac:
  cd ~/retreat-calculator && claude --teleport session_01LWTHsmyxnfN2GU9iTJmnvB
EOF
