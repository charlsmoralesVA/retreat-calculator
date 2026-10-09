# Proposal

## Why

Organizers want to log in to the retreat budget calculator and come back to their saved budgets instead of re-entering everything. Accounts and persistence turn a throwaway estimator into a tool people can refine as quotes arrive.

## What Changes

- Scaffold the web app with Vite, React and TypeScript, using `supabase-js` as the only backend client.
- Add email/password sign-up, login, logout and session persistence.
- Add a `budgets` table whose rows belong to a user, protected so users only access their own rows.
- Add a basic retreat budget calculator form whose inputs (headcount, nights, lodging, food, activities, travel, contingency) produce per-person and total costs.
- Let a signed-in user save, list, open, rename and delete budgets.
- Gate saved-budget features behind login; signed-out users can still use the calculator without saving.

## Capabilities

### New Capabilities
- `user-auth`: Account creation, login, logout and session handling for the app.
- `budget-persistence`: Saving, listing, loading, renaming and deleting a user's own budgets, with per-user data isolation.
- `retreat-budget-calculation`: Computing per-person and total retreat costs from budget inputs.

### Modified Capabilities
<!-- None: no existing specs. -->

## Impact

- New app source tree (Vite + React + TS), `package.json`, and `supabase-js` dependency.
- New migration under `supabase/migrations/` creating the `budgets` table and its row-level security policies.
- Depends on `add-local-supabase-stack` being applied first.
- Out of scope: sharing budgets between users, OAuth or magic-link login, hosted deployment, line-item normalized tables.
