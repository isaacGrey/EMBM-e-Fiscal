using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaxpayerPortabilityAndPreservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegalHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    Through = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalHolds_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RetentionVersions",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ProposedYears = table.Column<int>(type: "integer", nullable: true),
                    RequirementKey = table.Column<string>(type: "text", nullable: true),
                    RequirementVersion = table.Column<int>(type: "integer", nullable: true),
                    Preservation = table.Column<string>(type: "text", nullable: false),
                    MigrationEvidence = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionVersions", x => new { x.SubscriberId, x.CompanyId, x.Category, x.Version });
                    table.ForeignKey(
                        name: "FK_RetentionVersions_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxpayerExports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    Through = table.Column<DateOnly>(type: "date", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DocumentCount = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Archive = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxpayerExports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxpayerExports_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegalHoldReleases",
                columns: table => new
                {
                    HoldId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalHoldReleases", x => x.HoldId);
                    table.ForeignKey(
                        name: "FK_LegalHoldReleases_LegalHolds_HoldId",
                        column: x => x.HoldId,
                        principalTable: "LegalHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalHolds_SubscriberId_CompanyId_CreatedAt",
                table: "LegalHolds",
                columns: new[] { "SubscriberId", "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxpayerExports_SubscriberId_CompanyId_GeneratedAt",
                table: "TaxpayerExports",
                columns: new[] { "SubscriberId", "CompanyId", "GeneratedAt" });
            migrationBuilder.Sql("""
                CREATE TRIGGER taxpayer_export_immutable BEFORE UPDATE OR DELETE ON "TaxpayerExports"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE TRIGGER retention_policy_immutable BEFORE UPDATE OR DELETE ON "RetentionVersions"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE TRIGGER legal_hold_immutable BEFORE UPDATE OR DELETE ON "LegalHolds"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE TRIGGER legal_hold_release_immutable BEFORE UPDATE OR DELETE ON "LegalHoldReleases"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegalHoldReleases");

            migrationBuilder.DropTable(
                name: "RetentionVersions");

            migrationBuilder.DropTable(
                name: "TaxpayerExports");

            migrationBuilder.DropTable(
                name: "LegalHolds");
        }
    }
}
