using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

using YeaKazi.Platform.Persistence;

#nullable disable

namespace YeaKazi.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class S2RulesAndMfaRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rules");

            migrationBuilder.CreateTable(
                name: "mfa_recovery_codes",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_mfa_recovery_codes_users_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rule_versions",
                schema: "rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    authority_source = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    applicability = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reviewer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approved_at = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_file = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule_versions", x => x.id);
                    table.CheckConstraint("ck_rule_versions_period", "effective_to is null or effective_to >= effective_from");
                });

            migrationBuilder.CreateIndex(
                name: "ix_mfa_recovery_codes_tenant_id_code_hash",
                schema: "iam",
                table: "mfa_recovery_codes",
                columns: new[] { "tenant_id", "code_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfa_recovery_codes_tenant_id_user_id",
                schema: "iam",
                table: "mfa_recovery_codes",
                columns: new[] { "tenant_id", "user_id" },
                filter: "used_at is null and revoked_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_rule_versions_jurisdiction_rule_code_effective_from",
                schema: "rules",
                table: "rule_versions",
                columns: new[] { "jurisdiction", "rule_code", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_rule_versions_jurisdiction_rule_code_version_label",
                schema: "rules",
                table: "rule_versions",
                columns: new[] { "jurisdiction", "rule_code", "version_label" },
                unique: true);

            PlatformSecurityS2.Apply(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            PlatformSecurityS2.Revert(migrationBuilder);

            migrationBuilder.DropTable(
                name: "mfa_recovery_codes",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "rule_versions",
                schema: "rules");
        }
    }
}
