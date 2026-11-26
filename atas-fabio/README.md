# Fabio orderflow monitor for ATAS

Indicator + strategy skeleton that watches for the three pillars of the method taught in the
`fabio-orderflow-strategy` skill: **bias -> location -> confirmation (battle of orders)**.
See `SPEC.md` for exactly which rules are implemented, which are my assumptions, and which are missing.

## Layout
- `src/Core/` ATAS-independent engine (profiles, bias, zones, battle of orders, exits, checklist). netstandard2.0.
- `src/Atas/` thin ATAS adapter: `FabioFeed` (candles -> engine), `FabioLevelsIndicator`, `FabioStrategy`.
- `tests/FabioOrderflow.Tests/` 94 checks on synthetic sessions: `dotnet run --project tests/FabioOrderflow.Tests`.
- `tools/AtasCompileCheck/` compiles the adapter against hand-written stubs of the ATAS API (syntax/type sanity only).

## What the indicator shows
Prior-session POC / VAH / VAL, the 3-month composition value area, the initial balance, signal dots on the
confirming bar, and a panel with: bias (and why), location vs prior value, the nearest zone's checklist
(`[x]` met, `[ ]` not met, `[?]` unknown, each tagged Stated / Principle / Assumed), entry, stop, target, R:R.
A dot appears only when every required line is met on a CLOSED bar.

## Build for ATAS (not yet done against real ATAS)
1. Create a class library for the .NET version your ATAS build uses (ATAS ships an SDK sample / template).
2. Add `src/Core/*.cs` and `src/Atas/*.cs`; reference the ATAS DLLs from your ATAS install folder.
3. Fix any member-name differences. The most version-sensitive spots: `OnRender` (define `NO_PANEL` to drop the
   panel), `VisualMode`, the `DataSeries[0] = ...` pattern, `LogInfo`, and `candle.Time` (see time offset below).
4. Put the DLL where ATAS loads custom indicators, add "Fabio Orderflow Monitor" to a 5-minute chart.

## Settings you must check first
- **Time offset / RTH start / RTH end.** Sessions are cash-session bars in the clock your chart shows. If the panel
  says "outside regular session" during the cash session, adjust the offset. Default 09:30-16:00.
- Tick size is read from the instrument.
- Thresholds (`Absorb |delta| x average`, `Confirm delta x average`, zone tolerance) are my placeholders, not Fabio's.
  On purely random bars the engine still found 24 signals in 60 sessions, so they are loose until calibrated on real data.

## Strategy
`FabioStrategy` is signal-only (it logs). Order placement is a marked TODO: the ATAS order API cannot be checked
from here. Run it on sim first.

## Honest status
- Core engine: compiled, 94 tests passing on synthetic data.
- ATAS adapter: compiles against stubs only. Not run inside ATAS.
- Strategy fidelity: bias, zones, battle of orders, reactor filter and exits only. Models that live in the
  skill's reference files are not built (list in `SPEC.md`).
- Not financial advice. Nothing here is backtested on real market data.
