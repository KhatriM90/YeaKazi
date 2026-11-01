using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using YeaKazi.People.Domain;

#nullable disable

namespace YeaKazi.People.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "people");

            migrationBuilder.CreateTable(
                name: "bank_branches",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    branch_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    branch_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    citizenship = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    residency_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    intern_guardian_consent = table.Column<bool>(type: "boolean", nullable: false),
                    phone_mobile = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    email_work = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    email_personal = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    national_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    passport_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    work_permit_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    work_permit_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    kra_pin = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    nssf_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sha_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    helb_ref = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    disability_cert_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    disability_cert_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    bank_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    bank_branch_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    account_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    account_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    mpesa_phone = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.UniqueConstraint("ak_employees_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.CheckConstraint("ck_employees_citizenship", "citizenship in ('kenyan','non_kenyan')");
                    table.CheckConstraint("ck_employees_gender", "gender in ('female','male','intersex')");
                    table.CheckConstraint("ck_employees_identity", "national_id is not null or passport_no is not null");
                    table.CheckConstraint("ck_employees_payment_method", "payment_method is null or payment_method in ('bank','mpesa','cash')");
                    table.CheckConstraint("ck_employees_residency", "residency_status in ('resident','non_resident')");
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    error_rows = table.Column<int>(type: "integer", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by_display = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    committed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batches", x => x.id);
                    table.UniqueConstraint("ak_import_batches_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.CheckConstraint("ck_import_batches_four_eyes", "committed_by_user_id is null or committed_by_user_id is distinct from uploaded_by_user_id");
                    table.CheckConstraint("ck_import_batches_status", "status in ('validated','committed','discarded')");
                });

            migrationBuilder.CreateTable(
                name: "emergency_contacts",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    relationship = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emergency_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_emergency_contacts_employees_tenant_id_legal_entity_id_empl",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.employee_id },
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employment_episodes",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: true),
                    termination_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    termination_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    termination_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    termination_recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_episodes", x => x.id);
                    table.UniqueConstraint("ak_employment_episodes_tenant_id_legal_entity_id_id", x => new { x.tenant_id, x.legal_entity_id, x.id });
                    table.CheckConstraint("ck_episodes_dates", "termination_date is null or termination_date >= hire_date");
                    table.ForeignKey(
                        name: "fk_employment_episodes_employees_tenant_id_legal_entity_id_emp",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.employee_id },
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_detail_changes",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    bank_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    bank_branch_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    account_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    account_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    mpesa_phone = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_detail_changes", x => x.id);
                    table.CheckConstraint("ck_payment_changes_four_eyes", "status <> 'approved' or decided_by_user_id is distinct from requested_by_user_id");
                    table.CheckConstraint("ck_payment_changes_method", "payment_method in ('bank','mpesa','cash')");
                    table.CheckConstraint("ck_payment_changes_status", "status in ('pending','approved','rejected','withdrawn')");
                    table.ForeignKey(
                        name: "fk_payment_detail_changes_employees_tenant_id_legal_entity_id_",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.employee_id },
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_rows",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    errors = table.Column<List<FieldError>>(type: "jsonb", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_rows_import_batches_tenant_id_legal_entity_id_batch_",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.batch_id },
                        principalSchema: "people",
                        principalTable: "import_batches",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assignment_versions",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    episode_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    change_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    worker_category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pay_basis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    job_title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    contract_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    probation_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    corrects_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_versions", x => x.id);
                    table.CheckConstraint("ck_assignments_category", "worker_category in ('permanent','fixed_term','probation','casual','piece_rate','intern','contractor')");
                    table.CheckConstraint("ck_assignments_not_own_manager", "manager_employee_id is null or manager_employee_id <> employee_id");
                    table.CheckConstraint("ck_assignments_pay_basis", "pay_basis in ('monthly','daily','hourly','piece_rate')");
                    table.CheckConstraint("ck_assignments_status", "status in ('active','suspended')");
                    table.ForeignKey(
                        name: "fk_assignment_versions_employees_tenant_id_legal_entity_id_emp",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.employee_id },
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assignment_versions_employees_tenant_id_legal_entity_id_man",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.manager_employee_id },
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assignment_versions_employment_episodes_tenant_id_legal_ent",
                        columns: x => new { x.tenant_id, x.legal_entity_id, x.episode_id },
                        principalSchema: "people",
                        principalTable: "employment_episodes",
                        principalColumns: new[] { "tenant_id", "legal_entity_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_versions_tenant_id_employee_id_effective_from",
                schema: "people",
                table: "assignment_versions",
                columns: new[] { "tenant_id", "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_versions_tenant_id_legal_entity_id_employee_id",
                schema: "people",
                table: "assignment_versions",
                columns: new[] { "tenant_id", "legal_entity_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_versions_tenant_id_legal_entity_id_episode_id",
                schema: "people",
                table: "assignment_versions",
                columns: new[] { "tenant_id", "legal_entity_id", "episode_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_versions_tenant_id_legal_entity_id_manager_emplo",
                schema: "people",
                table: "assignment_versions",
                columns: new[] { "tenant_id", "legal_entity_id", "manager_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_versions_tenant_id_manager_employee_id",
                schema: "people",
                table: "assignment_versions",
                columns: new[] { "tenant_id", "manager_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_assignment_versions_one_per_date",
                schema: "people",
                table: "assignment_versions",
                columns: new[] { "tenant_id", "episode_id", "effective_from" },
                unique: true,
                filter: "not is_cancelled");

            migrationBuilder.CreateIndex(
                name: "ix_bank_branches_tenant_id_bank_code_branch_code",
                schema: "people",
                table: "bank_branches",
                columns: new[] { "tenant_id", "bank_code", "branch_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_emergency_contacts_tenant_id_employee_id",
                schema: "people",
                table: "emergency_contacts",
                columns: new[] { "tenant_id", "employee_id" },
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_emergency_contacts_tenant_id_legal_entity_id_employee_id",
                schema: "people",
                table: "emergency_contacts",
                columns: new[] { "tenant_id", "legal_entity_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_last_name_first_name",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_legal_entity_id_employee_number",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "legal_entity_id", "employee_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_employees_kra_pin",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "legal_entity_id", "kra_pin" },
                unique: true,
                filter: "kra_pin is not null");

            migrationBuilder.CreateIndex(
                name: "ux_employees_national_id",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "legal_entity_id", "national_id" },
                unique: true,
                filter: "national_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_employees_nssf_no",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "legal_entity_id", "nssf_no" },
                unique: true,
                filter: "nssf_no is not null");

            migrationBuilder.CreateIndex(
                name: "ux_employees_passport_no",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "legal_entity_id", "passport_no" },
                unique: true,
                filter: "passport_no is not null");

            migrationBuilder.CreateIndex(
                name: "ux_employees_sha_no",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "legal_entity_id", "sha_no" },
                unique: true,
                filter: "sha_no is not null");

            migrationBuilder.CreateIndex(
                name: "ix_employment_episodes_tenant_id_employee_id_sequence",
                schema: "people",
                table: "employment_episodes",
                columns: new[] { "tenant_id", "employee_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employment_episodes_tenant_id_legal_entity_id_employee_id",
                schema: "people",
                table: "employment_episodes",
                columns: new[] { "tenant_id", "legal_entity_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_employment_episodes_one_open",
                schema: "people",
                table: "employment_episodes",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "termination_date is null");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_tenant_id_uploaded_at",
                schema: "people",
                table: "import_batches",
                columns: new[] { "tenant_id", "uploaded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_tenant_id_batch_id_row_number",
                schema: "people",
                table: "import_rows",
                columns: new[] { "tenant_id", "batch_id", "row_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_tenant_id_legal_entity_id_batch_id",
                schema: "people",
                table: "import_rows",
                columns: new[] { "tenant_id", "legal_entity_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_detail_changes_tenant_id_legal_entity_id_employee_id",
                schema: "people",
                table: "payment_detail_changes",
                columns: new[] { "tenant_id", "legal_entity_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_detail_changes_tenant_id_status",
                schema: "people",
                table: "payment_detail_changes",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_detail_changes_one_pending",
                schema: "people",
                table: "payment_detail_changes",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status = 'pending'");
            PeopleSecurity.Apply(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            PeopleSecurity.Revert(migrationBuilder);

            migrationBuilder.DropTable(
                name: "assignment_versions",
                schema: "people");

            migrationBuilder.DropTable(
                name: "bank_branches",
                schema: "people");

            migrationBuilder.DropTable(
                name: "emergency_contacts",
                schema: "people");

            migrationBuilder.DropTable(
                name: "import_rows",
                schema: "people");

            migrationBuilder.DropTable(
                name: "payment_detail_changes",
                schema: "people");

            migrationBuilder.DropTable(
                name: "employment_episodes",
                schema: "people");

            migrationBuilder.DropTable(
                name: "import_batches",
                schema: "people");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "people");
        }
    }
}
