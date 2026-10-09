# Design

## Context

Greenfield app; no code exists. Backend is the local Supabase stack from `add-local-supabase-stack` (Postgres, Auth, PostgREST). See proposal.md for motivation and specs for behavior.

## Goals / Non-Goals

**Goals:**
- Browser-only app talking to Supabase directly; no custom server.
- Authorization enforced in the database via row-level security.
- Calculation logic isolated as pure functions, testable without a browser or backend.

**Non-Goals:**
- Sharing/collaboration, OAuth/magic links, hosted deployment, offline mode.

## Decisions

- **Vite + React + TypeScript with `supabase-js` v2.** Small, fast dev server, no SSR needed. Alternative: Next.js; rejected as unnecessary without server rendering or custom API routes.
- **Inputs stored as JSONB in one `budgets` table.** Columns: `id uuid pk default gen_random_uuid()`, `user_id uuid not null references auth.users(id) on delete cascade default auth.uid()`, `name text not null`, `inputs jsonb not null`, `created_at`, `updated_at timestamptz`. New calculator fields then need no migration. Alternative: normalized line-item tables; better for reporting, costlier now. Revisit if cross-budget queries appear.
- **Row-level security with `user_id = auth.uid()`** for select, insert, update, delete, granted to the `authenticated` role only. `anon` gets no access. `user_id` defaults to `auth.uid()` so clients cannot spoof ownership; the insert/update policy `with check` also requires it. An `updated_at` trigger maintains last-modified time server-side.
- **Index on `(user_id, updated_at desc)`** for the list query.
- **Calculation as a pure TypeScript module** with unit tests; UI renders its output. Food is charged per day, with days = nights + 1 (arrival and departure days); this is a stated assumption in the spec formula and a single constant to change.
- **Auth state via `supabase.auth.onAuthStateChange`** in a React context; the client persists the session in localStorage.
- **Config via `VITE_SUPABASE_URL` and `VITE_SUPABASE_ANON_KEY`** from `.env.local`, per the stack change.
- **Tests:** Vitest for calculation and UI components; SQL-level RLS checks run against the local stack using two test users.

## Risks / Trade-offs

- [JSONB inputs can drift from the TypeScript shape] → Validate and default missing fields on load; version key inside `inputs`.
- [RLS misconfigured silently exposes data] → Dedicated cross-user and anonymous tests are required tasks.
- [Email confirmation friction in dev] → Mailpit link; documented in the stack change.
- [Food-days assumption may not match organizers' expectations] → Isolated constant, called out in the spec formula.
