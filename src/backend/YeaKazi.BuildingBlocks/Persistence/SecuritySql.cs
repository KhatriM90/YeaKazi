namespace YeaKazi.BuildingBlocks.Persistence;

/// <summary>
/// SQL used by migrations to enforce tenant isolation with PostgreSQL row-level security.
/// Database roles (cluster level, created by deploy/postgres/00-roles.sql):
///   yeakazi_owner  - owns schemas and tables, runs migrations; never used by the app.
///   yeakazi_app    - API runtime; NOBYPASSRLS; sees only rows of app.tenant_id.
///   yeakazi_worker - worker runtime; may read and update the outbox only.
/// </summary>
public static class SecuritySql
{
    public const string AppRole = "yeakazi_app";
    public const string WorkerRole = "yeakazi_worker";

    public static string RequireRoles() => $"""
        do $$
        begin
          if not exists (select 1 from pg_roles where rolname = '{AppRole}') then
            raise exception 'Database role {AppRole} is missing. Run deploy/postgres/00-roles.sql first.';
          end if;
          if not exists (select 1 from pg_roles where rolname = '{WorkerRole}') then
            raise exception 'Database role {WorkerRole} is missing. Run deploy/postgres/00-roles.sql first.';
          end if;
        end $$;
        """;

    /// <summary>Enables and forces RLS so that only rows of the session tenant are visible or writable.</summary>
    public static string TenantTable(string schema, string table, string appGrants)
        => $"""
        alter table {schema}.{table} enable row level security;
        alter table {schema}.{table} force row level security;
        create policy tenant_isolation on {schema}.{table}
          using (tenant_id = platform.current_tenant_id())
          with check (tenant_id = platform.current_tenant_id());
        revoke all on {schema}.{table} from public;
        grant {appGrants} on {schema}.{table} to {AppRole};
        """;

    public static string SchemaUsage(string schema, params string[] roles)
        => $"revoke all on schema {schema} from public;\n" + string.Join("\n", roles.Select(r => $"grant usage on schema {schema} to {r};"));
}
