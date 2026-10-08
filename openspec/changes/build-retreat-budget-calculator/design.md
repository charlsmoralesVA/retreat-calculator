# Design

## Context

Greenfield repository: only `docs/PROJECT_OVERVIEW.md` and OpenSpec exist. See proposal.md for motivation and scope. The overview's planned layout (`src/calculator`, `src/ui`, `tests`) is kept, but its input model changes: lodging, meals and activities are priced from a markup-inclusive rate card instead of planner-typed costs.

## Goals / Non-Goals

**Goals:**
- A pure calculator that is trivially unit-testable and reusable in a future API or CLI.
- Prices and brand values live in swappable config, not in logic or component styles.
- One command (`npm run dev`) to run locally.

**Non-Goals:**
- Persistence, accounts, multi-currency, hosting, a UI framework.
- Editing the rate card from the UI.

## Decisions

**Vite + vanilla TypeScript, no framework.** One page with a form and a results table; live recalculation is a handful of `input` listeners that call the calculator and re-render. *Alternative: Vite + React* — more tooling and surface for a single screen; revisit only if the UI grows.

**Pure `calculateBudget(input, rateCard): BudgetResult`.** The rate card is a parameter, not an import, so tests and later price updates pass their own. The module has no DOM or framework imports. Money stays as full-precision numbers; rounding happens only in a display formatter (`Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })`). *Alternative: integer cents* — avoids float drift but conflicts with the "full precision, round for display" rule and adds conversion code; floats are adequate at this scale.

**Markup lives only in the rate card.** Rate-card values are stored already marked up, so the calculator has no markup concept. This keeps totals stable ("prices are set") and removes margin logic. Trade-off: the underlying cost and margin are not visible in the app.

**Rate card as a typed TS module (`src/rate-card.ts`).** Typed, tree-checked and importable by tests. *Alternative: JSON file* — editable without a build, but needs a loader and runtime validation; not worth it for v1.

**Day and room math.** `days = nights + 1` for meals and per-day venue; `rooms = ceil(attendees / occupancy)` for per-room lodging.

**Validation returns a discriminated result.** `validate(input)` returns `{ ok: true } | { ok: false, errors: Record<field, message> }`; the UI only calls the calculator when `ok`. Keeping validation separate from calculation means the calculator can assume valid input.

**Activities model.** `{ id, name, price, perPerson }`. The catalog comes from the rate card; the planner's selection is a list of activity ids plus custom activities of the same shape.

**Brand tokens as CSS custom properties** in one `src/styles/tokens.css` (`--rb-color-*`, `--rb-font-*`, `--rb-space-*`, `--rb-radius-*`), with placeholder values. Component CSS references only tokens. When the brand guide arrives, only this file changes.

**Tooling.** TypeScript strict, Vitest for `tests/`, ESLint + Prettier. Scripts: `dev`, `build` (type-check + build), `test`, `lint`.

## Risks / Trade-offs

- [Placeholder rate-card prices could be mistaken for real quotes] → Name them as placeholders in the file and show no claim of accuracy in the UI copy.
- [Assumptions about venue/transport/staff/misc being unmarked planner entries, and contingency being client-visible, are unconfirmed] → Recorded in the proposal; each is isolated in the calculator so changing it is a small edit.
- [Float rounding differences between line items and total] → Compute totals from unrounded values and round only at display; share percentages derive from the same unrounded numbers.
- [Hidden cost/margin] → Accepted by design; if needed later, add cost alongside price on the rate card.
