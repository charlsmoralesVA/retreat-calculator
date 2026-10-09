# Proposal

## Why

The retreat budget calculator needs user login and saved budgets, which requires a database and an auth service. We want these running locally via Supabase (Postgres + Auth) on Docker Desktop so development needs no cloud account and every contributor gets an identical, reproducible backend.

## What Changes

- Add a committed Supabase project (`supabase/config.toml`, `supabase/migrations/`) that boots the full local stack with `supabase start`.
- Define one shared local stack across all git worktrees of this repo: same `project_id`, default ports, one database volume.
- Provide an environment contract: `.env.example` (committed) and `.env.local` (gitignored) holding the local API URL and anon key.
- Document prerequisites (Docker Desktop, Supabase CLI) and the day-to-day commands (start, stop, reset, status).
- Configure local email/password auth with email confirmation delivered to the local Mailpit inbox.

## Capabilities

### New Capabilities
- `local-dev-backend`: A reproducible local Postgres + Auth backend that any contributor can start, reset to a known state, and connect the app to using documented configuration.

### Modified Capabilities
<!-- None: no existing specs. -->

## Impact

- New files: `supabase/` directory, `.env.example`, `.gitignore` entries, a short `README` section.
- New developer prerequisites: Docker Desktop and the Supabase CLI.
- Fixed local ports in use: 54321 (API), 54322 (Postgres), 54323 (Studio), 54324 (Mailpit).
- Unblocks `add-budget-auth-and-persistence`, which needs a running database to build against.
