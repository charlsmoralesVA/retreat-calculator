# Retreat Budget Calculator — Project Overview

## Purpose

The Retreat Budget Calculator helps **Retreat Builders** plan and price retreats quickly and consistently. Given a handful of planning inputs (group size, length of stay, venue, food, activities, travel), it produces an itemized cost breakdown, a total budget, and a per-person price. That makes it easier to quote clients, compare scenarios, and spot cost drivers before committing to vendors.

## Pricing model

Prices for **lodging, meals, and sample activities** come from a fixed **rate card** (`src/rate-card.ts`). Every rate-card price is a client price that **already includes Retreat Builders' markup**, so there is no markup input and no separate margin step: the total shown is the client price, and it does not change unless the rate card does. The values shipped today are placeholders until real prices are supplied.

Venue, transportation, staff fees, and miscellaneous are amounts the planner enters directly, with no markup added.

## Scope

### In scope (v1)

- A single-page web app that runs locally on `localhost`.
- Entering retreat parameters through a form, with prices taken from the rate card.
- Real-time calculation of category subtotals, contingency, total cost, and cost per attendee.
- Optional sample activities as add-ons, plus custom activities.
- Input validation with clear error messages, for example no negative numbers and at least one attendee.
- Placeholder Retreat Builders brand tokens (CSS custom properties) in `src/styles/tokens.css`, to be replaced when the brand guide is available.
- Unit tests for the calculation logic.

### Out of scope (v1)

- User accounts, authentication, or multi-user collaboration.
- Persisting budgets to a database or the cloud. Saving locally to the browser is a possible stretch goal.
- Live vendor pricing, booking, or payment integrations.
- Currency conversion. The app uses USD only.
- Editing the rate card from the UI.
- Production hosting or deployment.

## Inputs

| Input | Type | Description |
| --- | --- | --- |
| Retreat name | `string` | Label for the scenario (optional). |
| Number of attendees | `integer ≥ 1` | Headcount used for per-person costs. |
| Number of nights | `integer ≥ 1` | Length of stay. Days are calculated as nights + 1. |
| Lodging mode | `"perPerson" \| "perRoom"` | How lodging is priced. The nightly rate comes from the rate card. |
| Occupancy per room | `integer ≥ 1` | Used only when the mode is `perRoom`. Rooms = attendees ÷ occupancy, rounded up. |
| Venue / meeting space | `number ≥ 0` plus `"flat" \| "perDay"` | Flat fee, or a per-day rate charged for nights + 1 days. |
| Transportation | `number ≥ 0` plus `"flat" \| "perPerson"` | Flat amount or per person (flights, shuttles). |
| Activities | selection from the rate card, plus custom `{ name, price, perPerson }` | Excursions, workshops, facilitators. |
| Staff / facilitator fees | `number ≥ 0` | Flat fees for the Retreat Builders team and contractors. |
| Miscellaneous | `number ≥ 0` | Supplies, swag, insurance, and similar costs. |
| Contingency % | `number 0–100` | Buffer applied to the subtotal. The default is 10%. |

Meals are not entered: they are the rate-card price per person per day, charged for every attendee for nights + 1 days.

## Outputs

- **Itemized breakdown**: a subtotal for each category (lodging, venue, meals, activities, transportation, staff, miscellaneous).
- **Subtotal**: the sum of all categories.
- **Contingency amount**: subtotal × contingency %.
- **Total cost**: subtotal + contingency. This is the client price.
- **Cost per attendee**: total ÷ attendees.
- **Category share**: each category's percentage of the subtotal, which shows the main cost drivers.

All money values are rounded to two decimal places only for display. Calculations keep full precision.

## Tech Stack

- **Language**: TypeScript in strict mode.
- **Runtime / tooling**: Node.js (LTS) with npm.
- **Frontend**: Vite + vanilla TypeScript, with no UI framework.
- **Testing**: Vitest for unit tests of the calculation module.
- **Linting / formatting**: ESLint + Prettier.

## Architecture

```
src/
  calculator/       # Pure, framework-free budget logic
    types.ts        # BudgetInput, BudgetResult, LineItem, RateCard types
    calculate.ts    # calculateBudget(input, rateCard): BudgetResult
    validate.ts     # validate(input): field-level errors
  rate-card.ts      # Placeholder client prices (markup included)
  ui/               # Form, results table, formatting
  styles/
    tokens.css      # Placeholder brand tokens (--rb-*)
    app.css         # Layout and components, using tokens only
  main.ts           # App entry point
tests/
  calculate.test.ts
  validate.test.ts
  rate-card.test.ts
docs/
  PROJECT_OVERVIEW.md
```

The calculation logic is kept as pure functions, separate from the UI, so it is easy to test and reuse later in an API or CLI. The rate card is passed in as an argument rather than imported, so tests and future price updates can supply their own.

## Running Locally

The app is served on `localhost` during development:

```bash
npm install
npm run dev      # starts the dev server at http://localhost:5173
npm test         # runs unit tests
npm run lint     # ESLint
npm run build    # type-checks and builds for production
```

## Success Criteria

- A planner can enter retreat details and see an accurate total and per-person cost in under a minute.
- The calculation module has unit tests that cover typical scenarios and edge cases, such as a single attendee, zero-cost categories, and per-room lodging rounding.
- The app runs locally with a single `npm run dev` command.
