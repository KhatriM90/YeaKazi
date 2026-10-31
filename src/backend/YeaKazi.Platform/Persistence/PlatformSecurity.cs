using Microsoft.EntityFrameworkCore.Migrations;
using YeaKazi.BuildingBlocks.Persistence;

namespace YeaKazi.Platform.Persistence;

/// <summary>Row-level security, grants, append-only audit and hash chains for the Platform schemas.</summary>
public static class PlatformSecurity
{
    public static void Apply(MigrationBuilder m)
    {
        m.Sql(SecuritySql.RequireRoles());

        m.Sql("""
            create or replace function platform.current_tenant_id() returns uuid
              language sql stable as $$ select nullif(current_setting('app.tenant_id', true), '')::uuid $$;
            create or replace function platform.is_platform_scope() returns boolean
              language sql stable as $$ select coalesce(current_setting('app.scope', true), '') = 'platform' $$;
            """);

        m.Sql(SecuritySql.SchemaUsage("platform", SecuritySql.AppRole, SecuritySql.WorkerRole));
        m.Sql(SecuritySql.SchemaUsage("iam", SecuritySql.AppRole));
        m.Sql(SecuritySql.SchemaUsage("audit", SecuritySql.AppRole));
        m.Sql(SecuritySql.SchemaUsage("messaging", SecuritySql.AppRole, SecuritySql.WorkerRole));

        // Tenant registry: a tenant session sees its own row; platform scope sees all rows.
        // Not FORCEd so the SECURITY DEFINER login resolver (owner) can look up a slug.
        m.Sql($"""
            alter table platform.tenants enable row level security;
            create policy tenant_registry on platform.tenants
              using (tenant_id = platform.current_tenant_id() or platform.is_platform_scope())
              with check (platform.is_platform_scope());
            revoke all on platform.tenants from public;
            grant select, insert, update on platform.tenants to {SecuritySql.AppRole};

            create or replace function platform.resolve_active_tenant(p_slug text)
              returns table(tenant_id uuid)
              language sql stable security definer
              set search_path = pg_catalog, platform
              as $$ select t.tenant_id from platform.tenants t where t.slug = p_slug and t.status = 'active' $$;
            revoke all on function platform.resolve_active_tenant(text) from public;
            grant execute on function platform.resolve_active_tenant(text) to {SecuritySql.AppRole};
            """);

        // Platform-only tables: visible in platform scope only, never to tenant sessions.
        foreach (var (table, grants) in new[]
                 {
                     ("platform_users", "select, insert, update"),
                     ("tenant_provisioning_requests", "select, insert"),
                     ("platform_audit_events", "select, insert"),
                 })
        {
            m.Sql($"""
                alter table platform.{table} enable row level security;
                alter table platform.{table} force row level security;
                create policy platform_only on platform.{table}
                  using (platform.is_platform_scope()) with check (platform.is_platform_scope());
                revoke all on platform.{table} from public;
                grant {grants} on platform.{table} to {SecuritySql.AppRole};
                """);
        }

        m.Sql(SecuritySql.TenantTable("iam", "users", "select, insert, update"));
        m.Sql(SecuritySql.TenantTable("iam", "user_invitations", "select, insert, update"));
        m.Sql(SecuritySql.TenantTable("iam", "roles", "select, insert, update"));
        m.Sql(SecuritySql.TenantTable("iam", "role_permissions", "select, insert, delete"));
        m.Sql(SecuritySql.TenantTable("iam", "user_role_assignments", "select, insert, update"));
        m.Sql(SecuritySql.TenantTable("audit", "audit_events", "select, insert"));
        m.Sql(SecuritySql.TenantTable("messaging", "outbox_messages", "insert"));

        // Worker: dispatches outbox rows of every tenant, and nothing else.
        m.Sql($"""
            create policy outbox_worker on messaging.outbox_messages for all to {SecuritySql.WorkerRole}
              using (true) with check (true);
            grant select, update on messaging.outbox_messages to {SecuritySql.WorkerRole};
            """);

        m.Sql("""
            alter table audit.audit_events
              add constraint fk_audit_events_tenants foreign key (tenant_id) references platform.tenants (tenant_id);
            alter table messaging.outbox_messages
              add constraint fk_outbox_messages_tenants foreign key (tenant_id) references platform.tenants (tenant_id);
            """);

        // Append-only audit tables, even for the table owner.
        m.Sql("""
            create or replace function audit.prevent_mutation() returns trigger language plpgsql as $$
            begin
              raise exception 'Audit records are append-only' using errcode = '42501';
            end $$;

            create trigger audit_events_append_only before update or delete on audit.audit_events
              for each row execute function audit.prevent_mutation();
            create trigger audit_events_no_truncate before truncate on audit.audit_events
              for each statement execute function audit.prevent_mutation();
            create trigger platform_audit_events_append_only before update or delete on platform.platform_audit_events
              for each row execute function audit.prevent_mutation();
            create trigger platform_audit_events_no_truncate before truncate on platform.platform_audit_events
              for each statement execute function audit.prevent_mutation();
            """);

        // Tamper-evident SHA-256 hash chains (per tenant, and one for the platform).
        m.Sql("""
            create or replace function audit.compute_hash(p_prev text, p_seq bigint, e audit.audit_events) returns text
              language sql immutable as $$
              select encode(sha256(convert_to(concat_ws('|',
                p_prev, p_seq::text, e.id::text, e.tenant_id::text, coalesce(e.legal_entity_id::text, ''),
                e.actor_kind, coalesce(e.actor_user_id::text, ''), coalesce(e.actor_display, ''),
                coalesce(e.delegated_actor_user_id::text, ''),
                to_char(e.occurred_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US'),
                e.action, e.entity_type, coalesce(e.entity_id, ''), coalesce(e.entity_version::text, ''),
                coalesce(e.before_data::text, ''), coalesce(e.after_data::text, ''), coalesce(e.reason, ''),
                coalesce(e.correlation_id, ''), coalesce(e.client_ip, '')), 'UTF8')), 'hex')
            $$;

            create or replace function audit.chain_audit_event() returns trigger language plpgsql as $$
            declare v_prev text; v_seq bigint;
            begin
              perform pg_advisory_xact_lock(hashtextextended('audit:' || new.tenant_id::text, 0));
              select a.hash, a.seq into v_prev, v_seq from audit.audit_events a
                where a.tenant_id = new.tenant_id order by a.seq desc limit 1;
              new.seq := coalesce(v_seq, 0) + 1;
              new.prev_hash := coalesce(v_prev, repeat('0', 64));
              new.hash := audit.compute_hash(new.prev_hash, new.seq, new);
              return new;
            end $$;

            create trigger audit_events_chain before insert on audit.audit_events
              for each row execute function audit.chain_audit_event();

            create or replace function audit.verify_chain()
              returns table(valid boolean, events_checked bigint, first_broken_seq bigint)
              language plpgsql stable as $$
            declare r audit.audit_events; v_prev text := repeat('0', 64); v_expected bigint := 1; v_count bigint := 0;
            begin
              for r in select * from audit.audit_events where tenant_id = platform.current_tenant_id() order by seq loop
                v_count := v_count + 1;
                if r.seq <> v_expected or r.prev_hash <> v_prev or r.hash <> audit.compute_hash(r.prev_hash, r.seq, r) then
                  return query select false, v_count, r.seq;
                  return;
                end if;
                v_prev := r.hash;
                v_expected := v_expected + 1;
              end loop;
              return query select true, v_count, null::bigint;
            end $$;

            create or replace function audit.compute_platform_hash(p_prev text, p_seq bigint, e platform.platform_audit_events) returns text
              language sql immutable as $$
              select encode(sha256(convert_to(concat_ws('|',
                p_prev, p_seq::text, e.id::text, coalesce(e.target_tenant_id::text, ''), e.actor_kind,
                coalesce(e.actor_platform_user_id::text, ''), coalesce(e.actor_display, ''),
                to_char(e.occurred_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US'),
                e.action, e.entity_type, coalesce(e.entity_id, ''), coalesce(e.details::text, ''),
                coalesce(e.reason, ''), coalesce(e.correlation_id, ''), coalesce(e.client_ip, '')), 'UTF8')), 'hex')
            $$;

            create or replace function audit.chain_platform_audit_event() returns trigger language plpgsql as $$
            declare v_prev text; v_seq bigint;
            begin
              perform pg_advisory_xact_lock(hashtextextended('platform-audit', 0));
              select a.hash, a.seq into v_prev, v_seq from platform.platform_audit_events a order by a.seq desc limit 1;
              new.seq := coalesce(v_seq, 0) + 1;
              new.prev_hash := coalesce(v_prev, repeat('0', 64));
              new.hash := audit.compute_platform_hash(new.prev_hash, new.seq, new);
              return new;
            end $$;

            create trigger platform_audit_events_chain before insert on platform.platform_audit_events
              for each row execute function audit.chain_platform_audit_event();
            """);
    }

    public static void Revert(MigrationBuilder m)
    {
        m.Sql("""
            drop function if exists audit.verify_chain();
            drop function if exists audit.chain_audit_event() cascade;
            drop function if exists audit.chain_platform_audit_event() cascade;
            drop function if exists audit.compute_hash(text, bigint, audit.audit_events);
            drop function if exists audit.compute_platform_hash(text, bigint, platform.platform_audit_events);
            drop function if exists audit.prevent_mutation() cascade;
            drop function if exists platform.resolve_active_tenant(text);
            """);
    }
}
