# Proposal

## Why

Retreat Builders quotes retreats by hand, which makes pricing slow and inconsistent between planners. A local calculator that turns a few planning inputs into an itemized budget lets planners quote clients quickly, compare scenarios, and see which categories drive cost. See `docs/PROJECT_OVERVIEW.md`.

## What Changes

- Add a single-page web app (TypeScript + HTML, Vite, vanilla TS) served on `localhost`.
- Add a pure, framework-free calculation module that produces category line items, subtotal, contingency, total, per-attendee cost and category shares.
- Add a **rate card**: fixed client-facing prices for lodging, meals and sample activities, with Retreat Builders' markup already included. There is no markup input and no separate margin output; every figure shown is already a client price.
- Add input validation with clear messages (at least one attendee, no negative numbers, and similar).
- Add a form with real-time recalculation, an itemized results table and a summary, styled with placeholder Retreat Builders brand tokens (CSS custom properties) until the brand guide exists.
- Add Vitest unit tests for the calculator and ESLint/Prettier tooling.
- Update `docs/PROJECT_OVERVIEW.md` so its inputs/outputs match the rate-card model (remove the Markup % input and the client-price/margin outputs).

Assumptions recorded here (not yet confirmed by the user; revisit if wrong):

- Venue, transportation, staff fees and miscellaneous are planner-entered flat amounts with no markup. Venue has a `flat | perDay` mode and transportation a `flat | perPerson` mode.
- Contingency is 10% by default and is applied on top of the marked-up subtotal; it is visible in the total.
- Currency is USD only, displayed with `$`. Rate-card values are placeholders until real prices are supplied.

## Capabilities

### New Capabilities

- `budget-calculation`: Computes line items, subtotal, contingency, total, cost per attendee and category shares from validated inputs and rate-card prices.
- `rate-card`: Holds the fixed, markup-inclusive client prices for lodging, meals and the sample activity catalog, as configuration separate from calculation logic.
- `input-validation`: Checks planner inputs and reports clear, field-level errors without producing a budget for invalid input.
- `planner-interface`: The localhost single-page form and results view, including live recalculation, activity add-ons, USD formatting and placeholder brand tokens.

### Modified Capabilities

None (no specs exist yet).

## Impact

- New code: `src/calculator/`, `src/ui/`, `src/main.ts`, rate-card config, brand-token stylesheet, `tests/`.
- New tooling/dependencies: Node.js LTS, Vite, TypeScript (strict), Vitest, ESLint, Prettier.
- Modified docs: `docs/PROJECT_OVERVIEW.md`.
- Out of scope: accounts, persistence, vendor integrations, multi-currency, hosting.
