using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProviderReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderReadinessEvents",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    ConnectorRevision = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderReadinessEvents", x => new { x.VersionId, x.Sequence });
                    table.CheckConstraint("CK_ProviderReadinessEvent_Action", "\"Sequence\" > 0 AND \"ConnectorRevision\" > 0 AND\n((\"Action\" = 'EvidenceRecorded' AND \"Evidence\" IS NOT NULL AND \"Reason\" IS NULL) OR\n (\"Action\" IN ('EvidenceReviewed', 'ChangesRequested', 'Withdrawn') AND \"Evidence\" IS NULL AND \"Reason\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_ProviderReadinessEvents_ConnectorVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "ConnectorVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProviderReadinessEvents_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderReadinessEvents_ActorId",
                table: "ProviderReadinessEvents",
                column: "ActorId");
            migrationBuilder.Sql("""
                CREATE TRIGGER provider_readiness_event_immutable BEFORE UPDATE OR DELETE ON "ProviderReadinessEvents"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderReadinessEvents");
        }
    }
}
