# Laya on every candle: ATAS + your MacBook

Laya is Claude with the Fabio skill. In this setup she **reads every closed candle herself**, decides every set-up the
engine finds, and you watch it all on a desk app on your Mac.

```
ATAS X (Mac)                         your Mac                                   Claude (your plan)
  Fabio Orderflow Strategy            laya_runner.py ---- claude -p ---------->  Laya + Fabio skill
  - engine: profiles, delta,   <----> waits for each closed candle (free)        reads the candle, decides set-ups
    footprint, zones, set-ups  bridge posts Laya's read + decisions back
  - orders + hard limits      :8765   Laya Tape Desk (app from the Claude desktop app)
                                        live tape, Laya's reads, set-ups, notifications, menu bar, Start/Stop Laya
```
Works on the ATAS **PRO** plan (real-time futures data + live futures orders). ULTRA is not needed.

## Why this design
- **Laya reads every candle.** When a 5-minute candle closes, the strategy publishes a candle card: the bar
  (delta and volume against average, body, close position), the footprint at its extremes, big prints, the nearest trade
  zone and the approach into it, bias / value / VWAP / IB, and the engine's checklist. The runner hands it to Laya with
  the previous candles and her own earlier reads, and she answers with a phase, an action, a confidence and a read.
- **Waiting costs nothing.** The runner waits on the bridge locally; Claude is called only when a candle closes
  (78 calls for a full 5-minute cash session). If she falls behind (Mac asleep, started late) she reads the latest
  three and marks the older ones skipped.
- **Code keeps the risk rules.** Stop on every trade, max losing trades and set-ups per session, the entry-slippage
  guard, flat at the end of the cash session. Laya can approve or veto a set-up; she cannot move a stop or lift a limit.
- **The app is the cockpit, not the brain.** Artifacts may call Claude only on a click, so the app shows Laya's work and
  has an "Ask Laya about this candle" button; the per-candle reading is done by the runner through Claude Code.

## Set up (once)
1. ATAS X for macOS with a PRO (or ULTRA) plan and your real-time data / broker connection.
2. .NET 10 SDK (`brew install --cask dotnet-sdk`), then in the `atas-fabio` folder: `./install-mac.sh`.
3. **Claude Code** installed and logged in with your Claude account (`claude`, then `/login`). Make sure
   `ANTHROPIC_API_KEY` is NOT set, so it uses your plan. Your synced Fabio skill loads automatically; if your Claude Code
   shows it as a slash command, put that command (e.g. `/fabio-orderflow-strategy`) in the desk's Settings.
4. Python 3 (built into macOS with the developer tools). The runner needs nothing else.
5. Open the **Laya Tape Desk** artifact in the Claude desktop app and choose **Install as app**. Allow
   *full access* (it runs `curl` to the bridge and starts the runner), *notifications* and *menu bar* when asked.
   In Settings, set the `atas-fabio` folder (default `~/retreat-calculator/atas-fabio`).

## Every session
1. ATAS X: 5-minute ES or NQ chart, at least 20 days loaded. Add **Fabio Orderflow Monitor** and start
   **Fabio Orderflow Strategy** (bridge ON, Require Laya approval ON, Trading OFF to begin).
2. Open the desk app: the bridge pill turns green. Press **Start Laya**.
3. Each closed candle appears with Laya's read. Alerts and set-ups arrive as Mac notifications and in ATAS.
   Click a candle and **Ask Laya** for a fuller explanation (uses Claude once per click).
4. Go live in stages: watch (Trading OFF) -> ATAS simulation account (Trading ON) -> live, small size.

Without the app: `python3 laya/laya_runner.py` in a Terminal does the same per-candle work, and the `fabio-atas` MCP
server (below) lets you talk to Laya about the market in Claude Desktop / Claude Code.

## Practice without ATAS
```
dotnet run --project tools/BridgeDemo -- --review-timeout 120 --speed 20
python3 laya/laya_runner.py            # or Start Laya in the desk app
```
The demo replays six synthetic sessions, then a practice day, and waits for Laya's read of every practice-day candle.
`SAMPLE_REAL_READS.md` shows real reads Laya produced on it.

## MCP server for chatting with Laya (optional)
`fabio_atas_mcp.py` gives Claude Desktop / Claude Code these tools: `atas_status`, `recent_candles`, `wait_for_candle`,
`review_candle`, `wait_for_setup`, `list_setups`, `decide_setup`, `journal` (prompt: `fabio_review`).
```
python3 -m venv ~/laya-venv && ~/laya-venv/bin/pip install -r laya/requirements.txt
claude mcp add fabio-atas -e FABIO_BRIDGE_URL=http://localhost:8765 -- ~/laya-venv/bin/python /FULL/PATH/atas-fabio/laya/fabio_atas_mcp.py
```
Claude Desktop: add the same command under `mcpServers` in `~/Library/Application Support/Claude/claude_desktop_config.json`.
Check: `~/laya-venv/bin/python laya/check_connection.py`.

## What it costs and what it cannot do
- **Claude usage:** one Claude Code call per closed candle, about 15 KB of context each; 78 a session on 5-minute bars.
  A read takes roughly 10-60 s, so 1-minute bars are possible but leave little slack. "Ask Laya" adds one call per click.
- **Timing:** set-up approvals wait up to 90 s; an approval that comes after price has run more than the slippage guard
  (8 ticks) is skipped, never chased.
- **Fidelity:** Laya follows the skill and the written instructions; the engine's rules cover what `SKILL.md` states.
  The skill's reference-file models (stacked profiles, momentum/reload models, IVB rules, gap model, pre-market
  mapping) are not in the engine, though Laya may still apply what her skill contains.
- **The app:** it reaches the bridge only when installed with full access, on the Mac running ATAS. In a browser it
  shows a clearly labelled example. Laya stops when the app window closes.
- Not financial advice.
