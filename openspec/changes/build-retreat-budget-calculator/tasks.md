# Tasks

## 1. Project setup

- [x] 1.1 Initialize npm project with Vite + vanilla TypeScript (strict), and verify `npm run build` type-checks and builds
- [x] 1.2 Add Vitest, ESLint and Prettier with `test` and `lint` scripts, and verify `npm test` and `npm run lint` run (even if empty)
- [x] 1.3 Create the `src/calculator`, `src/ui`, `src/styles`, `tests` structure and verify `npm run dev` serves a blank page on localhost

## 2. Rate card and types

- [x] 2.1 Define `BudgetInput`, `BudgetResult`, `LineItem`, `Activity` and `RateCard` types in `src/calculator/types.ts`, and verify they compile
- [x] 2.2 Add `src/rate-card.ts` with placeholder markup-inclusive prices (lodging per person/room per night, meals per person per day, sample activities), marked as placeholders, and verify a test asserts all prices are non-negative and each activity has a name and pricing basis

## 3. Validation

- [x] 3.1 Implement `validate.ts` per the input-validation spec, and verify unit tests cover zero attendees, fractional nights, negative amounts, occupancy only in per-room mode, contingency range, multiple errors and empty name

## 4. Calculation

- [x] 4.1 Implement lodging (per person, per room with ceil rooms), meals (nights + 1 days) and venue (flat / per day), and verify tests for the spec scenarios including 7 attendees at 2 per room and a single attendee
- [x] 4.2 Implement activities, transportation, staff and miscellaneous, and verify tests for mixed per-person/flat activities, no activities and per-person transportation
- [x] 4.3 Implement subtotal, contingency (default 10%), total, cost per attendee and category shares (including zero subtotal), and verify tests for default contingency, 0%, shares summing to 100 and no divide-by-zero
- [x] 4.4 Verify full precision is kept and that a custom rate card changes the result, with tests

## 5. UI

- [x] 5.1 Add `src/styles/tokens.css` with placeholder `--rb-*` brand tokens and base styles that use only tokens, and verify no hardcoded colors or fonts remain in component CSS (grep)
- [x] 5.2 Build the planner form with labelled inputs, lodging/venue/transport modes, occupancy shown only for per-room lodging, and contingency defaulting to 10%, and verify in the browser
- [x] 5.3 Build activity add-ons (sample catalog selection plus custom activity entry), and verify selecting and adding change the activities line
- [x] 5.4 Wire live recalculation: validate, then calculate and render the itemized table, shares, subtotal, contingency, total and per-attendee cost in `$` with two decimals, and verify updates on every input change
- [x] 5.5 Show field-level errors tied to inputs and replace results with a fix-inputs prompt while invalid, and verify clearing attendees shows an error and no stale totals

## 6. Docs and integration

- [x] 6.1 Update `docs/PROJECT_OVERVIEW.md` for the rate-card model (remove Markup % input and client-price/margin outputs, document the rate card and run commands), and verify the documented commands run as written
- [x] 6.2 Run `npm run lint`, `npm test`, `npm run build` and a manual end-to-end check that a planner gets a total and per-person cost in under a minute
