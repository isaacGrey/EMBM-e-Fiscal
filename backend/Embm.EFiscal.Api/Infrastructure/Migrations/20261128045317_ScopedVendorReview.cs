using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScopedVendorReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScanState",
                table: "RegulatoryAuthorizationVersions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VendorReviewCase",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorReviewCase", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorReviewCase_RegulatoryAuthorizations_SubscriberId_Comp~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId },
                        principalTable: "RegulatoryAuthorizations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorReviewAssignment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reviewer = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorReviewAssignment", x => x.Id);
                    table.CheckConstraint("CK_VendorReviewAssignment_Time", " \"StartsAt\" < \"ExpiresAt\" ");
                    table.ForeignKey(
                        name: "FK_VendorReviewAssignment_Users_Reviewer",
                        column: x => x.Reviewer,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorReviewAssignment_VendorReviewCase_CaseId",
                        column: x => x.CaseId,
                        principalTable: "VendorReviewCase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorReviewRevocation",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorReviewRevocation", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_VendorReviewRevocation_VendorReviewAssignment_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "VendorReviewAssignment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorReviewAssignment_CaseId_Reviewer_ExpiresAt",
                table: "VendorReviewAssignment",
                columns: new[] { "CaseId", "Reviewer", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorReviewAssignment_Reviewer",
                table: "VendorReviewAssignment",
                column: "Reviewer");

            migrationBuilder.CreateIndex(
                name: "IX_VendorReviewCase_SubscriberId_CompanyId_AuthorizationId",
                table: "VendorReviewCase",
                columns: new[] { "SubscriberId", "CompanyId", "AuthorizationId" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE TRIGGER vendor_review_case_immutable BEFORE UPDATE OR DELETE ON "VendorReviewCase"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                CREATE TRIGGER vendor_review_assignment_immutable BEFORE UPDATE OR DELETE ON "VendorReviewAssignment"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                CREATE TRIGGER vendor_review_revocation_immutable BEFORE UPDATE OR DELETE ON "VendorReviewRevocation"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VendorReviewRevocation");

            migrationBuilder.DropTable(
                name: "VendorReviewAssignment");

            migrationBuilder.DropTable(
                name: "VendorReviewCase");

            migrationBuilder.DropColumn(
                name: "ScanState",
                table: "RegulatoryAuthorizationVersions");
        }
    }
}
