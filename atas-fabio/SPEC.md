# Traceability: what the indicator does, and where each rule comes from

Source of truth used: `SKILL.md` of the `fabio-orderflow-strategy` skill. The skill's `references/*.md` files
(setups-and-entries, bias-and-mapping, deep-charts-models, glossary-and-signal-logic) were **not available** in the
session that built this, so every model that lives only in those files is listed as NOT IMPLEMENTED below.

Provenance tags (also shown next to every line of the on-chart checklist):
- **Stated**: the rule or number is written in SKILL.md.
- **Principle**: SKILL.md states the idea; the numeric implementation is mine and must be calibrated.
- **Assumed**: not in SKILL.md.

## Implemented

| SKILL.md says | Code | Tag |
|---|---|---|
| Bias from daily value-area shifts: higher = bullish, lower = bearish, overlapping = neutral | `BiasEngine.ValueShift` (both VA edges moved; `StrictValueShift` = no overlap at all) | Principle |
| Keep a multi-session narrative (>= 5 sessions); one counter-shift is usually a retracement | `BiasEngine.Narrative` (majority vote) | Stated |
| Composition profile (3-month); auction flips only when price accepts back inside it | `BiasEngine.Composition`, `ClassifyAuction`, `FinalBias` (acceptance = last N closes, N = 2 is mine) | Principle |
| VAH: sellers defend. VAL: buyers defend. POC: magnet while balanced, rejector once accepted outside value | `ZoneEngine.Locate`, `ZoneEngine.Build` | Stated |
| Delta/sniper levels: balanced = only outside the VA; swing = most significant even inside | `ZoneEngine.DeltaLevels` ("significant" = largest abs delta at a price is my reading) | Principle |
| Universal trigger: one side loses (absorbed: effort, no result), the other wins (aggressive, in the body, closes its way) | `BattleOfOrders.IsAbsorption`, `IsConfirmation` (multipliers are mine, relative to recent bars) | Principle |
| Reactor: judge how price arrived; struggle into the level = good, effortless explosion = worse | `BattleOfOrders.MeasureApproach` (efficiency = progress per unit of effort) | Principle |
| Weigh the entry against the long-term auction | Check "Bias supports side" (`RequireBiasAlignment`) | Principle |
| Executed order data: delta and big trades ("one side loses, the other wins") | Footprint rows of the absorption bar (`SetupEvaluator.FootprintCheck`), big prints from ATAS cumulative trades (`FabioFeed.RegisterCumulative`), delta level near the zone (`DeltaLevelCheck`): shown as **confluence**, not required, because SKILL.md gives no rule for combining them | Principle |
| VWAP (listed among the core concepts) | Session VWAP drawn and shown in the panel; no trade rule | Assumed (context only) |
| Stop just beyond the zone; target previous control level / POC / value area / ~1:2 | `SetupEvaluator` (nearest reference level with R:R >= 1, else 2R) | Stated |
| Big trades: ~60-75 contracts on 5m; no more than ~3 per candle, else scale the filter up | `BigTradeFilter` (60 / 3 stated, 1.25x scale-up mine) | Stated |
| Not a machine gun | `MaxSignalsPerSession`, `MinBarsBetweenSignals` | Principle |
| Entry flexible ("100 ways") | Signal = confirming bar close. Retest entries not built | Principle |
| IB / IVB named; gap fills named as mechanical | `InitialBalance`: IB range, breakout state, gap/fill status shown. Provisional IVB-2 zone behind `EnableIvb` (off by default) | Assumed |

## Not in SKILL.md, so not implemented as Fabio's rules

"Delta EMA" and "POH" do not appear in SKILL.md. They can be added if a source shows how Fabio uses them.

## NOT implemented (needs the reference files)

High / low volume node rules; stacked profiles + POC hint + explosion rule, and the "accepts below VAL / above the swing point" flip rule on stacked profiles;
pre-market scenario building (London + previous day), pre-market profile shapes (balanced / P / V), last line of defense,
balanced vs trending models, asymmetric bias days; reload levels, retreat and passover zones; band squeeze / compression;
trapped participants (reload vs rejection); stair model; gap model rules; momentum trend model (~75% win, ~1:1 quoted);
reload trend model; locked-value model; trigger-point entry confirmation; strong vs weak area reaction;
volume + price combined setup; filtering rules; VWAP usage; delta print; deep-effort / best-effort algo (40-range chart);
IVB Model 1 and Model 2 rules and the statistical protection level / TP (~65-70% hit rate quoted); volume ledge;
mean reversion inside the range; range-chart handling (80-range mapping chart).

When the reference files are provided, each becomes one more evaluator that returns the same `SetupEvaluation`
checklist, so it shows up on the chart panel and in the strategy without changing the adapter.

## Tooling that is not strategy

`TickAggregator`, `CsvTicks`, `Backtester`, `Replay` and `fabio-replay` are plumbing for testing the rules above on real
trades. They add no trading rules. Simulation assumptions (mine): entry at the next bar's open plus slippage, stop wins a
same-bar tie, stop exits pay slippage and a gap through the stop fills at the open, one position at a time, flat at the end of
the session, daily loss cap in R (default 3R, 0 = off).

## What the indicator cannot do

The skill is explicit that this is discretionary orderflow reading. The checklist makes the mechanical conditions
visible and flags candidates; whether "the other side clearly won" is a judgement call that the delta-based test only
approximates. Treat a signal as "conditions met, now look at the tape", not as an order to fire blindly.
