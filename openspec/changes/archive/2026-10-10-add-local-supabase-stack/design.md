# Design

## Context

The repo is empty apart from OpenSpec. Docker Desktop and the Supabase CLI are installed on the developer machine (the CLI is; Docker Desktop is pending). The Supabase CLI generates and manages the Docker containers, including Postgres, so we do not author our own compose file. This repo is used from several git worktrees. See proposal.md for motivation.

## Goals / Non-Goals

**Goals:**
- One command to boot Postgres, Auth, REST, Studio and Mailpit.
- Schema defined only by committed migrations; reset gives a known state.
- A single stack shared by all worktrees.

**Non-Goals:**
- Production or hosted Supabase deployment.
- CI database setup.
- Custom docker-compose files or a bespoke backend server.
- Seed data beyond what the app change needs.

## Decisions

- **Supabase CLI over hand-written docker-compose.** The CLI tracks the correct image versions and wiring for each service. Alternative: compose file with plain Postgres plus GoTrue and PostgREST; rejected as more maintenance for the same result.
- **Shared stack via a fixed `project_id` and default ports.** The CLI names containers and the data volume after `project_id`, so identical config in every worktree resolves to the same stack. `supabase start` is run once; other worktrees just point at it. Alternative: per-worktree `project_id` and ports; rejected as the default because it multiplies containers and loses test accounts between branches, but remains the escape hatch for a branch needing an isolated database.
- **Default ports kept** (API 54321, DB 54322, Studio 54323, Mailpit 54324) so docs match upstream Supabase documentation.
- **Email/password auth with confirmations on**, delivered to Mailpit, so the real sign-up flow is exercised locally.
- **Env contract:** `.env.example` committed; `.env.local` gitignored. The local anon key is not secret, but keys are still kept out of git to avoid habit leakage when hosted keys are introduced.

## Risks / Trade-offs

- [Two branches with different migrations share one database] → Run `supabase db reset` when switching branches with schema differences; document it.
- [Port 54321-54324 already in use] → `supabase status`/`lsof` check in the setup docs; unique `project_id` and ports as the isolated alternative.
- [Docker Desktop needs admin approval to install and must be running] → Documented prerequisite with a clear failure mode.
- [Docker memory use] → Stop the stack with `supabase stop` when not developing.
