using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YeaKazi.Organization.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "org");

            migrationBuilder.CreateTable(
                name: "legal_entities",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    kra_pin = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    nssf_employer_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    base_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_entities", x => x.id);
                    table.UniqueConstraint("ak_legal_entities_tenant_id_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                    table.UniqueConstraint("ak_branches_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.ForeignKey(
                        name: "fk_branches_legal_entities_tenant_id_legal_entity_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id },
                        principalSchema: "org",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cost_centers",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_centers", x => x.id);
                    table.UniqueConstraint("ak_cost_centers_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.ForeignKey(
                        name: "fk_cost_centers_legal_entities_tenant_id_legal_entity_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id },
                        principalSchema: "org",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grades",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grades", x => x.id);
                    table.UniqueConstraint("ak_grades_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.ForeignKey(
                        name: "fk_grades_legal_entities_tenant_id_legal_entity_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id },
                        principalSchema: "org",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "holiday_calendars",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendars", x => x.id);
                    table.UniqueConstraint("ak_holiday_calendars_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.ForeignKey(
                        name: "fk_holiday_calendars_legal_entities_tenant_id_legal_entity_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id },
                        principalSchema: "org",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_schedules",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    working_days = table.Column<int[]>(type: "integer[]", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_schedules", x => x.id);
                    table.UniqueConstraint("ak_work_schedules_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.CheckConstraint("ck_work_schedules_break", "break_minutes >= 0 and break_minutes < 1440");
                    table.CheckConstraint("ck_work_schedules_days", "working_days <@ array[1,2,3,4,5,6,7] and cardinality(working_days) between 1 and 7");
                    table.ForeignKey(
                        name: "fk_work_schedules_legal_entities_tenant_id_legal_entity_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id },
                        principalSchema: "org",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                    table.UniqueConstraint("ak_departments_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.CheckConstraint("ck_departments_not_own_parent", "parent_department_id is null or parent_department_id <> id");
                    table.ForeignKey(
                        name: "fk_departments_cost_centers_tenant_id_legal_entity_id_cost_cen",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.cost_center_id },
                        principalSchema: "org",
                        principalTable: "cost_centers",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_departments_departments_tenant_id_legal_entity_id_parent_de",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.parent_department_id },
                        principalSchema: "org",
                        principalTable: "departments",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_departments_legal_entities_tenant_id_legal_entity_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id },
                        principalSchema: "org",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holidays", x => x.id);
                    table.UniqueConstraint("ak_holidays_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.ForeignKey(
                        name: "fk_holidays_holiday_calendars_tenant_id_legal_entity_id_calend",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.calendar_id },
                        principalSchema: "org",
                        principalTable: "holiday_calendars",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_holidays_legal_entities_tenant_id_legal_entity_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id },
                        principalSchema: "org",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reports_to_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                    table.UniqueConstraint("ak_positions_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.CheckConstraint("ck_positions_not_own_manager", "reports_to_position_id is null or reports_to_position_id <> id");
                    table.ForeignKey(
                        name: "fk_positions_branches_tenant_id_legal_entity_id_branch_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.branch_id },
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_positions_departments_tenant_id_legal_entity_id_department_",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.department_id },
                        principalSchema: "org",
                        principalTable: "departments",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_positions_grades_tenant_id_legal_entity_id_grade_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.grade_id },
                        principalSchema: "org",
                        principalTable: "grades",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_positions_legal_entities_tenant_id_legal_entity_id",
                        columns: x => new { x.tenant_id, x.legal_entity_id },
                        principalSchema: "org",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_positions_positions_tenant_id_legal_entity_id_reports_to_po",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.reports_to_position_id },
                        principalSchema: "org",
                        principalTable: "positions",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_legal_entity_id_code",
                schema: "org",
                table: "branches",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_centers_tenant_id_legal_entity_id_code",
                schema: "org",
                table: "cost_centers",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id_legal_entity_id_code",
                schema: "org",
                table: "departments",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id_legal_entity_id_cost_center_id",
                schema: "org",
                table: "departments",
                columns: new[] { "tenant_id", "legal_entity_id", "cost_center_id" });

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id_legal_entity_id_parent_department_id",
                schema: "org",
                table: "departments",
                columns: new[] { "tenant_id", "legal_entity_id", "parent_department_id" });

            migrationBuilder.CreateIndex(
                name: "ix_grades_tenant_id_legal_entity_id_code",
                schema: "org",
                table: "grades",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendars_tenant_id_legal_entity_id_code",
                schema: "org",
                table: "holiday_calendars",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holidays_tenant_id_calendar_id_date",
                schema: "org",
                table: "holidays",
                columns: new[] { "tenant_id", "calendar_id", "date" },
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_holidays_tenant_id_legal_entity_id_calendar_id",
                schema: "org",
                table: "holidays",
                columns: new[] { "tenant_id", "legal_entity_id", "calendar_id" });

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id_code",
                schema: "org",
                table: "legal_entities",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id_kra_pin",
                schema: "org",
                table: "legal_entities",
                columns: new[] { "tenant_id", "kra_pin" },
                unique: true,
                filter: "kra_pin is not null");

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_branch_id",
                schema: "org",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_code",
                schema: "org",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_department_id",
                schema: "org",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "department_id" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_grade_id",
                schema: "org",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "grade_id" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_reports_to_position_id",
                schema: "org",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "reports_to_position_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_schedules_tenant_id_legal_entity_id_code",
                schema: "org",
                table: "work_schedules",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true);
        
            // Row-level security from the first migration (rule 3).
            OrganizationSecurity.Apply(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holidays",
                schema: "org");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "org");

            migrationBuilder.DropTable(
                name: "work_schedules",
                schema: "org");

            migrationBuilder.DropTable(
                name: "holiday_calendars",
                schema: "org");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "org");

            migrationBuilder.DropTable(
                name: "departments",
                schema: "org");

            migrationBuilder.DropTable(
                name: "grades",
                schema: "org");

            migrationBuilder.DropTable(
                name: "cost_centers",
                schema: "org");

            migrationBuilder.DropTable(
                name: "legal_entities",
                schema: "org");
        }
    }
}
