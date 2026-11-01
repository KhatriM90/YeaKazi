using Microsoft.EntityFrameworkCore.Migrations;
using YeaKazi.BuildingBlocks.Persistence;

namespace YeaKazi.Platform.Persistence;

/// <summary>S2 additions: MFA recovery codes (tenant RLS) and the product-wide rule-version store.</summary>
public static class PlatformSecurityS2
{
    public static void Apply(MigrationBuilder m)
    {
        m.Sql(SecuritySql.RequireRoles());
        m.Sql(SecuritySql.TenantTable("iam", "mfa_recovery_codes", "select, insert, update"));

        // Rule versions are product data (no tenant_id): readable by the API, writable only by the owner (rule loader).
        m.Sql($"""
            {SecuritySql.SchemaUsage("rules", SecuritySql.AppRole)}
            revoke all on rules.rule_versions from public;
            grant select on rules.rule_versions to {SecuritySql.AppRole};

            create or replace function rules.prevent_mutation() returns trigger language plpgsql as $$
            begin
              raise exception 'Loaded rule versions are immutable; publish a new version' using errcode = '42501';
            end $$;
            create trigger rule_versions_immutable before update or delete on rules.rule_versions
              for each row execute function rules.prevent_mutation();
            create trigger rule_versions_no_truncate before truncate on rules.rule_versions
              for each statement execute function rules.prevent_mutation();
            """);
    }

    public static void Revert(MigrationBuilder m)
        => m.Sql("drop function if exists rules.prevent_mutation() cascade;");
}
