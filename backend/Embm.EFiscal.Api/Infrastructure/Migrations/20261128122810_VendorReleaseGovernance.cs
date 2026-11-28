using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VendorReleaseGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectorVersionRevocations",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorVersionRevocations", x => x.VersionId);
                    table.ForeignKey(
                        name: "FK_ConnectorVersionRevocations_ConnectorVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "ConnectorVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConnectorVersionRevocations_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorVersionRevocations_ActorId",
                table: "ConnectorVersionRevocations",
                column: "ActorId");
            migrationBuilder.Sql("""
                CREATE TRIGGER connector_version_revocation_immutable BEFORE UPDATE OR DELETE ON "ConnectorVersionRevocations"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();

                INSERT INTO "VendorConnectorGrants" ("UserId", "Permission")
                SELECT "UserId", 'Connectors.Approve' FROM "VendorConnectorGrants"
                WHERE "Permission" = 'Connectors.Publish'
                ON CONFLICT DO NOTHING;

                INSERT INTO "VendorConnectorGrants" ("UserId", "Permission")
                SELECT DISTINCT u."Id", p.permission
                FROM "Users" u
                JOIN "SecurityAudits" a ON a."ActorId" = u."Id" AND a."Action" = 'LocalVendorBootstrapped'
                CROSS JOIN (VALUES ('Billing.Catalog.View'), ('Billing.Catalog.Publish'), ('Billing.Monitor.View'), ('Billing.Credits.Adjust')) p(permission)
                WHERE u."Kind" = 'Vendor' AND u."DevelopmentFixture"
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectorVersionRevocations");
        }
    }
}
