# Proposal

## Why

Retreat Builders quote clients a price that includes their margin, but the calculator only shows what a retreat costs. Planners currently compute the client price by hand, where mixing up markup and margin or rounding inconsistently leads to mis-quoted retreats. This is the "Markup % / client price" item listed as planned in `docs/PROJECT_OVERVIEW.md`.

## What Changes

- Add an optional **Markup %** input (0 or more, no upper cap), applied to the total cost including contingency.
- When markup is above 0, show a **client price** section: per-person price, quoted total, margin amount, and margin %.
- The per-person price is rounded to the nearest cent first; the quoted total is defined as that rounded price times headcount, so a client who multiplies the per-person price by headcount gets exactly the quoted total.
- Margin amount and margin % are measured against the quoted total (what the client actually pays).
- Total cost keeps its exact value and is unaffected by markup.
- Saved budgets store markup with the other inputs. Budgets saved before this change open with no markup.
- Update `docs/PROJECT_OVERVIEW.md` to move markup and client price out of "Planned".

## Capabilities

### New Capabilities
<!-- None: client pricing extends the existing calculation capability. -->

### Modified Capabilities
- `retreat-budget-calculation`: the accepted inputs gain a markup percentage (modified requirement), and a new requirement defines the client price outputs and rounding.
- `budget-persistence`: a new requirement states that budgets saved without markup still open correctly and that markup is saved and restored.

## Impact

- Code: `src/lib/calculate.ts` (new input and result fields), `src/components/Calculator.tsx` (new input and results section), their tests, and the saved-inputs shape in `src/budgets/api.ts` (additive; no database migration).
- Docs: `docs/PROJECT_OVERVIEW.md`.
- No new dependencies, no database or auth changes.
- Out of scope: negative markup (discounts), a client-facing view that hides markup, and entering a margin % instead of markup %.
