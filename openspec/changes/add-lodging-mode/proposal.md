# Proposal

## Why

Venues usually quote lodging per room, not per person, but the calculator can only price lodging per person per night. Planners have to work out the room count and convert the room rate by hand, and rounding a half-empty room the wrong way understates the cost. This is the "Lodging mode / Occupancy per room" item listed as planned in `docs/PROJECT_OVERVIEW.md`.

## What Changes

- Add a **lodging mode** choice: lodging is priced **per person** (today's behavior) or **per room**.
- In per-room mode, add a **people per room** input. Rooms needed = headcount divided by people per room, **rounded up**, and lodging = rooms needed x nights x room rate.
- The lodging rate label follows the mode ("per person per night" or "per room per night"). The people-per-room input is shown only in per-room mode.
- Show **Rooms needed** as a read-only result in per-room mode, so the round-up is visible.
- Food, activities, travel, venue, contingency, markup and the client price rules are unchanged; they use the lodging total as before.
- Saved budgets store the mode and people per room. Budgets saved before this change open as per person, with no migration.
- Retreat name is **not** a new field: the saved budget's name already serves that purpose. `docs/PROJECT_OVERVIEW.md` is updated to say so and to move lodging mode out of "Planned".

## Capabilities

### New Capabilities
<!-- None: lodging mode extends the existing calculation capability. -->

### Modified Capabilities
- `retreat-budget-calculation`: the accepted inputs gain a lodging mode and people per room (modified requirement), the lodging line of the totals formula depends on the mode (modified requirement), and a new requirement defines per-room pricing, round-up, validation and the Rooms needed result.
- `budget-persistence`: new requirements state that the mode and people per room are saved and restored, and that budgets saved before lodging mode existed open as per person.

## Impact

- Code: `src/lib/calculate.ts` (first non-numeric input, new result field), `src/components/Calculator.tsx` (mode control, conditional input, label, Rooms needed row), `src/budgets/api.ts` (reading the mode from stored JSON), and their tests. Additive stored shape; no database migration.
- Docs: `docs/PROJECT_OVERVIEW.md`.
- No new dependencies, no database or auth changes.
- Out of scope: mixed room types (singles and doubles), single-occupancy supplements, a separate retreat-name field, and changing how food, activities or travel are priced.
