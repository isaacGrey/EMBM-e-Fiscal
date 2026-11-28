using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegulatorReadAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegulatorGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RdoCode = table.Column<string>(type: "text", nullable: true),
                    Categories = table.Column<string[]>(type: "text[]", nullable: false),
                    CanExport = table.Column<bool>(type: "boolean", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatorGrants", x => x.Id);
                    table.CheckConstraint("CK_RegulatorGrant_Period", " \"ExpiresAt\" > \"StartsAt\" ");
                    table.CheckConstraint("CK_RegulatorGrant_Scope", "(\"Scope\" = 'Company' AND \"SubscriberId\" IS NOT NULL AND \"CompanyId\" IS NOT NULL AND \"RdoCode\" IS NULL) OR\n(\"Scope\" = 'Rdo' AND \"SubscriberId\" IS NULL AND \"CompanyId\" IS NULL AND \"BranchId\" IS NULL AND \"RdoCode\" IS NOT NULL) OR\n(\"Scope\" = 'Nationwide' AND \"SubscriberId\" IS NULL AND \"CompanyId\" IS NULL AND \"BranchId\" IS NULL AND \"RdoCode\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_RegulatorGrants_Branches_SubscriberId_CompanyId_BranchId",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegulatorGrants_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegulatorGrants_RdoReferences_RdoCode",
                        column: x => x.RdoCode,
                        principalTable: "RdoReferences",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegulatorGrants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegulatorReadAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RdoCode = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    Through = table.Column<DateOnly>(type: "date", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Export = table.Column<bool>(type: "boolean", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatorReadAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatorReadAudits_RegulatorGrants_GrantId",
                        column: x => x.GrantId,
                        principalTable: "RegulatorGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatorGrants_RdoCode",
                table: "RegulatorGrants",
                column: "RdoCode");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatorGrants_SubscriberId_CompanyId_BranchId",
                table: "RegulatorGrants",
                columns: new[] { "SubscriberId", "CompanyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatorGrants_UserId_ExpiresAt",
                table: "RegulatorGrants",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatorReadAudits_GrantId",
                table: "RegulatorReadAudits",
                column: "GrantId");
            migrationBuilder.Sql("""
                CREATE TRIGGER regulator_read_audit_immutable BEFORE UPDATE OR DELETE ON "RegulatorReadAudits"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();

                CREATE FUNCTION regulator_grant_revocation_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF OLD."RevokedAt" IS NULL AND NEW."RevokedAt" IS NOT NULL
                            AND (to_jsonb(NEW) - 'RevokedAt') = (to_jsonb(OLD) - 'RevokedAt') THEN
                            RETURN NEW;
                        END IF;
                    END IF;
                    RAISE EXCEPTION 'Regulator grants may only be revoked';
                END
                $$;
                CREATE TRIGGER regulator_grant_revocation_only BEFORE UPDATE OR DELETE ON "RegulatorGrants"
                    FOR EACH ROW EXECUTE FUNCTION regulator_grant_revocation_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegulatorReadAudits");

            migrationBuilder.DropTable(
                name: "RegulatorGrants");

            migrationBuilder.Sql("DROP FUNCTION regulator_grant_revocation_only()");
        }
    }
}
