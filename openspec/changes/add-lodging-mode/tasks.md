# Tasks

## 1. Calculation

- [x] 1.1 In `src/lib/calculate.ts` add `lodgingMode` and `roomOccupancy` to `BudgetInputs` and `DEFAULT_INPUTS` (per person, 0), and replace the `Object.keys` field list with an explicit numeric field tuple that places `roomOccupancy` after `lodgingRate`; verify `npx tsc --noEmit` passes and the existing calculation tests still pass
- [x] 1.2 Validate `roomOccupancy` only in per-room mode (whole number of 1 or more; blank or zero is flagged and lodging excluded) and never in per-person mode; verify unit tests for blank, zero, negative, fractional and NaN occupancy in per-room mode, and that the same values raise nothing in per-person mode
- [x] 1.3 Compute `roomsNeeded = ceil(headcount / roomOccupancy)` and lodging = rooms x nights x rate in per-room mode, with `roomsNeeded` null in per-person mode or when occupancy is invalid; verify unit tests for both spec worked examples (rooms 5, lodging 2,700, total 7,590, per person 759; and 7 people at 2 per room gives 4 rooms and lodging 1,800)
- [x] 1.4 Cover the remaining spec scenarios in the same test file and verify they pass: one person per room equals per-person pricing, zero headcount gives 0 rooms, only lodging differs between modes for the same inputs, and the contingency and client price are computed from the resulting total cost

## 2. Reading saved budgets

- [x] 2.1 In `src/budgets/api.ts` read `lodgingMode` explicitly so only the exact value `perRoom` selects per room and anything else means per person, leaving the numeric loop unchanged; verify unit tests for a round trip of both modes, a stored object with no mode, and unknown or malformed modes (`'room'`, a number, null) all parsing as per person with every other input intact
- [x] 2.2 Verify a legacy stored object re-serializes with `lodgingMode: 'perPerson'` and `version` still 1

## 3. Calculator UI

- [x] 3.1 Add the "Lodging is priced per" two-option control above the lodging rate, flip the rate label between per person and per room, and show the "People per room" input after the rate only in per-room mode while keeping typed values when switching; verify component tests for the default state, the switch, the label flip, and values surviving a switch back and forth
- [x] 3.2 Add the "Rooms needed" row to the totals in per-room mode (N/A when occupancy is invalid) and the occupancy error message ("Enter a whole number, 1 or more."); verify component tests that rooms round up, the row is absent in per-person mode, and a blank occupancy flags the field, shows N/A and removes lodging from the totals
- [x] 3.3 Verify visually in a browser (light and dark, 390px width) that the control, the conditional field and the Rooms needed row look right, and note the check in the PR description

## 4. Saving and reopening

- [x] 4.1 Add component tests with the fake backend: saving a per-room budget and reopening restores mode, occupancy, rooms needed and totals; a budget stored without a mode opens per person with unchanged totals and re-saves with the explicit mode; an unrecognised stored mode opens per person
- [x] 4.2 Extend `e2e/budgets.e2e.mjs` so one user saves a per-room budget (for example 25 people, 2 per room, 2 nights at 100 per room, 1,000 venue fee), reloads and opens it, and the rooms needed (13) and total (3,600) match; verify `npm run e2e` passes against the local stack

## 5. Documentation and final checks

- [x] 5.1 Update `docs/PROJECT_OVERVIEW.md`: move lodging mode and people per room into the implemented inputs, describe per-room pricing and the round-up and add Rooms needed to the outputs, remove lodging mode and retreat name from "Planned", and state that the saved budget's name serves as the retreat name; verify no remaining text calls lodging mode or retreat name planned
- [x] 5.2 Run `npm test`, `npm run build` and `npm run e2e` together and verify all pass

## Workflow follow-up

- Archive the change after the PR is merged, syncing the modified `retreat-budget-calculation` and `budget-persistence` specs.
