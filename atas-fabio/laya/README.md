# Laya + ATAS on your MacBook

Laya is Claude with the Fabio skill. Here she supervises the ATAS strategy: the strategy finds set-ups with code;
Laya reads each one and approves or rejects it; the strategy enforces the hard limits whatever she says.

```
ATAS X (Mac)                                         Claude Desktop / Claude Code (Mac)
  Fabio Orderflow Strategy  --- localhost:8765 --->  fabio-atas MCP server  <--->  Laya (Claude + Fabio skill)
  - finds set-ups (engine)        bridge             atas_status, wait_for_setup,
  - waits for approval                               decide_setup, list_setups, journal
  - places entry/stop/target
  - loss limit, flat at session end
```
This works on the ATAS **PRO** plan; ULTRA's "AI Connect" is not needed for this path.

## 1. Install (once)
1. ATAS X for macOS, logged in with a plan that has real-time futures data (PRO or ULTRA) and your data/broker connection.
2. .NET 10 SDK: https://dotnet.microsoft.com/download (or `brew install --cask dotnet-sdk`).
3. In Terminal, inside the `atas-fabio` folder: `./install-mac.sh`
   It builds against ATAS X and copies the indicator and the strategy to `~/Library/Application Support/ATAS/`.
4. Python for the MCP server:
   ```
   python3 -m venv ~/laya-venv
   ~/laya-venv/bin/pip install -r laya/requirements.txt
   ```

## 2. In ATAS X
- 5-minute ES or NQ chart, at least 20 days loaded (60+ for the 3-month value area).
- Add the indicator **Fabio Orderflow Monitor** (levels, set-ups, trades, panel).
- Add the strategy **Fabio Orderflow Strategy** and start it. Defaults: bridge ON (port 8765, localhost only),
  **Require Laya approval ON**, **Trading OFF**, approval timeout 45 s.

## 3. Connect Claude (pick one)
**Claude Desktop** - edit `~/Library/Application Support/Claude/claude_desktop_config.json`, then restart Claude:
```json
{
  "mcpServers": {
    "fabio-atas": {
      "command": "/Users/YOUR_NAME/laya-venv/bin/python",
      "args": ["/FULL/PATH/TO/atas-fabio/laya/fabio_atas_mcp.py"],
      "env": { "FABIO_BRIDGE_URL": "http://localhost:8765" }
    }
  }
}
```
**Claude Code**:
```
claude mcp add fabio-atas -e FABIO_BRIDGE_URL=http://localhost:8765 -- ~/laya-venv/bin/python /FULL/PATH/TO/atas-fabio/laya/fabio_atas_mcp.py
```
If you set a "Bridge token" in the strategy, add `"FABIO_BRIDGE_TOKEN": "..."` (or `-e FABIO_BRIDGE_TOKEN=...`).

Check the link: `~/laya-venv/bin/python laya/check_connection.py` -> `connected. instrument: ... bias: ...`

## 4. Run Laya
In a Claude conversation with the Fabio skill available, use the **fabio_review** prompt from the fabio-atas server
(or say: "Run the Fabio review loop: wait for set-ups and decide each one"). Laya then loops
`wait_for_setup` -> read the checklist + `atas_status` -> `decide_setup` with a reason. Her instructions are in
`LAYA_FABIO_INSTRUCTIONS.md`: approve only when bias, location, approach and the battle of orders all agree; reject when unsure.

## 5. Go live in stages
1. **Watch** (Trading OFF): Laya decides, nothing is traded; the journal records what would have happened.
2. **Simulation account** (Trading ON, sim portfolio): real orders on the ATAS simulator.
3. **Live**, small size, once the journal shows the set-ups and Laya's filtering are worth it.

## Practice without ATAS
`dotnet run --project tools/BridgeDemo` replays bars through the same engine and serves the same bridge on port 8765
(built-in synthetic example, or `--ticks your_trades.csv --tick-size 0.25`). It pauses at each set-up until Laya
decides and prints how the trade would have ended. `check_connection.py --decide` approves one set-up as a test.

## What it costs and what it cannot do
- **Claude usage:** every Laya tool call and decision uses your Claude plan. While waiting, each `wait_for_setup`
  call lasts up to 120 s, so a full cash session is roughly 200 waiting calls plus a few per set-up. Run Laya only
  during the hours you want covered to keep usage down.
- **Speed:** a decision takes seconds. The strategy refuses an approved entry if price has already moved more than
  "Max entry slippage" ticks from the set-up, so a slow decision becomes a skipped trade, never a chased one.
- **Limits are code, not Laya:** stop on every trade, max losing trades and set-ups per session, flat at the end of the
  cash session. Laya can veto a trade; she cannot move a stop or lift a limit.
- The bridge listens on `localhost` only. Not financial advice.
