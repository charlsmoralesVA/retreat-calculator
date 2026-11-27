# Adding Fabio's models to the engine and the backtest

**For Claude Code on the Mac**, which has the `favio-orderflow-scalp` skill (SKILL.md, PLAYBOOK.md, brain/01-08). Paste:

> Read atas-fabio/MODELS_GUIDE.md and implement Fabio's models from the favio-orderflow-scalp skill as it says, one
> model per commit, then push the branch.

## Where things stand
- The engine runs **models** from `src/Core/Models.cs`. Each one has a switch, a name that appears on every set-up,
  candle card and backtest report, and its own settings that a sweep can tune.
- Built so far: **ZoneReaction** (SKILL.md's core model: VAL / VAH faded from inside value, the POC as a rejector after
  acceptance outside value, delta levels) and **IVB-2 (provisional)** (a guess at the initial-balance model, off by
  default, rules marked Assumed).
- Missing: every model written in the skill's chapters, such as momentum, reload trend, locked value, stair, gap, the
  real IVB rules, trapped participants, delta print, deep effort. They live in `brain/04_models_and_entries.md` and
  `brain/03_order_flow_tools.md`, which only the Mac can read.

## Rules for each model
1. **Take the rules from the skill, not from memory.** Use `Grep` on the chapter for the model's name, read that
   section, and write each condition as a `Check` whose `Detail` cites it. Tag every check: `Provenance.Stated`
   (the chapter says it), `Principle` (follows from what it says) or `Assumed` (a number or detail the chapter leaves
   open).
2. **Every Assumed number is a setting.** Add it to `FabioParams.cs` with a comment `// [Assumed] ...` (or `[Stated]`
   with the chapter's number), so the sweep can tune it and the report shows it.
3. **New models start switched off** (`EnableX = false`) until their backtest and the simulator days say otherwise,
   so the live strategy does not change behind the trader's back.
4. **Same confirmation discipline.** Fabio's universal trigger is the battle of orders (one side absorbed, the other
   wins in the body), executed data only. Reuse `SetupEvaluator` helpers (absorption, confirmation, approach,
   footprint) where the chapter's model uses them; do not invent other triggers.
5. **Never weaken the risk rules** (stop on every trade, max set-ups and losers per session, flat at 16:00). A model
   gives entry, stop and target; the strategy does the rest.

## How to add one
1. `src/Core/Models.cs`, or a new file `src/Core/Model<Name>.cs`: a class derived from `SetupModel`
   - `Name` (short, shown in reports), `Description` (one line: what it trades, and the chapter it comes from)
   - `IsEnabled` / `SetEnabled` on its `Enable<Name>` field; `TunableSettings` lists its settings
   - zone models: `OwnsZone` + `EvaluateZone`; other models: `Evaluate(ModelContext c)` returning `SetupEvaluation`s with
     `Model = Name`, `Side`, `Zone` (the level traded; make one if needed), `Checks`, `Entry` / `Stop` / `Target` / `Rr`,
     `AbsorbIndex` / `ConfirmIndex` (indexes into `c.Bars`)
2. Register it in `ModelRegistry.All`.
3. Needs data the context lacks (VWAP, overnight / London levels, gap, swing levels, CVD ...)? Add a field to
   `ModelContext` and fill it in `FabioEngine.Evaluate` from the engine's existing state. Do not recompute it in the
   model.
4. Tests in `tests/FabioOrderflow.Tests/`: one synthetic scenario where the model must fire (with the exact entry /
   stop / target) and one where it must not (each required check failing once). Look at `Program.cs` `TestEngineLong`
   and `Synth` for how scenarios are built.
5. Check: `dotnet run --project tests/FabioOrderflow.Tests` (all pass), `./backtest.sh list` (the model and its
   settings), `./backtest.sh models --bars data/DEMO.bars.gz` (it runs; demo numbers mean nothing).
6. Add a row for it in `SPEC.md` and `FABIO_TOOLS.md`, and add it to Laya's instructions if the card needs a new field.
7. Commit (one model per commit) and push the branch.

## Then, on real data
`./backtest-atas.sh` (ATAS export) or `./backtest.sh models --bars data/ES.bars.gz` (Databento) compares every model
alone and all together, in- and out-of-sample. Tune a model with
`./backtest.sh sweep --bars ... --set Enable<Name>=true --set EnableZoneReaction=false --grid "<its settings>"`, and
paste the chosen settings line into ATAS ("Engine settings from the backtest").
