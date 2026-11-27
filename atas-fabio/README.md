# Fabio orderflow monitor for ATAS

> **Starting the ATAS trial? Follow [`SETUP_GUIDE.md`](SETUP_GUIDE.md)** - everything in order, from the Mac tools to the daily routine.
> **Which of Fabio's tools ATAS provides, and what Laya watches: [`FABIO_TOOLS.md`](FABIO_TOOLS.md).**

An ATAS indicator (and an optional strategy) that watches for the three pillars of the method in the
`fabio-orderflow-strategy` skill: **bias -> location -> confirmation (battle of orders)**.
`SPEC.md` lists every rule, where it comes from, and what is still missing.

> **There is no Pine Script.** Pine Script is TradingView's language. ATAS indicators are C# programs compiled into a
> `.dll` that ATAS loads. This folder contains that C# source plus an installer that builds the `.dll` on your PC.

## Install it in ATAS
One build runs in the classic ATAS and in ATAS X (docs.atas.net). Install the **.NET 10 SDK** first
(https://dotnet.microsoft.com/download; on a Mac also `brew install --cask dotnet-sdk`).

| You use | Run | DLLs go to |
|---|---|---|
| Classic ATAS or ATAS X on **Windows** | double-click **`install.cmd`** (or `install.cmd "D:\your\ATAS folder"`) | `%APPDATA%\ATAS\Indicators` + `\Strategies` (ATAS X: `%APPDATA%\ATAS X\...`) |
| ATAS X on **macOS** | `./install-mac.sh` in Terminal | `~/Library/Application Support/ATAS/Indicators` + `/Strategies` |

The installers find ATAS, pick the .NET version from ATAS's own runtime file, build against **your** ATAS files and copy
the two DLLs. ATAS X loads them by itself; classic ATAS shows a reload button.
Then open a **5-minute ES or NQ chart**, load **at least 20 days** (60+ for the 3-month value area) and add the
indicator **"Fabio Orderflow Monitor"**.

If the build stops with an error, send the red lines: the code was written against ATAS's open-source API and checked
against stand-ins, but it has not been compiled against the real ATAS files yet.

## What you see on the chart
| On the chart | Meaning |
|---|---|
| Gold / red / green lines | Previous session POC / VAH / VAL (cash session, New York time) |
| Grey dashes | 3-month composition value area (VAH / VAL) |
| Blue line | Session VWAP (context only) |
| Purple dashes | Initial balance |
| Green up-arrow / red down-arrow | A set-up: every required rule was met on that **closed** bar |
| White / red / green dashes | Entry / stop / target of the trade from that set-up |
| Green square / red cross / grey dot | Result: target / stop / flat at the end of the session |
| Panel (top left) | Bias and why, location, VWAP, IB, **history statistics**, and the live checklist of the nearest zone |

**Past days and weeks:** yes. The indicator calculates every loaded bar, so all set-ups, entries, stops, targets and
results are drawn for the whole history on the chart, and the panel shows the totals (trades, win rate, total R,
average R, max drawdown). Load more days to see more.

**Alerts:** a new live set-up raises an ATAS alert with entry, stop and target (setting "Alert on new set-up").

## Which data it uses, and how
| Data | Used for | Role |
|---|---|---|
| Volume profile of the previous cash session (POC, VAH, VAL) | Zones: VAL/VAH are faded inside value; POC becomes a rejector after acceptance outside | **Required** (location) |
| Day-over-day value-area shifts, 5-session narrative, 3-month composition profile | Bias; a long against the long-term auction is blocked | **Required** (bias) |
| Delta of each bar vs recent bars | Absorption (heavy delta, no result) then the other side winning in the body | **Required** (confirmation) |
| Effort vs result on the approach (delta vs distance travelled) | The reactor filter: an effortless explosion into the level is skipped | **Required** |
| Footprint rows (bid/ask volume per price) | Where the losing side's heavy prints sat inside the absorption bar | Confluence (shown, not required) |
| Big trades / prints (ATAS cumulative trades, filter 60 contracts, auto-scaled above 3 per bar) | Big prints with the winning side | Confluence, live only |
| Delta / sniper level (strongest delta price of the session profile) | Agrees with the zone? | Confluence |
| Swing sniper level (profile over the last swing leg, strongest delta price) | Reload level for Laya; counts inside value | Shown to Laya |
| Session VWAP | Context in the panel | Shown only |

**Not in the skill, so not added as Fabio's rules:** "delta EMA" and "POH". If you have a source where Fabio defines them,
send it and they can be added. The skill's detailed models (stacked profiles, momentum / reload / locked-value models,
IVB rules, gap model, pre-market mapping) live in reference files that were not available; see `SPEC.md`.

## Laya on every candle (recommended)
Laya (Claude with the Fabio skill) reads **every closed candle** and decides every set-up: the strategy publishes a
candle card per bar on its local bridge, `laya/laya_runner.py` hands each one to Claude Code (`claude -p`, your plan),
and the **Laya Tape Desk** app (https://claude.ai/artifact/HcjbAHrF2AEQcpW2o7Jfad, install it from the Claude desktop
app) shows the live tape with her reads, set-ups, Mac notifications and a menu-bar status. Setup: **`laya/README.md`**.

## The strategy and Laya (optional)
"Fabio Orderflow Strategy" starts with **trading OFF**: it only reports set-ups. It also runs a **bridge for Laya**
(Claude with the Fabio skill) on `localhost:8765`: with "Require Laya approval" on, each set-up waits for Laya to
approve or reject it. Switched on, it trades one position: market entry, then a stop order and a limit target; when one
fills the other is cancelled; flat at the end of the cash session; stops for the session after 2 losing trades;
an approved entry is skipped if price has run more than 8 ticks. Laya can veto, never lift these limits.
Setup on the Mac, costs and the staged go-live: **`laya/README.md`**. **Use a simulation account first.**

## Settings to check first
- **Exchange time zone** (default `America/New_York`) and **cash session** 09:30-16:00. Your own chart time zone does
  not matter: candles are converted from UTC to New York time, daylight saving included.
- The absorption / confirmation multipliers and zone tolerance are my starting values, not Fabio's numbers. Watch the
  history statistics on your own instruments and adjust.

## For developers
| Path | What it is |
|---|---|
| `src/Core/` | ATAS-independent engine (profiles, bias, zones, battle of orders, confluence, exits, tick aggregation, backtester) |
| `src/Atas/` | ATAS adapter (`FabioFeed`, indicator, strategy) and the two project files (`Indicator/`, `Strategy/`) |
| `tests/FabioOrderflow.Tests/` | 292 checks: `dotnet run --project tests/FabioOrderflow.Tests` |
| `laya/` | `laya_runner.py` (Laya reads every candle), MCP server, her instructions, `check_connection.py`, sample real reads |
| `app/` | Laya Tape Desk app source (`laya-desk.src.html` + `build_desk.py`), browser test `tests/desk_test.cjs` |
| `tools/BridgeDemo/` | Practice feed for Laya without ATAS (same engine, same bridge) |
| `tools/Replay/` | `fabio-replay`: run trade CSVs through the engine outside ATAS |
| `tools/AtasCompileCheck/`, `tools/AtasStubs/` | Stand-ins for the ATAS API (built from ATAS's open-source code) for compiling and exercising the adapter on Linux |

Manual build: `dotnet build src/Atas/Indicator/FabioOrderflow.Indicator.csproj -c Release -p:ATAS_BASE="C:\Program Files (x86)\ATAS Platform"`
(add `-p:AtasTarget=net8.0-windows` for ATAS 7.x).

## Honest status
- Engine, confluence, backtester, bridge and replay tools: 292 checks pass, including the real indicator and strategy
  classes driven the way ATAS calls them (history load, live ticks, alerts, orders with simulated fills and rejections,
  Laya approve / reject / timeout, the bridge over real HTTP).
- Laya's MCP server tested end to end with a real MCP client against the practice bridge.
- The ATAS calls follow ATAS's developer docs and open-source indicators; builds cleanly for Windows (.NET 8 / 10) and
  macOS (.NET 10) against stand-ins of the ATAS API. **Not yet compiled against your real ATAS files or run inside ATAS**:
  the installer does that compile on your machine.
- The thresholds are placeholders and nothing has been run on real market data. Not financial advice.
