-- Local development database owned by the migration role.
select 'create database yeakazi owner yeakazi_owner'
where not exists (select 1 from pg_database where datname = 'yeakazi')\gexec
