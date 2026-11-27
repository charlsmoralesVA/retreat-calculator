# Setup guide: ATAS + Fabio engine + Laya, for your 14-day trial

Do **Part 1 before you start the ATAS trial**: it needs no ATAS, so none of the 14 days are spent on installs.
Part 2 is trial day 1. Part 3 is the daily routine. Part 4 is how to judge the trial.

```
ATAS X (Mac)                              your Mac                                   Claude (your plan)
  Fabio Orderflow Monitor   (indicator)   Laya Tape Desk app   <-- shows everything
  Fabio Orderflow Strategy  (engine,      laya_runner.py  ---- claude -p --------->  Laya + Fabio skill
    bridge on localhost:8765)  <------->  one call per closed candle + a pre-market plan
```

**What Laya monitors in real time** (measured by the engine, read by Laya with the Fabio skill):
the candle itself (delta and volume against average, body, close position, footprint at the high / low, big prints);
previous-session POC / VAH / VAL; today's developing POC / VAH / VAL; the value ladder of the last sessions (higher /
lower / overlapping); the 3-month composition value; high and low volume nodes; session VWAP with ±1 / ±2 SD bands;
session cumulative delta; the strongest delta levels of the day and the swing sniper level; the initial balance; overnight and London
high / low; the gap from the prior close; the approach into each zone (struggle or effortless); the engine's checklist.
Before the open she builds the day's plan (bias, key levels, if-then scenarios, when to stay out), as the skill's
"bias -> mapping the day" describes, and every candle is read against that plan.
The full list of Fabio's tools, which ATAS plan provides each one and what Laya watches: **`FABIO_TOOLS.md`**.

---

## Part 1 - before the trial (about one hour, no ATAS needed)

**Fastest: one paste.** Open Terminal on the Mac and paste:
```
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/charlsmoralesVA/retreat-calculator/ccr-fc3e3057-0ka152/atas-fabio/bootstrap-mac.sh)"
```
It asks for your Mac password once, then installs and sets up everything in 1.1-1.2 and 1.6 (Homebrew, .NET, Python,
Claude Code, the project, Laya's connector, the backtest tool) and runs `setup-mac.sh`. When it ends: run `claude` once
to sign in, open a new Terminal window, and continue with 1.3. The steps below are the same thing by hand.

### 1.1 Tools on the Mac
Open Terminal and run, one at a time:
```
xcode-select --install                      # git and python3 (skip if already installed)
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"   # Homebrew, if you don't have it
brew install --cask dotnet-sdk               # .NET 10 SDK (or the installer from https://dotnet.microsoft.com/download)
dotnet --list-sdks                           # must show a 10.x line
```
Install the **Claude desktop app** (https://claude.ai/download) and **Claude Code** (https://code.claude.com/docs/en/setup).
Then log Claude Code in with the account that has the Fabio skill:
```
claude            # first run opens the login; use /login if asked, then /exit
echo $ANTHROPIC_API_KEY      # must print nothing: if it prints a key, Laya would bill that key instead of your plan
```

### 1.2 Get the code
```
git clone -b ccr-fc3e3057-0ka152 https://github.com/charlsmoralesVA/retreat-calculator.git ~/retreat-calculator
cd ~/retreat-calculator/atas-fabio
```

**Fast path:** run `./setup-mac.sh` now. It checks or installs everything in 1.1, runs the engine's tests, builds the
backtest tool and Laya's practice feed, installs the ATAS plug-in if ATAS X is already there, and ends with a checklist
of anything left to do. Run it again until the checklist is all ticked.

### 1.3 Check that Laya sees the Fabio skill
```
python3 laya/laya_runner.py --check-skill
```
- "Fabio skill found. Reference files present." - best case: Laya can use Fabio's detailed models.
- "Only SKILL.md is visible" - she works from the core method; the detailed models live in reference files that are
  not in your synced copy. Re-upload the full skill (with its `references/` folder) in claude.ai if you have it.
- "does NOT see the Fabio skill" - Claude Code is logged into another account; run `claude`, then `/login`.
If your Claude Code lists the skill as a slash command (for example `/fabio-orderflow-strategy`), note it: you can put it
in the desk's Settings ("Skill command") to force-load it on every call.

### 1.4 Practice the whole loop without ATAS
Terminal 1:
```
cd ~/retreat-calculator/atas-fabio
dotnet run --project tools/BridgeDemo -- --review-timeout 120 --speed 1
```
Terminal 2:
```
cd ~/retreat-calculator/atas-fabio
python3 laya/laya_runner.py
```
You should see, in order: `pre-market: Laya is building the plan`, then one `candle ... Laya is reading it` per
5-minute practice candle with her read, and at 10:35 a long set-up that she approves or rejects. The data is synthetic.
Stop both with Ctrl+C. Each read uses your Claude plan (about 20 calls for this practice).

### 1.5 The dashboard
```
cd ~/retreat-calculator/atas-fabio && ./start-laya.sh
```
It opens **Laya's dashboard at http://localhost:8770** in your browser and starts Laya. On it: the tape with each
candle and her read, the chart with the prior and developing value, VWAP, overnight levels, her plan for today, the
market map, the set-ups with Approve / Reject, **Ask Laya** about any candle, her console, and **Backtests**. Allow
notifications when the browser asks. Without ATAS (or the practice feed) it says "Bridge: offline" and Laya waits.
Ctrl+C in Terminal stops Laya and the dashboard. To try it now, without ATAS: `./start-laya.sh --practice` (the
practice feed stands in for ATAS; about 20 Claude calls).

Optional, the same desk as an app inside the Claude desktop app: open https://claude.ai/artifact/HcjbAHrF2AEQcpW2o7Jfad
there, choose **Install as app** and allow full access, notifications and the menu bar.

### 1.6 Backtest on years of data (before the trial)
Follow **`BACKTEST_GUIDE.md`**: practise with `./backtest.sh demo`, get ES (and NQ) trades from Databento, import them,
run the defaults and a sweep, and ask Laya to review it (`python3 laya/review_backtest.py reports/es-sweep`). Note the
**settings line** of the setting you choose: it goes into ATAS on day 1 (2.4 and 2.5).

---

## Part 2 - trial day 1 (about 45 minutes)

### 2.1 ATAS
1. Download **ATAS X for macOS** from atas.net and sign in.
2. Start the **14-day trial of ULTRA** (the trial can be used once per account). ULTRA includes everything PRO has, plus
   AI Connect, Options X-Ray, MBO and no limit on indicators per chart (section 2.8).
3. Connect your **real-time CME data** (your broker / prop firm / data provider, e.g. Rithmic or CQG). The subscription
   does not include quotes. Without a live feed you can still use **Market Replay** (included in the trial plan) to
   replay past sessions through the same tools.

### 2.2 Install the indicator and the strategy
Quit ATAS X, then:
```
cd ~/retreat-calculator/atas-fabio
./install-mac.sh
```
It builds against your ATAS X and copies the two files to `~/Library/Application Support/ATAS/Indicators` and
`.../Strategies`. If it stops with red error lines, copy them to Claude: the code was compiled against stand-ins of the
ATAS API, so a small name difference in your version is possible and quick to fix.

### 2.3 The chart
- Instrument: **ES** (or MES) or **NQ** (or MNQ), front month. Timeframe: **5 minutes**.
- Session: the **full electronic session (24h)**, not "day session only": the overnight and London levels need the
  night bars. The engine finds the 09:30-16:00 New York cash session by itself, whatever your Mac's time zone.
- Days to load: **60** (minimum 20; the 3-month value needs about 60 sessions).

### 2.4 Add the indicator: "Fabio Orderflow Monitor"
Indicators -> Fabio Orderflow Monitor. Check the panel (top left of the chart):
- "Last bar ... exchange time - cash session" during 09:30-16:00 New York time. If it says outside the cash session
  while the US market is open, check "Exchange time zone" = America/New_York in the indicator settings.
- Bias with a reason, prior POC / VAH / VAL, today's value, VWAP bands, overnight / London, value ladder, nodes.
- On the chart: prior value lines, today's developing POC / VAH / VAL (dots), VWAP and bands, IB, overnight high / low,
  and for past days the set-up arrows with entry / stop / target and the result. "History on this chart" totals them.

### 2.5 Add the strategy: "Fabio Orderflow Strategy"
Chart -> strategies -> Fabio Orderflow Strategy -> Add. Select the **simulation (demo) account** as portfolio.
Settings for the trial:
| Setting | Value |
|---|---|
| Trading enabled | **OFF** for the first days |
| Contracts | 1 |
| Max losing trades per session | 2 |
| Bridge for Laya enabled | ON (port 8765) |
| Require Laya approval before entering | ON |
| Approval timeout | 90 |
| Max set-ups per session | 3 |
| Engine settings from the backtest | the settings line you chose in 1.6 (empty = defaults) |
Start the strategy (its control button).
Put the same settings line in the indicator ("5. Backtest" -> "Engine settings from the backtest"), so the chart and
the strategy run the same rules. Then turn on **Export history for backtest** once and compare it with fabio-backtest
(`BACKTEST_GUIDE.md`, "Trial day 1").

### 2.6 Start Laya
`cd ~/retreat-calculator/atas-fabio && ./start-laya.sh`: the dashboard opens, the bridge pill turns green, the market
map fills. Before 09:00 New York time she waits; from 09:00 she builds the plan; from 09:30 every closed candle gets
her read. Alerts and set-ups arrive as notifications and in ATAS. Click a candle and **Ask Laya** for a longer
explanation.

### 2.7 Backtest ATAS's own history (right away)
In the indicator, turn on **Export history for backtest** once the chart has loaded. Then on the dashboard press
**Backtests -> Backtest the latest ATAS export**, or in Terminal: `./backtest-atas.sh`. It runs the defaults, a sweep
and Laya's review, and opens the report (`BACKTEST_GUIDE.md` explains how to read it).

---

### 2.8 The ULTRA extras (after 2.1-2.6 work)
Get the core working first: everything above runs the same on PRO and ULTRA.

**AI Connect: Laya inside ATAS.** ATAS's own MCP server, so Claude on this Mac can read the chart and draw on it.
- Per ATAS (aiconnect.atas.net), AI Connect runs in the **ATAS beta**, in ATAS X and Classic. If the **Connect AI agent**
  menu item is missing, switch ATAS X to the beta version. If the beta misbehaves with the chart or orders, go back to
  the stable version: everything except AI Connect works there.
- Open **Connect AI agent**, switch the MCP server on, and follow the dialog for **Claude** (Claude Desktop or Claude
  Code). The agent must run on the same Mac as ATAS. It uses your Claude plan: ATAS does not charge for the agent.
- In the same Claude, add this project's **fabio-atas** connector (`laya/README.md`, "MCP server"), so Laya sees the
  engine's levels and her own plan and reads too.
- Start the conversation with `laya/LAYA_AI_CONNECT.md`. Use it for three jobs: **"Put the plan on the chart"**
  (09:15), **"What do you see?"** (any time), **"Review the day"** (after 16:00).
- **Safety:** orders go only through the Fabio strategy, which has the stop and the limits. If AI Connect shows a tool
  that places or closes orders, block it. In Claude Desktop, switch that tool off in the connector's settings. In
  Claude Code, add it to the `deny` list under `permissions`.
- Per ATAS, real-time market analysis through AI Connect is offered for crypto; for ES / NQ, Laya's per-candle reads
  stay with the runner and the bridge (2.6), which are built for that. On day 1, ask Laya in that chat what AI Connect
  can read on your ES / NQ chart, and note the answer.

**Risk Manager (a second safety net, in ATAS itself).** On the demo account, set a daily loss limit of 2 x your
risk per trade and at most 3 trades. It stops trading in ATAS even if something outside the strategy misbehaves.

**Options X-Ray (gamma / options delta).** You can add it to the chart for your own eyes. It is not part of Fabio's
method, so Laya does not use it (`FABIO_TOOLS.md`).

**MBO data.** Not used: Fabio reads executed orders (delta, big trades), not the order book.

---

## Part 3 - every trading day

| When (New York time) | Do |
|---|---|
| before 09:00 | Open ATAS X (chart + indicator + strategy), then `./start-laya.sh` (Laya + dashboard). Mac plugged in, sleep off (System Settings -> Battery / Lock Screen). Open the Claude chat with AI Connect. |
| 09:00-09:30 | Read **Laya's plan for today** in the desk. Disagree? Ask Laya about it. In the AI Connect chat: "Put the plan on the chart". |
| 09:30-16:00 | Watch the tape. On alerts / set-ups: read her reason, look at the footprint yourself. |
| after 16:00 | Stop Laya. In the AI Connect chat: "Review the day". In the desk, review the set-ups and her reads. Note them in the log below. |

Convert the New York times to your own clock (they shift by an hour when US daylight saving changes).

Stages:
- **Days 1-5: watch.** Trading OFF. Judge whether Laya's reads and alerts match what you see on the footprint.
- **Days 6-14: simulator.** Trading ON, on the ATAS **demo** portfolio. The strategy enters only set-ups Laya approves,
  with the stop and target attached, flat at the end of the session.
- **Live** only after the trial, if the log says so, with 1 micro contract.

### Daily log (copy into a sheet)
| Date | Laya's bias | Plan right? | Alerts | Set-ups | Approved | Result (R) | Laya right when she rejected? | Notes |
|---|---|---|---|---|---|---|---|---|

---

## Part 4 - judging the trial
- **Set-ups:** "History on this chart" in the indicator panel gives trades, win rate, total R, max drawdown for all the
  loaded history. The desk shows the session's set-ups with Laya's decision and outcome.
- **Laya's value:** compare approved vs rejected set-ups. A good filter rejects more losers than winners.
- **Her reads:** do her phases (approaching / battle / set-up) match what you see on the footprint? Note disagreements.
- **The plan:** how often was her pre-market bias and key-level map right?
- Full record: `~/.config/FabioBridge/journal.jsonl` (every set-up, decision, read and outcome, one JSON per line).
- Tuning (indicator settings): zone tolerance, absorption and confirmation multipliers, big-trade filter. They are
  starting values, not Fabio's numbers: change one at a time and compare the history totals.

On day 12, decide whether AI Connect earned its place. If it did not, PRO still runs everything else here (engine,
strategy, bridge, Laya's per-candle reads and plan, the desk).

---

## Troubleshooting
| Symptom | Fix |
|---|---|
| `install-mac.sh` says ATAS.Indicators.dll not found | Pass the bundle: `./install-mac.sh "/Applications/ATAS X.app/Contents/MonoBundle"` |
| Build errors (red lines) | Send them to Claude: an ATAS API name differs in your version. |
| Indicator not in the list | ATAS X log (Logs button): look for "Rejected assembly" and send the line. |
| Panel: "outside the cash session" during US hours | Indicator setting "Exchange time zone" = `America/New_York`. |
| No overnight / London levels | The chart is on the day session: switch it to the full 24h session. |
| Desk: bridge offline | The strategy is not started on a chart, or "Bridge for Laya" is off, or the port is not 8765. |
| Desk shows only example data | You opened it in a browser: open the installed app. Allow "full access" in its Permissions. |
| Start Laya stops at once | Settings -> folder must contain `laya/laya_runner.py` (default `~/retreat-calculator/atas-fabio`). |
| Laya's reads say "could not review" | Run `claude -p "hello"` in Terminal: log in again if needed; unset ANTHROPIC_API_KEY. |
| Reads arrive late | Normal read time is 10-60 s. Choose "Sonnet" in Settings; keep 5-minute candles. |
| Set-up approved but not traded | Price moved more than 8 ticks before approval (safety guard), or Trading is OFF. |
| No notifications | Desk app -> Permissions -> notifications on; macOS -> Notifications -> allow it. |

## Safety rules (built in, Laya cannot change them)
Stop on every trade, max 2 losing trades and 3 set-ups per session, no entry after an 8-tick run, flat at 16:00 New
York, one position at a time, trading OFF by default. The approval step lets Laya veto; it cannot remove a stop.

This is a decision-support and automation tool, not financial advice. Futures trading can lose more than your stake.
