-- YeaKazi database roles (run once per PostgreSQL cluster as a superuser).
-- Passwords here are for LOCAL DEVELOPMENT ONLY. In other environments create the
-- roles with secrets from the secret store and never reuse these values.
--
--   yeakazi_owner  owns schemas/tables and runs migrations (not superuser, no BYPASSRLS)
--   yeakazi_app    API runtime; subject to row-level security
--   yeakazi_worker background worker runtime; outbox access only

do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'yeakazi_owner') then
    create role yeakazi_owner login password 'yeakazi_owner_dev' nosuperuser nocreaterole nobypassrls;
  end if;
  if not exists (select 1 from pg_roles where rolname = 'yeakazi_app') then
    create role yeakazi_app login password 'yeakazi_app_dev' nosuperuser nocreatedb nocreaterole nobypassrls;
  end if;
  if not exists (select 1 from pg_roles where rolname = 'yeakazi_worker') then
    create role yeakazi_worker login password 'yeakazi_worker_dev' nosuperuser nocreatedb nocreaterole nobypassrls;
  end if;
end $$;
