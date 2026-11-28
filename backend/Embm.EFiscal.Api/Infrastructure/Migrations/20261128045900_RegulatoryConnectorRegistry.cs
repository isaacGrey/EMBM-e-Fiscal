using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegulatoryConnectorRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegulatoryConnectorVersion",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    ApiVersion = table.Column<string>(type: "text", nullable: false),
                    Documentation = table.Column<string>(type: "text", nullable: true),
                    Origin = table.Column<string>(type: "text", nullable: true),
                    AuthenticationAdapter = table.Column<string>(type: "text", nullable: false),
                    Specification = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    RateLimit = table.Column<int>(type: "integer", nullable: false),
                    Batch = table.Column<bool>(type: "boolean", nullable: false),
                    Actor = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryConnectorVersion", x => x.Number);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryConnectorEvent",
                columns: table => new
                {
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryConnectorEvent", x => new { x.Version, x.Sequence });
                    table.ForeignKey(
                        name: "FK_RegulatoryConnectorEvent_RegulatoryConnectorVersion_Version",
                        column: x => x.Version,
                        principalTable: "RegulatoryConnectorVersion",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryConnectorTest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Actor = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Scenario = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    ResponseHash = table.Column<string>(type: "text", nullable: false),
                    Response = table.Column<JsonElement>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryConnectorTest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatoryConnectorTest_RegulatoryConnectorVersion_Version",
                        column: x => x.Version,
                        principalTable: "RegulatoryConnectorVersion",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryConnectorTest_Version",
                table: "RegulatoryConnectorTest",
                column: "Version");
            migrationBuilder.Sql("""
                CREATE TRIGGER regulatory_connector_version_immutable BEFORE UPDATE OR DELETE ON "RegulatoryConnectorVersion"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                CREATE TRIGGER regulatory_connector_event_immutable BEFORE UPDATE OR DELETE ON "RegulatoryConnectorEvent"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                CREATE TRIGGER regulatory_connector_test_immutable BEFORE UPDATE OR DELETE ON "RegulatoryConnectorTest"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegulatoryConnectorEvent");

            migrationBuilder.DropTable(
                name: "RegulatoryConnectorTest");

            migrationBuilder.DropTable(
                name: "RegulatoryConnectorVersion");
        }
    }
}
