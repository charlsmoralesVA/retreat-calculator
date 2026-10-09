# Tasks

## 1. App scaffold

- [x] 1.1 Scaffold Vite + React + TypeScript and verify `npm run dev` serves a page (use a free port, not 5173 if occupied)
- [x] 1.2 Add `@supabase/supabase-js`, Vitest and Testing Library; verify `npm test` runs an empty suite successfully
- [x] 1.3 Add a Supabase client module reading `VITE_SUPABASE_URL` and `VITE_SUPABASE_ANON_KEY` and verify it fails loudly when either is missing

## 2. Budget calculation

- [x] 2.1 Implement the pure calculation module per the spec formula and verify unit tests cover the worked example (7,200 / 720 / 7,920 / 792), zero headcount, and invalid inputs
- [x] 2.2 Build the calculator form with live totals and verify component tests show totals updating on input change and working while signed out

## 3. Database

- [x] 3.1 Create a migration for the `budgets` table, `updated_at` trigger and `(user_id, updated_at desc)` index; verify `supabase db reset` applies it cleanly
- [x] 3.2 Add RLS policies for `authenticated` only and verify with SQL tests: user A cannot read, update or delete user B's rows, and anonymous requests get nothing
- [x] 3.3 Verify `user_id` cannot be spoofed on insert (insert with another user's id is rejected)

## 4. Authentication

- [x] 4.1 Implement auth context and sign-up, login and logout UI; verify component tests for validation messages and generic wrong-credential error
- [x] 4.2 Verify end to end against the local stack: sign up, confirm via Mailpit, log in, reload stays signed in, log out ends the session

## 5. Budget persistence

- [x] 5.1 Implement save (create and update) with a name field and the login prompt that preserves inputs when signed out; verify tests for both paths
- [x] 5.2 Implement list (newest first, empty state), open, rename and confirmed delete; verify tests including cancelled delete
- [x] 5.3 Surface save/load/delete errors without clearing inputs; verify with a simulated backend failure
- [x] 5.4 Verify end to end with two real accounts that each only sees their own saved budgets

## 6. Documentation

- [x] 6.1 Add README instructions for running the app against the local stack and running tests; verify the documented commands work on a clean checkout

## Workflow follow-up

- Apply `add-local-supabase-stack` before starting this change.
- Archive after all tasks pass and both changes are verified together.
