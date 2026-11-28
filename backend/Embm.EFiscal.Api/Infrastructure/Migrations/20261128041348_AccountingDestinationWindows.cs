using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountingDestinationWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncDestinationWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: true),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncDestinationWindows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncDestinationWindows_Branches_SubscriberId_CompanyId_Bran~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncDestinationWindows_MappingProfiles_SubscriberId_Company~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ProfileId },
                        principalTable: "MappingProfiles",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncDestinationWindows_SubscriberId_CompanyId_BranchId",
                table: "SyncDestinationWindows",
                columns: new[] { "SubscriberId", "CompanyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncDestinationWindows_SubscriberId_CompanyId_DocumentType_~",
                table: "SyncDestinationWindows",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentType", "BranchId", "Start" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncDestinationWindows_SubscriberId_CompanyId_ProfileId",
                table: "SyncDestinationWindows",
                columns: new[] { "SubscriberId", "CompanyId", "ProfileId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncDestinationWindows");
        }
    }
}
