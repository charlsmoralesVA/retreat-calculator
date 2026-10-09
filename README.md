# Retreat Budget Calculator

Plan a retreat budget and, once logged in, save it. The backend is a local
Supabase stack (Postgres + Auth + REST + Mailpit) running in Docker Desktop.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/), running
- [Supabase CLI](https://supabase.com/docs/guides/local-development/cli/getting-started)
  (`brew install supabase/tap/supabase`)
- Node.js 20.19+

## Local backend

All commands run from the repo root.

| Task | Command |
|---|---|
| Start the stack (first run pulls images) | `supabase start` |
| Show URLs and keys | `supabase status` |
| Print values as env vars | `supabase status -o env` |
| Stop (keeps data) | `supabase stop` |
| Rebuild DB from migrations, wiping all data | `supabase db reset` |

| Service | URL |
|---|---|
| API | http://127.0.0.1:54321 |
| Postgres | postgresql://postgres:postgres@127.0.0.1:54322/postgres |
| Studio (admin UI) | http://127.0.0.1:54323 |
| Mailpit (captured emails) | http://127.0.0.1:54324 |

### Environment

```
cp .env.example .env.local
```

Fill in `VITE_SUPABASE_ANON_KEY` with `ANON_KEY` from `supabase status -o env`.
`.env.local` is gitignored.

The auth site URL in `supabase/config.toml` points at the app's dev server
(`http://127.0.0.1:5174`); confirmation emails link back to wherever you signed up.

### Working in multiple git worktrees

Every worktree uses the same `project_id` and ports in `supabase/config.toml`,
so there is **one shared stack**. Start it once from any worktree; the others
just point at it. Do not run `supabase start` in a second worktree while it is
running - the ports are already taken and it will fail.

If you switch to a branch with different migrations, run `supabase db reset`.
If a branch truly needs an isolated database, give that worktree its own
`project_id` and ports in `config.toml`.

### Auth emails

Sign-up requires email confirmation. Emails are never sent externally; open
Mailpit at http://127.0.0.1:54324 to find the confirmation link.

## Running the app

```
npm install
cp .env.example .env.local     # then fill in the anon key (see above)
supabase start                 # if the stack is not already running
npm run dev                    # http://127.0.0.1:5174
```

The dev server uses port **5174** with `--strictPort` (5173 is commonly taken
by other Vite projects). Sign-up emails arrive in Mailpit
(http://127.0.0.1:54324); click the confirmation link, then log in.

Signed-out visitors can use the whole calculator. Logging in adds saving,
opening, renaming and deleting budgets.

## Tests

| What | Command | Needs |
|---|---|---|
| Unit and component tests | `npm test` | nothing running |
| Type-check and production build | `npm run build` | nothing running |
| Database security (RLS) tests | `supabase test db` | local stack running |
| Browser end-to-end checks | `npm run e2e` | local stack running, Google Chrome |

The end-to-end scripts (`e2e/`) start the dev server themselves, create
throwaway `e2e-*@example.com` users, and delete them afterwards.

## Where things live

- `src/lib/calculate.ts` - the cost formula (pure, unit-tested)
- `src/auth/` - sign-up, login, logout and session handling
- `src/budgets/` - saving, listing, opening, renaming and deleting budgets
- `supabase/migrations/` - the `budgets` table and its row-level security
- `supabase/tests/database/` - SQL tests proving users cannot touch each other's rows
