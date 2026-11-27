using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReportingPreparationGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportingPreparations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportingPreparations", x => x.Id);
                    table.UniqueConstraint("AK_ReportingPreparations_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReportingPreparations_FiscalDocuments_SubscriberId_CompanyI~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportingSimulations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportingSimulations", x => x.Id);
                    table.CheckConstraint("CK_ReportingSimulation_Outcome", "\"Outcome\" = 'SimulationValidated'");
                    table.ForeignKey(
                        name: "FK_ReportingSimulations_ReportingPreparations_SubscriberId_Com~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.PreparationId },
                        principalTable: "ReportingPreparations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportingPreparations_SubscriberId_CompanyId_DocumentId_Sch~",
                table: "ReportingPreparations",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentId", "SchemaVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportingSimulations_SubscriberId_CompanyId_PreparationId",
                table: "ReportingSimulations",
                columns: new[] { "SubscriberId", "CompanyId", "PreparationId" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION efiscal_reporting_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Reporting evidence is immutable'; END IF;
                  IF TG_TABLE_NAME = 'ReportingPreparations' AND NOT EXISTS (
                    SELECT 1 FROM "FiscalDocuments" d WHERE d."Id" = NEW."DocumentId"
                    AND d."SubscriberId" = NEW."SubscriberId" AND d."CompanyId" = NEW."CompanyId"
                    AND d."Type" IN ('SalesInvoice', 'CreditMemo')) THEN
                    RAISE EXCEPTION 'Only committed invoices and credit memos are eligible';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER reporting_preparation_guard BEFORE INSERT OR UPDATE OR DELETE ON "ReportingPreparations"
                  FOR EACH ROW EXECUTE FUNCTION efiscal_reporting_guard();
                CREATE TRIGGER reporting_simulation_guard BEFORE UPDATE OR DELETE ON "ReportingSimulations"
                  FOR EACH ROW EXECUTE FUNCTION efiscal_reporting_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportingSimulations");

            migrationBuilder.DropTable(
                name: "ReportingPreparations");
            migrationBuilder.Sql("DROP FUNCTION efiscal_reporting_guard();");
        }
    }
}
