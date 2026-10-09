# Tasks

## 1. Calculation

- [x] 1.1 Add `markupPct` to `BudgetInputs`, `DEFAULT_INPUTS` and the validation in `src/lib/calculate.ts`; verify unit tests show a negative, NaN or infinite markup is flagged and excluded, and that a markup above 100 is accepted
- [x] 1.2 Add a whole-cent rounding helper that normalises float noise before rounding half up; verify unit tests at exact half-cent boundaries (for example 1.005 and 2.675 dollars) round up
- [x] 1.3 Extend `calculateBudget` with `clientPrice` (per-person price rounded first, quoted total = rounded price x headcount, margin amount and margin % from the quoted total); verify unit tests for both spec worked examples (990.00 / 9,900.00 / 1,980.00 / 20.0% and 1,785.71 / 12,499.97 / 2,499.97 / 20.0%)
- [x] 1.4 Cover the edge cases in the same test file and verify they pass: `clientPrice` is null at zero or invalid markup, fields are null at zero headcount, total cost is identical with and without markup, contingency is inside the cost base, and per-person price x headcount equals the quoted total across a range of headcounts and costs

## 2. Calculator UI

- [x] 2.1 Add the "Markup (%)" input after contingency in `src/components/Calculator.tsx` with the existing invalid-field handling; verify a component test that a negative markup shows the error message and aria-invalid and leaves totals unchanged
- [x] 2.2 Render the "Client price" rows (per person, quoted total, margin amount, margin %) only when `clientPrice` is non-null, showing N/A at zero headcount, and style them with the existing totals styles; verify component tests that the section is absent at zero markup, appears when markup is set, updates live when headcount or markup changes, and shows N/A at zero headcount
- [x] 2.3 Verify visually in a browser (light and dark, 390px width) that the new input and section look right, and note the check in the PR description

## 3. Persistence

- [x] 3.1 Save `markupPct` through `serializeInputs` and read it in `parseInputs` in `src/budgets/api.ts` (version stays 1); verify unit tests that a round trip preserves markup and that a stored object without `markupPct` parses with markup 0 and all other fields intact
- [x] 3.2 Add component tests with the fake backend: saving a budget with 25% markup and reopening restores the field and client price, and a budget stored without `markupPct` opens with no client price section and re-saves with markup 0
- [x] 3.3 Extend `e2e/budgets.e2e.mjs` to save a budget with markup, reload, open it and check the client price; verify `npm run e2e` passes against the local stack

## 4. Documentation and final checks

- [x] 4.1 Update `docs/PROJECT_OVERVIEW.md`: move markup and client price from "Planned" to implemented inputs and outputs, describe the markup-vs-margin and rounding rules, and refresh the saved-budgets note; verify no remaining text says markup is planned or out of scope
- [x] 4.2 Run `npm test`, `npm run build` and `npm run e2e` together and verify all pass

## Workflow follow-up

- Archive the change after the PR is merged, syncing the modified `retreat-budget-calculation` and `budget-persistence` specs.
