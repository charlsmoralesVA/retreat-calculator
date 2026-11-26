# Fabio orderflow monitor for ATAS

An ATAS indicator (and an optional strategy) that watches for the three pillars of the method in the
`fabio-orderflow-strategy` skill: **bias -> location -> confirmation (battle of orders)**.
`SPEC.md` lists every rule, where it comes from, and what is still missing.

> **There is no Pine Script.** Pine Script is TradingView's language. ATAS indicators are C# programs compiled into a
> `.dll` that ATAS loads. This folder contains that C# source plus an installer that builds the `.dll` on your PC.

## Install it in ATAS (Windows)
1. Install the **.NET SDK** that matches your ATAS: **.NET 10 for ATAS 8.x**, **.NET 8 for ATAS 7.x**
   (https://dotnet.microsoft.com/download). The installer tells you if the wrong one is missing.
2. Download this folder (`atas-fabio`) to your PC and **close ATAS**.
3. Double-click **`install.cmd`**. If ATAS is not in `C:\Program Files (x86)\ATAS Platform`, run
   `install.cmd "D:\your\ATAS Platform"` from a terminal instead.
   It detects your ATAS version, builds against **your** ATAS files, and copies:
   - `FabioOrderflow.Indicator.dll` to `Documents\ATAS\Indicators`
   - `FabioOrderflow.Strategy.dll` to `%APPDATA%\ATAS\Strategies`
4. Start ATAS, open a **5-minute ES or NQ chart** and add the indicator **"Fabio Orderflow Monitor"**.
   Load **at least 20 days** (60+ days for the 3-month value area): in the chart settings, days to load.

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
| Session VWAP | Context in the panel | Shown only |

**Not in the skill, so not added as Fabio's rules:** "delta EMA" and "POH". If you have a source where Fabio defines them,
send it and they can be added. The skill's detailed models (stacked profiles, momentum / reload / locked-value models,
IVB rules, gap model, pre-market mapping) live in reference files that were not available; see `SPEC.md`.

## The strategy (optional)
"Fabio Orderflow Strategy" starts with **trading OFF**: it only reports set-ups. Switched on, it trades one position:
market entry, then a stop order and a limit target; when one fills the other is cancelled; flat at the end of the cash
session; stops for the session after 2 losing trades. **Use a simulation account first.**

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
| `tests/FabioOrderflow.Tests/` | 223 checks: `dotnet run --project tests/FabioOrderflow.Tests` |
| `tools/Replay/` | `fabio-replay`: run trade CSVs through the engine outside ATAS |
| `tools/AtasCompileCheck/`, `tools/AtasStubs/` | Stand-ins for the ATAS API (built from ATAS's open-source code) for compiling and exercising the adapter on Linux |

Manual build: `dotnet build src/Atas/Indicator/FabioOrderflow.Indicator.csproj -c Release -p:ATAS_BASE="C:\Program Files (x86)\ATAS Platform"`
(add `-p:AtasTarget=net8.0-windows` for ATAS 7.x).

## Honest status
- Engine, confluence, backtester and replay tool: 223 checks pass, including the real indicator and strategy classes driven
  the way ATAS calls them (history load, live ticks, alerts, order flow with simulated fills).
- Builds cleanly for .NET 8 and .NET 10 against stand-ins of the ATAS API. **Not yet compiled against your real ATAS files
  or run inside ATAS** - the installer does that compile on your PC.
- The thresholds are placeholders and nothing has been run on real market data. Not financial advice.
