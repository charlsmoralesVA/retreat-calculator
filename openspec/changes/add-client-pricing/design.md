# Design

## Context

`calculateBudget` in `src/lib/calculate.ts` is a pure function ending at `total = subtotal + contingency` and `perPerson = total / headcount`. Inputs live in `BudgetInputs`; `INPUT_FIELDS`, `DEFAULT_INPUTS`, validation, and the calculator form are all derived from that one shape. Saved budgets store inputs as JSON with a `version` key, and `parseInputs` in `src/budgets/api.ts` already defaults any missing numeric field to 0. See proposal.md for motivation and the specs for behavior.

## Goals / Non-Goals

**Goals:**
- Client price computed by the same pure function as everything else, unit-tested without a browser.
- Markup flows through the existing input, validation, and save/open paths with minimal new code.

**Non-Goals:**
- Margin-% entry, negative markup, a client-facing view that hides markup, or itemized client quotes.
- Any database migration.

## Decisions

- **Markup, not margin, is the input.** It matches the "Markup %" name in the project overview and is safe at any value; a margin input must stay below 100%. Margin amount and margin % are shown as read-only outputs so both views are visible. Alternative: a markup/margin toggle; rejected as extra UI and a second formula to get wrong.
- **Cost base is total cost including contingency.** Contingency is money the planner expects to have available, so it belongs in the base. Alternative: markup on the subtotal only; rejected because it would make the client price understate what the retreat is expected to cost.
- **Round the per-person price first; quoted total = rounded price x headcount.** A client who multiplies the per-person price by headcount must get the quoted total exactly. Margin amount and margin % are measured against this quoted total, so they describe what the client actually pays. Alternative: round the total and derive per-person; rejected because the per-person price would then not multiply back.
- **Rounding on whole cents with float-noise normalisation.** Compute the price in cents, normalise binary noise (for example via `toPrecision(12)`) before `Math.round`, so exact half-cent cases round up predictably instead of depending on representation. Tests pin half-cent boundaries. Alternative: a decimal library; rejected as a new dependency for one operation.
- **Result shape:** `calculateBudget` gains `clientPrice: { perPerson, total, marginAmount, marginPct } | null`. It is `null` when markup is not above zero, and its fields are `null` when headcount is zero or the quoted total is zero (margin %), keeping the "not available" case explicit and typed.
- **Additive stored shape, no version bump.** `markupPct` is a new numeric field; `parseInputs` already supplies 0 for missing fields, so older budgets open unchanged. Saving writes `markupPct: 0` explicitly. The `version` stays at 1 because nothing is reinterpreted.
- **Validation reuses the existing rule** (finite and 0 or more). An invalid markup is excluded (treated as 0), so the client price section is hidden until corrected.
- **UI:** one new input labelled "Markup (%)" after contingency, and a "Client price" group of rows after the existing totals, rendered only when `clientPrice` is non-null, using the existing `emphasis` row style for the per-person and quoted total.

## Risks / Trade-offs

- [Rounding makes the quoted total differ from the exact marked-up cost by up to half a cent per attendee] -> intended and documented in the spec; the margin figures are based on the quoted total so they stay truthful.
- [Planners may confuse markup and margin] -> label the input "Markup (%)" and show margin % beside the margin amount.
- [Float rounding bugs at half-cent boundaries] -> normalise before rounding and add boundary tests.
- [The overview doc drifts again] -> updating it is a task in this change.
