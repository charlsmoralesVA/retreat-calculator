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
session cumulative delta; the strongest delta (sniper) levels of the day; the initial balance; overnight and London
high / low; the gap from the prior close; the approach into each zone (struggle or effortless); the engine's checklist.
Before the open she builds the day's plan (bias, key levels, if-then scenarios, when to stay out), as the skill's
"bias -> mapping the day" describes, and every candle is read against that plan.

---

## Part 1 - before the trial (about one hour, no ATAS needed)

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

### 1.5 Install the desk app
Open https://claude.ai/artifact/HcjbAHrF2AEQcpW2o7Jfad **in the Claude desktop app** and choose **Install as app**.
Open the installed app: in a browser it only shows example data. When it asks, allow **full access** (it runs `curl`
to the bridge and starts Laya), **notifications** and **menu bar**. Settings: the folder must be
`~/retreat-calculator/atas-fabio` (the default). Optional: run the practice of 1.4 again and press **Start Laya** in the
app instead of Terminal 2 - you see the plan, the market map and each read appear live.

---

## Part 2 - trial day 1 (about 45 minutes)

### 2.1 ATAS
1. Download **ATAS X for macOS** from atas.net and sign in.
2. Start the **14-day trial of PRO** (or ULTRA). The trial can be used once per account. START and PLUS cannot show
   real-time futures data, so do not test on them.
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
Start the strategy (its control button).

### 2.6 Start Laya
Open the **Laya Tape Desk** app: the bridge pill turns green, the market map fills. Press **Start Laya**. Before 09:00
New York time she waits; from 09:00 she builds the plan; from 09:30 every closed candle gets her read.
Alerts and set-ups arrive as Mac notifications, in the menu bar and in ATAS. Click a candle and **Ask Laya** for a
longer explanation.

---

## Part 3 - every trading day

| When (New York time) | Do |
|---|---|
| before 09:00 | Open ATAS X (chart + indicator + strategy), the desk app, **Start Laya**. Mac plugged in, sleep off (System Settings -> Battery / Lock Screen). |
| 09:00-09:30 | Read **Laya's plan for today** in the desk. Disagree? Ask Laya about it. |
| 09:30-16:00 | Watch the tape. On alerts / set-ups: read her reason, look at the footprint yourself. |
| after 16:00 | Stop Laya. In the desk, review the set-ups and her reads. Note them in the log below. |

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

Decide after day 12 which plan to keep: PRO is enough for everything here; ULTRA only for its extras (AI Connect, MBO).

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
