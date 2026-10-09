-- Saved retreat budgets. Each row belongs to one user; row-level security
-- ensures users can only ever see and change their own rows.

create table public.budgets (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  name text not null check (length(btrim(name)) > 0),
  inputs jsonb not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index budgets_user_id_updated_at_idx on public.budgets (user_id, updated_at desc);

create function public.set_updated_at()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

create trigger budgets_set_updated_at
before update on public.budgets
for each row execute function public.set_updated_at();

alter table public.budgets enable row level security;

-- Only signed-in users get any access; anonymous requests get nothing.
revoke all on public.budgets from anon;
grant select, insert, update, delete on public.budgets to authenticated;

create policy "budgets_select_own" on public.budgets
  for select to authenticated
  using (user_id = (select auth.uid()));

create policy "budgets_insert_own" on public.budgets
  for insert to authenticated
  with check (user_id = (select auth.uid()));

create policy "budgets_update_own" on public.budgets
  for update to authenticated
  using (user_id = (select auth.uid()))
  with check (user_id = (select auth.uid()));

create policy "budgets_delete_own" on public.budgets
  for delete to authenticated
  using (user_id = (select auth.uid()));
