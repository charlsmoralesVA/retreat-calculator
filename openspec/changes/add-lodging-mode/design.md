# Design

## Context

Every calculator input is a number today. `BudgetInputs` in `src/lib/calculate.ts` is all numeric, and `INPUT_FIELDS` is derived from `DEFAULT_INPUTS` with `Object.keys`. That one list drives four things: validation (`isInvalid`), the form (`Calculator.tsx` maps over it for inputs and labels), reading saved budgets (`parseInputs` in `src/budgets/api.ts` copies each numeric field), and the `InputField` type used for labels and `invalidFields`. A lodging mode (`"perPerson" | "perRoom"`) would be the first non-numeric input, so those assumptions need to be made explicit. See proposal.md for motivation and the specs for behavior.

## Goals / Non-Goals

**Goals:**
- Per-room lodging computed by the same pure function as everything else, unit-tested without a browser.
- Keep the generic numeric handling (validation, form loop, parsing) working for numeric fields, and add the one enum field explicitly rather than loosening types.

**Non-Goals:**
- Mixed room types, single supplements, a separate retreat-name field, or changes to how food, activities, travel or the client price work.
- Any database migration.

## Decisions

- **Mode and occupancy are two inputs:** `lodgingMode: 'perPerson' | 'perRoom'` and a numeric `roomOccupancy` (people per room). The existing `lodgingRate` field keeps its name and takes whichever meaning the mode gives it, so saved budgets and the cost model stay one shape. Alternative: a separate `roomRate` field; rejected because it would leave a stale, unused rate in the other mode and double the lodging inputs.
- **Keep the numeric field list explicit.** Replace `Object.keys(DEFAULT_INPUTS)` with an explicit `NUMERIC_FIELDS` tuple (the existing numeric fields plus `roomOccupancy`, ordered with `roomOccupancy` right after `lodgingRate`). `INPUT_FIELDS`, the `InputField` type, the form loop, `isInvalid` and `parseInputs`' numeric loop then stay as they are, typed over numbers only. `lodgingMode` is handled separately everywhere it appears. Alternative: widen every input to `number | string`; rejected because it would push type checks into every calculation.
- **Occupancy validation depends on the mode.** In per-room mode `roomOccupancy` must be a whole number of 1 or more, so blank or zero is flagged and lodging is excluded until it is fixed. In per-person mode it is never validated, so a stale or half-typed value cannot raise a hidden error. Alternative: default occupancy to 2; rejected to keep the existing rule that every numeric input starts at zero.
- **Rooms are rounded up with `Math.ceil`.** `roomsNeeded = ceil(headcount / roomOccupancy)`, an integer division of two whole numbers, so no float noise. A partly filled room is charged in full because that is what the venue bills. `BudgetResult` gains `roomsNeeded: number | null`, `null` in per-person mode or when occupancy is invalid, so "not available" stays explicit and typed.
- **Lodging formula:** per person is `headcount x nights x rate` (unchanged); per room is `roomsNeeded x nights x rate`. With one person per room the two agree for the same rate, which gives a cheap consistency test.
- **Stored shape is additive; `version` stays 1.** `serializeInputs` already spreads all inputs, so `lodgingMode` and `roomOccupancy` are written automatically. `parseInputs` reads `lodgingMode` explicitly: only the exact string `perRoom` selects per room, anything else (missing, malformed, unknown) means per person. Older budgets therefore open unchanged. Saving writes the explicit mode.
- **UI:** a two-option control (radio group or segmented toggle) above the lodging rate, labelled "Lodging is priced per". The rate label flips between "Lodging per person per night ($)" and "Lodging per room per night ($)". The "People per room" input appears directly after the rate only in per-room mode. A "Rooms needed" row appears in the totals in per-room mode, showing N/A when occupancy is invalid. Values typed into the hidden input are kept in component state so switching back and forth does not lose them.

## Risks / Trade-offs

- [The rate value carries over when the mode flips, so $150 per person becomes $150 per room] -> the label and totals change immediately and the value is kept deliberately; the spec calls this out so it is a known, tested behavior rather than a surprise.
- [Round-up can surprise planners with an odd group] -> the Rooms needed row makes the rounding visible.
- [The first non-numeric field could leak a type error into generic code] -> the explicit numeric field list and a compiler-checked split keep the generic paths numeric-only; the type check and build catch any miss.
- [Saved budgets with an unknown mode string] -> treated as per person, covered by a spec scenario and a parse test.
- [The overview doc drifts again] -> updating it is a task in this change.
