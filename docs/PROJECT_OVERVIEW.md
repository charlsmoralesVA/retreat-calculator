# Retreat Budget Calculator — Project Overview

## Purpose

The Retreat Budget Calculator helps **Retreat Builders** plan and price retreats quickly and consistently. Given a handful of planning inputs (group size, length of stay, venue, food, activities, travel), it produces an itemized cost breakdown, a total budget, and a per-person price. That makes it easier to quote clients, compare scenarios, and spot cost drivers before committing to vendors.

## Scope

### In scope (v1)

- A single-page web app that runs locally on `localhost`.
- Entering retreat parameters and cost assumptions through a form.
- Real-time calculation of category subtotals, contingency, total cost, and cost per attendee.
- Optional markup/margin so the per-person price can be quoted to clients.
- Input validation with clear error messages, for example no negative numbers and at least one attendee.
- Unit tests for the calculation logic.

### Out of scope (v1)

- User accounts, authentication, or multi-user collaboration.
- Persisting budgets to a database or the cloud. Saving locally to the browser is a possible stretch goal.
- Live vendor pricing, booking, or payment integrations.
- Currency conversion. The app assumes a single currency.
- Production hosting or deployment.

## Inputs

| Input | Type | Description |
| --- | --- | --- |
| Retreat name | `string` | Label for the scenario (optional). |
| Number of attendees | `integer ≥ 1` | Headcount used for per-person costs. |
| Number of nights | `integer ≥ 1` | Length of stay. Days are calculated as nights + 1. |
| Lodging cost per night | `number ≥ 0` | Per room or per person, depending on the selected mode. |
| Lodging mode | `"perPerson" \| "perRoom"` | How lodging is priced. |
| Occupancy per room | `integer ≥ 1` | Used only when the mode is `perRoom`. |
| Venue / meeting space | `number ≥ 0` | Flat fee or per-day rate. |
| Meals per person per day | `number ≥ 0` | Food and beverage cost. |
| Activities | `{ name, cost, perPerson: boolean }[]` | Excursions, workshops, facilitators. |
| Transportation | `number ≥ 0` | Flat amount or per person (flights, shuttles). |
| Staff / facilitator fees | `number ≥ 0` | Fees for the Retreat Builders team and contractors. |
| Miscellaneous | `number ≥ 0` | Supplies, swag, insurance, and similar costs. |
| Contingency % | `number 0–100` | Buffer applied to the subtotal. The default is 10%. |
| Markup % | `number ≥ 0` | Optional margin used to calculate the client price. |

## Outputs

- **Itemized breakdown**: a subtotal for each category (lodging, venue, meals, activities, transportation, staff, miscellaneous).
- **Subtotal**: the sum of all categories.
- **Contingency amount**: subtotal × contingency %.
- **Total cost**: subtotal + contingency.
- **Cost per attendee**: total cost ÷ attendees.
- **Client price**, when a markup is set: the total and per-person price with markup applied, plus the margin amount.
- **Category share**: each category's percentage of the total, which shows the main cost drivers.

All money values are rounded to two decimal places only for display. Calculations keep full precision.

## Tech Stack

- **Language**: TypeScript in strict mode.
- **Runtime / tooling**: Node.js (LTS) with npm.
- **Frontend**: a lightweight TypeScript web app, for example Vite + React or Vite + vanilla TS.
- **Testing**: Vitest for unit tests of the calculation module.
- **Linting / formatting**: ESLint + Prettier.

## Architecture (planned)

```
src/
  calculator/       # Pure, framework-free budget logic
    types.ts        # BudgetInput, BudgetResult, LineItem types
    calculate.ts    # calculateBudget(input): BudgetResult
    validate.ts     # Input validation helpers
  ui/               # Form, results table, summary components
  main.ts           # App entry point
tests/
  calculate.test.ts
docs/
  PROJECT_OVERVIEW.md
```

The calculation logic is kept as pure functions, separate from the UI, so it is easy to test and reuse later in an API or CLI.

## Running Locally

The app is served on `localhost` during development:

```bash
npm install
npm run dev      # starts the dev server, e.g. http://localhost:5173
npm test         # runs unit tests
npm run build    # type-checks and builds for production
```

## Success Criteria

- A planner can enter retreat details and see an accurate total and per-person cost in under a minute.
- The calculation module has unit tests that cover typical scenarios and edge cases, such as a single attendee, zero-cost categories, and per-room lodging rounding.
- The app runs locally with a single `npm run dev` command.
