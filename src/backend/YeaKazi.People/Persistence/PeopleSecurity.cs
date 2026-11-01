using Microsoft.EntityFrameworkCore.Migrations;
using YeaKazi.BuildingBlocks.Persistence;

namespace YeaKazi.People.Persistence;

/// <summary>
/// Row-level security, grants and cross-schema composite foreign keys for the People schema.
/// The app role can never delete people data; history is closed or cancelled, not removed.
/// </summary>
public static class PeopleSecurity
{
    public static readonly string[] Tables =
    [
        "employees", "emergency_contacts", "employment_episodes", "assignment_versions",
        "payment_detail_changes", "bank_branches", "import_batches", "import_rows",
    ];

    public static void Apply(MigrationBuilder m)
    {
        m.Sql(SecuritySql.RequireRoles());
        m.Sql(SecuritySql.SchemaUsage("people", SecuritySql.AppRole));
        foreach (var table in Tables)
        {
            m.Sql(SecuritySql.TenantTable("people", table, "select, insert, update"));
        }

        m.Sql("""
            alter table people.employees
              add constraint fk_employees_legal_entities foreign key (tenant_id, legal_entity_id) references org.legal_entities (tenant_id, id);
            alter table people.import_batches
              add constraint fk_import_batches_legal_entities foreign key (tenant_id, legal_entity_id) references org.legal_entities (tenant_id, id);
            alter table people.bank_branches
              add constraint fk_bank_branches_tenants foreign key (tenant_id) references platform.tenants (tenant_id);
            alter table people.assignment_versions
              add constraint fk_assignments_departments foreign key (tenant_id, legal_entity_id, department_id) references org.departments (tenant_id, legal_entity_id, id),
              add constraint fk_assignments_positions foreign key (tenant_id, legal_entity_id, position_id) references org.positions (tenant_id, legal_entity_id, id),
              add constraint fk_assignments_branches foreign key (tenant_id, legal_entity_id, branch_id) references org.branches (tenant_id, legal_entity_id, id),
              add constraint fk_assignments_cost_centers foreign key (tenant_id, legal_entity_id, cost_center_id) references org.cost_centers (tenant_id, legal_entity_id, id),
              add constraint fk_assignments_grades foreign key (tenant_id, legal_entity_id, grade_id) references org.grades (tenant_id, legal_entity_id, id),
              add constraint fk_assignments_work_schedules foreign key (tenant_id, legal_entity_id, work_schedule_id) references org.work_schedules (tenant_id, legal_entity_id, id);
            """);

        // History may be closed or cancelled, never rewritten: the period and identity columns of an
        // assignment version and an episode's hire date are frozen once written.
        m.Sql("""
            create or replace function people.protect_history() returns trigger language plpgsql as $$
            begin
              if tg_table_name = 'assignment_versions' then
                if old.is_cancelled then
                  raise exception 'A cancelled assignment version cannot change' using errcode = '23514';
                end if;
                if (new.employee_id, new.episode_id, new.effective_from, new.change_type, new.status, new.worker_category, new.pay_basis,
                    new.job_title, new.contract_end_date, new.probation_end_date, new.department_id, new.position_id, new.branch_id,
                    new.cost_center_id, new.grade_id, new.work_schedule_id, new.manager_employee_id, new.created_at)
                   is distinct from
                   (old.employee_id, old.episode_id, old.effective_from, old.change_type, old.status, old.worker_category, old.pay_basis,
                    old.job_title, old.contract_end_date, old.probation_end_date, old.department_id, old.position_id, old.branch_id,
                    old.cost_center_id, old.grade_id, old.work_schedule_id, old.manager_employee_id, old.created_at) then
                  raise exception 'Assignment history cannot be overwritten; cancel and correct instead' using errcode = '23514';
                end if;
              elsif tg_table_name = 'employment_episodes' then
                if (new.employee_id, new.sequence, new.hire_date) is distinct from (old.employee_id, old.sequence, old.hire_date) then
                  raise exception 'An employment episode cannot be moved' using errcode = '23514';
                end if;
              end if;
              return new;
            end $$;

            create trigger assignment_versions_protect_history before update on people.assignment_versions
              for each row execute function people.protect_history();
            create trigger employment_episodes_protect_history before update on people.employment_episodes
              for each row execute function people.protect_history();
            """);
    }

    public static void Revert(MigrationBuilder m)
        => m.Sql("drop function if exists people.protect_history() cascade;");
}
