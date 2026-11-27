using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SourceStatusHistoryAndResponsePins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResponseMappingNumber",
                table: "SyncOperations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResponseProfileId",
                table: "SyncOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SourceOrders_SubscriberId_CompanyId_Id",
                table: "SourceOrders",
                columns: new[] { "SubscriberId", "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "SourceOrderObservations",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Snapshot = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceOrderObservations", x => new { x.SubscriberId, x.CompanyId, x.SourceOrderId, x.Revision });
                    table.ForeignKey(
                        name: "FK_SourceOrderObservations_SourceOrders_SubscriberId_CompanyId~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.SourceOrderId },
                        principalTable: "SourceOrders",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_SubscriberId_CompanyId_ResponseProfileId_Res~",
                table: "SyncOperations",
                columns: new[] { "SubscriberId", "CompanyId", "ResponseProfileId", "ResponseMappingNumber" });

            migrationBuilder.AddForeignKey(
                name: "FK_SyncOperations_MappingVersions_SubscriberId_CompanyId_Respo~",
                table: "SyncOperations",
                columns: new[] { "SubscriberId", "CompanyId", "ResponseProfileId", "ResponseMappingNumber" },
                principalTable: "MappingVersions",
                principalColumns: new[] { "SubscriberId", "CompanyId", "ProfileId", "Number" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
            CREATE TRIGGER protect_source_observations BEFORE UPDATE OR DELETE ON "SourceOrderObservations"
                FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncOperations_MappingVersions_SubscriberId_CompanyId_Respo~",
                table: "SyncOperations");

            migrationBuilder.DropTable(
                name: "SourceOrderObservations");

            migrationBuilder.DropIndex(
                name: "IX_SyncOperations_SubscriberId_CompanyId_ResponseProfileId_Res~",
                table: "SyncOperations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SourceOrders_SubscriberId_CompanyId_Id",
                table: "SourceOrders");

            migrationBuilder.DropColumn(
                name: "ResponseMappingNumber",
                table: "SyncOperations");

            migrationBuilder.DropColumn(
                name: "ResponseProfileId",
                table: "SyncOperations");
        }
    }
}
