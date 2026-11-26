# ATAS skeleton for Fabio's orderflow method

Status: **draft, uncompiled, untested.** No .NET toolchain was available in the cloud session.

## Files
- `FabioCore.cs` – ATAS-independent logic: volume profile (POC/VAH/VAL), value-shift bias, narrative bias, absorption + confirmation approximation.
- `FabioLevelsIndicator.cs` – ATAS indicator: previous-session POC/VAH/VAL, bias, long/short confirmation markers.
- `FabioStrategy.cs` – ATAS strategy: signal-only by default, 1:2 target, stop beyond the zone, trade/day cap.

## What is implemented (taken from SKILL.md)
- Bias from daily value-area shifts (higher = bull, lower = bear, overlap = neutral), 5-session narrative.
- Location from VAH / VAL / POC.
- Confirmation: "one side loses, the other wins" at a zone (absorption bar, then a body-close confirmation bar).
- ~1:2 reward, stop beyond the zone, no machine-gunning (daily trade cap).

## What is NOT implemented yet (needs the skill's `references/*.md`, which did not sync)
IVB breakout (Models 1 and 2), gap model, stair model, momentum trend model, reload trend model, locked-value model,
stacked profiles + POC hint + explosion rule, band squeeze filter, delta/sniper level refinement, reload / retreat / passover zones,
the exact rules for trading with vs against bias, and the effort-vs-result approach filter.

## Honest limits
- Absorption/aggression thresholds in `ConfirmParams` are placeholders, **not Fabio's numbers**. Calibrate on your instrument.
- A rule-based bot approximates a discretionary read. Run it signal-only, then on sim, before any live use.
- Session boundaries use calendar days; switch to the cash (RTH) session for the real profile.
- Not financial advice.

## Build
Create an ATAS custom indicator/strategy project in Visual Studio (ATAS ships templates and an SDK sample folder),
reference the ATAS DLLs from your install, add these files, fix any member-name mismatches, build, and copy the DLL to the ATAS custom folder.
