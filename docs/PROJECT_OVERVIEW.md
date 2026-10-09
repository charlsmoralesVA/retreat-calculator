# Retreat Budget Calculator — Project Overview

## Purpose

The Retreat Budget Calculator helps **Retreat Builders** plan and price retreats quickly and consistently. Given a handful of planning inputs (group size, length of stay, venue, food, activities, travel), it produces a cost breakdown, a total budget, and a per-person price. That makes it easier to quote clients, compare scenarios, and spot cost drivers before committing to vendors.

Planners can log in to save their budgets and come back to them as quotes arrive. The calculator itself works without an account.

## Scope

### In scope (v1)

- A single-page web app that runs locally on `localhost`.
- Entering retreat parameters and cost assumptions through a form.
- Real-time calculation of category subtotals, contingency, total cost, and cost per attendee.
- **Client pricing**: an optional markup on the total cost produces a client price (per person and quoted total) and shows the margin.
- Input validation with clear error messages, for example no negative numbers and whole-number attendees.
- **User accounts**: email and password sign-up with email confirmation, login, logout, and sessions that survive a page reload.
- **Saved budgets**: logged-in users can save, open, rename, and delete their own budgets. Each user sees only their own budgets, enforced by the database.
- **A local backend** (Supabase: Postgres + Auth) run in Docker Desktop, so development needs no cloud account.
- Unit and component tests, database security tests, and browser end-to-end checks.

### Planned (not built yet)

- A richer input set: retreat name, per-room lodging, itemized activities, staff fees, and miscellaneous costs (see [Planned inputs and outputs](#planned-inputs-and-outputs)).
- Linting and formatting with ESLint and Prettier.

### Out of scope (v1)

- Multi-user collaboration or sharing a budget between accounts.
- Social or magic-link login (email and password only).
- Live vendor pricing, booking, or payment integrations.
- Currency conversion. The app assumes a single currency.
- Production hosting or deployment. The backend runs locally only.

## Inputs

Implemented today:

| Input | Type | Description |
| --- | --- | --- |
| Attendees | `integer ≥ 0` | Headcount used for per-person costs. |
| Nights | `number ≥ 0` | Length of stay. Food is charged for nights + 1 days. |
| Lodging per person per night | `number ≥ 0` | Lodging rate. |
| Food per person per day | `number ≥ 0` | Food and beverage cost. |
| Venue fee | `number ≥ 0` | Fixed fee. |
| Activities per person | `number ≥ 0` | Excursions, workshops, facilitators. |
| Travel per person | `number ≥ 0` | Flights, shuttles. |
| Contingency % | `number ≥ 0` | Buffer applied to the subtotal. |
| Markup % | `number ≥ 0` | Optional. Added on top of the total cost (including contingency) to get the client price. No upper limit; 0 or empty means no client price. |

An invalid field (negative, not a number, or a fractional headcount) is flagged and left out of the totals until it is corrected. With zero attendees, the per-person cost shows as N/A.

### Planned inputs and outputs

| Input | Type | Description |
| --- | --- | --- |
| Retreat name | `string` | Label for the scenario. Saved budgets already have a name. |
| Lodging mode | `"perPerson" \| "perRoom"` | How lodging is priced. |
| Occupancy per room | `integer ≥ 1` | Used only when the mode is `perRoom`. |
| Venue / meeting space | `number ≥ 0` | Flat fee or per-day rate. |
| Activities | `{ name, cost, perPerson: boolean }[]` | Itemized, replacing the single per-person amount. |
| Staff / facilitator fees | `number ≥ 0` | Fees for the Retreat Builders team and contractors. |
| Miscellaneous | `number ≥ 0` | Supplies, swag, insurance, and similar costs. |

Planned output: each category's percentage of the total, to show the main cost drivers.

## Outputs

Implemented today:

- **Category subtotals**: lodging, food, venue, activities, and travel.
- **Subtotal**: the sum of all categories.
- **Contingency amount**: subtotal × contingency %.
- **Grand total**: subtotal + contingency.
- **Cost per attendee**: grand total ÷ attendees.

All cost figures are rounded to two decimal places only for display; calculations keep full precision.

### Client price

Shown only when the markup is above 0.

- **Per person**: total cost × (1 + markup %) ÷ attendees, **rounded to the nearest cent first** (exact halves round up).
- **Quoted total**: that rounded per-person price × attendees, so a client who multiplies the per-person price by headcount gets exactly the quoted total. It can differ from the exact marked-up cost by under half a cent per attendee.
- **Margin amount**: quoted total − total cost.
- **Margin %**: margin amount ÷ quoted total.

Markup and margin are different numbers: a 25% markup is a 20% margin. The input is markup; margin is shown as a result. Markup does not change any cost figure. With zero attendees the client figures show N/A.

## Saved Budgets

Each saved budget belongs to one user and stores a name plus the calculator inputs. Inputs are stored as JSON with a version number, so new calculator fields, such as the markup, do not need a database migration. Budgets saved before the markup existed open with markup 0 and no client price. Row-level security in Postgres ensures a user can read, change, and delete only their own rows; anonymous requests get nothing. Behavior is specified in `openspec/specs/` (`user-auth`, `budget-persistence`, `retreat-budget-calculation`, `local-dev-backend`).

## Tech Stack

- **Language**: TypeScript in strict mode.
- **Runtime / tooling**: Node.js with npm.
- **Frontend**: Vite + React.
- **Backend**: Supabase (Postgres, Auth, Mailpit for local email) via the Supabase CLI, running in Docker Desktop. The browser talks to it directly with `supabase-js`; there is no custom server.
- **Testing**: Vitest and Testing Library for unit and component tests, pgTAP (`supabase test db`) for database security, Playwright (driving Chrome) for end-to-end checks.
- **Specs**: OpenSpec, with requirements under `openspec/specs/`.

## Architecture

```
src/
  lib/              # Pure calculation (calculate.ts), money formatting, Supabase client
  auth/             # Sign-up, login, logout, session context
  budgets/          # Save, list, open, rename, delete budgets
  components/       # Calculator form and live totals
  test/             # Fake backend and render helper for tests
  App.tsx           # Page layout and wiring
  main.tsx          # App entry point
supabase/
  config.toml       # Local stack configuration
  migrations/       # budgets table and row-level security
  tests/database/   # SQL tests for data isolation
e2e/                # Real-browser checks against the local stack
openspec/           # Capability specs and archived changes
docs/
  PROJECT_OVERVIEW.md
```

The calculation logic is kept as pure functions, separate from the UI, so it is easy to test and reuse later in an API or CLI. Authorization lives in the database, not in the UI.

## Running Locally

Requires Docker Desktop and the Supabase CLI. See the README for full setup.

```bash
supabase start               # local database, auth, and email inbox
npm install
cp .env.example .env.local   # fill in the anon key from `supabase status -o env`
npm run dev                  # http://127.0.0.1:5174
npm test                     # unit and component tests
supabase test db             # database security tests
npm run e2e                  # browser end-to-end checks (needs Chrome)
npm run build                # type-checks and builds for production
```

The dev server uses port 5174 because 5173 is commonly taken by other Vite projects.

## Success Criteria

- A planner can enter retreat details and see an accurate total and per-person cost in under a minute, without an account.
- A planner can sign up, log in, save a budget, and find it again after reloading the page or logging back in.
- One user can never read, change, or delete another user's budgets.
- The calculation module has unit tests that cover typical scenarios and edge cases, such as zero attendees and invalid input.
- The whole stack runs locally with `supabase start` and `npm run dev`.
