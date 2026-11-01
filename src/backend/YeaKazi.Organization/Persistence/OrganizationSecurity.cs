using Microsoft.EntityFrameworkCore.Migrations;
using YeaKazi.BuildingBlocks.Persistence;

namespace YeaKazi.Organization.Persistence;

/// <summary>Row-level security and grants for the Organization schema. Org records are never deleted.</summary>
public static class OrganizationSecurity
{
    public static readonly string[] Tables =
    [
        "legal_entities", "branches", "cost_centers", "grades", "departments", "positions",
        "holiday_calendars", "holidays", "work_schedules",
    ];

    public static void Apply(MigrationBuilder m)
    {
        m.Sql(SecuritySql.RequireRoles());
        m.Sql(SecuritySql.SchemaUsage("org", SecuritySql.AppRole));
        foreach (var table in Tables)
        {
            m.Sql(SecuritySql.TenantTable("org", table, "select, insert, update"));
        }

        m.Sql("""
            alter table org.legal_entities
              add constraint fk_legal_entities_tenants foreign key (tenant_id) references platform.tenants (tenant_id);
            """);
    }
}
