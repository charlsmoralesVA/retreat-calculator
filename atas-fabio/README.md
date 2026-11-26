# Fabio orderflow monitor for ATAS

Indicator, strategy skeleton and replay/backtest tooling for the three pillars of the method taught in the
`fabio-orderflow-strategy` skill: **bias -> location -> confirmation (battle of orders)**.
`SPEC.md` lists exactly which rules are implemented, which numbers are mine, and which models are still missing.

## Layout
| Path | What it is |
|---|---|
| `src/Core/` | ATAS-independent engine: profiles, bias, zones, battle of orders, reactor filter, exits, checklist, tick aggregation, CSV reader, backtester. `netstandard2.0`. |
| `src/Atas/` | Thin ATAS adapter (`FabioFeed`, `FabioLevelsIndicator`, `FabioStrategy`) and the project file that builds it against **your** ATAS install. |
| `tools/Replay/` | `fabio-replay`: raw trades (CSV) -> engine -> simulated trades and statistics. Runs anywhere, no ATAS needed. |
| `tests/FabioOrderflow.Tests/` | 171 checks. `dotnet run --project tests/FabioOrderflow.Tests` |
| `tools/AtasCompileCheck/`, `tools/AtasStubs/` | Hand-written stubs of the ATAS API, used only to check the adapter's syntax and the project wiring. Not the real SDK. |

## 1. Run it on real trades (works today, outside ATAS)
Trade CSV: `timestamp,price,volume[,aggressor]`, one row per executed trade, aggressor = who **initiated** it
(`B`/`BUY`/`1` = buyer lifted the offer, `S`/`SELL`/`-1` = seller hit the bid; missing = tick rule). Timestamps must be in the
clock the cash-session times refer to, or ISO-8601 with `Z`/offset plus `--offset-hours`. Vendors code the aggressor differently:
map it with `--buy-tokens` / `--sell-tokens` (check your vendor's documentation).

```
dotnet run --project tools/Replay -- --ticks trades.csv --tick-size 0.25 --minutes 5 --rth 09:30-16:00 \
    --point-value 50 --slippage-ticks 1 --trades-out trades_out.csv --show-checklists --all-trades
dotnet run --project tools/Replay -- --ticks trades.csv --set AbsorbDeltaMult=1.5 --set ZoneToleranceTicks=8
dotnet publish tools/Replay -c Release -r win-x64 --self-contained false -o out     # for a Windows machine
```
`--make-demo demo.csv` writes **synthetic** trades so you can check the plumbing. Its results mean nothing about any market.

## 2. The indicator in ATAS (not yet run inside ATAS)
Prior-session POC / VAH / VAL, the 3-month composition value area, the initial balance, signal dots on the confirming bar,
and a panel with: bias (and why), location vs prior value, the nearest zone's checklist (`[x]` met, `[ ]` not met, `[?]` unknown,
each line tagged Stated / Principle / Assumed), entry, stop, target, R:R. A dot appears only when every required line is met on a
**closed** bar.

Build against your ATAS install folder (the one containing `ATAS.Indicators.dll`):
```
dotnet build src/Atas/FabioOrderflow.Atas.csproj -c Release -p:AtasDir="C:\Program Files (x86)\ATAS Platform"
```
Pass `-p:AtasTarget=net48` if your ATAS build runs on .NET Framework, `-p:AtasUseWpf=true` if its assemblies need WPF,
`-p:DefineConstants=NO_PANEL` to drop the on-chart panel if `OnRender` does not match your version.
Copy **both** `FabioOrderflow.Atas.dll` and `FabioOrderflow.Core.dll` to ATAS's custom indicators/strategies folder.
Expect a few member-name fixes: the adapter was compiled against stubs written from memory of the ATAS SDK, not the real DLLs.

First settings to check: **time offset / RTH start / RTH end** (if the panel says "outside regular session" during the cash
session, adjust). Tick size is read from the instrument.

## 3. Calibrating the thresholds without fooling yourself
The absorb / confirm / zone-tolerance numbers are my placeholders, not Fabio's. On purely random bars the engine still produces
signals, so they are loose until tuned on real data.
- Tune on one period, judge on a **different** period. Dozens of trades prove nothing; hundreds of trades across different regimes start to.
- Look at the checklists of the signals (`--show-checklists`) against the chart: do they match what Fabio would call a set-up?
- Keep slippage and commission realistic. The simulator is deliberately pessimistic (entry at the next open + slippage, stop wins same-bar ties).
- Sessions end flat. That cut-off penalises far targets (a nearer stop resolves faster than a farther target). On a fair random walk
  the 1:2 hit rate falls from 33% to ~28% purely from the cut-off, so compare like with like.
- A backtest cannot see intrabar order, queue position or your discretion. It is a calibration aid, not evidence of an edge.

## Strategy (`FabioStrategy`)
Signal-only: it logs the signal. Order placement is a marked TODO because the ATAS order API cannot be checked from outside ATAS.
Run on sim first.

## Honest status
- Core engine, tick aggregation, CSV reader, backtester and replay tool: compiled and covered by 171 passing checks, including an end-to-end run from raw trades to a simulated trade and a null-model check that the simulator has no hidden bias.
- ATAS adapter: compiles against stubs, and its project file builds against a stub folder. **Never run inside ATAS.**
- Strategy fidelity: bias, zones, battle of orders, reactor filter and exits only. Models that live in the skill's reference files are
  not built (list in `SPEC.md`); they need those files.
- Nothing has been run on real market data. Not financial advice.
