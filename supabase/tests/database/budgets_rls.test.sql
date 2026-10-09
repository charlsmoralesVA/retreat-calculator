begin;
create extension if not exists pgtap with schema extensions;
set search_path = public, extensions;

select plan(16);

-- Two users, created as the table owner.
insert into auth.users (id, instance_id, aud, role, email)
values
  ('aaaaaaaa-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', 'a@example.com'),
  ('bbbbbbbb-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', 'b@example.com');

-- ---------- user A ----------
set local role authenticated;
set local request.jwt.claims = '{"sub":"aaaaaaaa-0000-0000-0000-000000000001","role":"authenticated"}';

select lives_ok(
  $$insert into budgets (id, name, inputs) values ('11111111-1111-1111-1111-111111111111', 'A budget', '{"headcount":10}')$$,
  'A can insert a budget'
);
select is(
  (select user_id from budgets where id = '11111111-1111-1111-1111-111111111111'),
  'aaaaaaaa-0000-0000-0000-000000000001'::uuid,
  'user_id defaults to the signed-in user'
);
select is((select count(*)::int from budgets), 1, 'A sees their own budget');

select throws_ok(
  $$insert into budgets (name, inputs) values ('   ', '{}')$$,
  '23514', null, 'blank names are rejected'
);

-- ---------- user B ----------
set local request.jwt.claims = '{"sub":"bbbbbbbb-0000-0000-0000-000000000002","role":"authenticated"}';

select is((select count(*)::int from budgets), 0, 'B cannot read A''s budget');

select is_empty(
  $$update budgets set name = 'hacked' where id = '11111111-1111-1111-1111-111111111111' returning 1$$,
  'B cannot update A''s budget'
);
select is_empty(
  $$delete from budgets where id = '11111111-1111-1111-1111-111111111111' returning 1$$,
  'B cannot delete A''s budget'
);
select throws_ok(
  $$insert into budgets (user_id, name, inputs) values ('aaaaaaaa-0000-0000-0000-000000000001', 'spoof', '{}')$$,
  '42501', null, 'B cannot insert a row owned by A (no spoofing)'
);

-- ---------- user A again ----------
set local request.jwt.claims = '{"sub":"aaaaaaaa-0000-0000-0000-000000000001","role":"authenticated"}';

select is(
  (select name from budgets where id = '11111111-1111-1111-1111-111111111111'),
  'A budget',
  'A''s budget survived B''s update and delete attempts'
);
select throws_ok(
  $$update budgets set user_id = 'bbbbbbbb-0000-0000-0000-000000000002' where id = '11111111-1111-1111-1111-111111111111'$$,
  '42501', null, 'A cannot hand a budget to another user'
);

select lives_ok(
  $$update budgets set updated_at = '2000-01-01' where id = '11111111-1111-1111-1111-111111111111'$$,
  'A can update their budget'
);
select ok(
  (select updated_at > now() - interval '1 minute' from budgets where id = '11111111-1111-1111-1111-111111111111'),
  'updated_at is maintained by the trigger'
);

-- ---------- anonymous ----------
reset role;
set local role anon;
set local request.jwt.claims = '{"role":"anon"}';

select throws_ok($$select * from budgets$$, '42501', null, 'anonymous cannot read');
select throws_ok(
  $$insert into budgets (user_id, name, inputs) values ('aaaaaaaa-0000-0000-0000-000000000001', 'x', '{}')$$,
  '42501', null, 'anonymous cannot insert'
);
select throws_ok(
  $$update budgets set name = 'x'$$, '42501', null, 'anonymous cannot update'
);
select throws_ok(
  $$delete from budgets$$, '42501', null, 'anonymous cannot delete'
);

select * from finish();
rollback;
