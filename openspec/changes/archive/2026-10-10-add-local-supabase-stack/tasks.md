# Tasks

## 1. Prerequisites

- [x] 1.1 Install Docker Desktop and verify `docker info` prints a server version
- [x] 1.2 Verify `supabase --version` works and ports 54321-54324 are free (`lsof -iTCP -sTCP:LISTEN -P -n | grep -E ':5432[1-4]'` returns nothing)

## 2. Supabase project

- [x] 2.1 Run `supabase init` and verify `supabase/config.toml` exists; set a fixed `project_id` for the repo
- [x] 2.2 Configure auth in `config.toml` (email/password enabled, confirmations on, site URL for the Vite dev server) and verify with `supabase start` that Auth boots without config errors
- [x] 2.3 Run `supabase start` and verify `supabase status` shows API, DB, Studio and Mailpit URLs

## 3. Environment contract

- [x] 3.1 Add `.env.example` with the API URL and anon key variable names and verify it contains no real secrets
- [x] 3.2 Add `.env.local` and Supabase temp files to `.gitignore` and verify with `git check-ignore .env.local`
- [x] 3.3 Copy values from `supabase status` into a local `.env.local` and verify the API URL responds with `curl` on `/auth/v1/health`

## 4. Documentation and verification

- [x] 4.1 Write a README section covering prerequisites, start, stop, status and `supabase db reset`, plus the shared-stack rule for worktrees; verify each documented command runs as written
- [x] 4.2 Verify reset: run `supabase db reset` and confirm it completes with no migration errors
- [x] 4.3 Verify email capture: create a test user via the auth API and confirm the confirmation email appears in Mailpit at port 54324

## Workflow follow-up

- Archive the change once the stack starts cleanly from a fresh clone.
