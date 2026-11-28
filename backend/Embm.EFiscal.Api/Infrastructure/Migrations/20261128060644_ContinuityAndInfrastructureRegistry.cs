using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContinuityAndInfrastructureRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Accreditation_Category",
                table: "AccreditationVersions");

            migrationBuilder.CreateTable(
                name: "ContinuityRecords",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Details = table.Column<JsonElement>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContinuityRecords", x => new { x.SubscriberId, x.CompanyId, x.Kind, x.Key, x.Version });
                    table.ForeignKey(
                        name: "FK_ContinuityRecords_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accreditation_Category",
                table: "AccreditationVersions",
                sql: "\"Number\" > 0 AND \"Category\" IN ('Application','Checklist','Requirement','MaterialChange','Infrastructure')");
            migrationBuilder.Sql("""
                CREATE TRIGGER continuity_record_immutable BEFORE UPDATE OR DELETE ON "ContinuityRecords"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContinuityRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accreditation_Category",
                table: "AccreditationVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accreditation_Category",
                table: "AccreditationVersions",
                sql: "\"Number\" > 0 AND \"Category\" IN ('Application','Checklist','Requirement','MaterialChange')");
        }
    }
}
