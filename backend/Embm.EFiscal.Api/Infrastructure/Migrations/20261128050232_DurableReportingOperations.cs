using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DurableReportingOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportingOperation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConnectorVersion = table.Column<int>(type: "integer", nullable: true),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    Scenario = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "text", nullable: false),
                    MappingVersion = table.Column<string>(type: "text", nullable: true),
                    SourceHash = table.Column<string>(type: "text", nullable: true),
                    PayloadHash = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    Failure = table.Column<string>(type: "text", nullable: true),
                    VerifyOnly = table.Column<bool>(type: "boolean", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Lease = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportingOperation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportingOperation_FiscalDocuments_SubscriberId_CompanyId_D~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportingOperation_RegulatoryConnectorVersion_ConnectorVers~",
                        column: x => x.ConnectorVersion,
                        principalTable: "RegulatoryConnectorVersion",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportingOperationAttempt",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    ResponseHash = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportingOperationAttempt", x => new { x.OperationId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_ReportingOperationAttempt_ReportingOperation_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ReportingOperation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportingOperation_ConnectorVersion",
                table: "ReportingOperation",
                column: "ConnectorVersion");

            migrationBuilder.CreateIndex(
                name: "IX_ReportingOperation_State_AvailableAt",
                table: "ReportingOperation",
                columns: new[] { "State", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportingOperation_SubscriberId_CompanyId_DocumentId_Mode_C~",
                table: "ReportingOperation",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentId", "Mode", "ConnectorVersion" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
            migrationBuilder.Sql("""
                CREATE TRIGGER regulatory_operation_attempt_immutable BEFORE UPDATE OR DELETE ON "ReportingOperationAttempt"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportingOperationAttempt");

            migrationBuilder.DropTable(
                name: "ReportingOperation");
        }
    }
}
