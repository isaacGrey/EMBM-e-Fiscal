using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegulatoryAuthorizationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegulatoryAuthorizations",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryAuthorizations", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.CheckConstraint("CK_RegulatoryAuthorization_Document", " \"DocumentType\" IN ('SalesInvoice', 'CreditMemo') ");
                    table.CheckConstraint("CK_RegulatoryAuthorization_Kind", "\"Kind\" IN ('PermitToIssue', 'EisCertification', 'PermitToTransmit', 'CasRegistration', 'PermitToUse',\n  'AcknowledgmentCertificate', 'Other')");
                    table.ForeignKey(
                        name: "FK_RegulatoryAuthorizations_Branches_SubscriberId_CompanyId_Br~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegulatoryAuthorizations_TaxpayerProfiles_SubscriberId_Comp~",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "TaxpayerProfiles",
                        principalColumns: new[] { "SubscriberId", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryAuthorizationVersions",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Authority = table.Column<string>(type: "text", nullable: false),
                    Solution = table.Column<string>(type: "text", nullable: false),
                    SolutionVersion = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Filename = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<int>(type: "integer", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryAuthorizationVersions", x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number });
                    table.CheckConstraint("CK_RegulatoryVersion_Dates", " \"ExpiresOn\" IS NULL OR \"ExpiresOn\" >= \"EffectiveFrom\" ");
                    table.CheckConstraint("CK_RegulatoryVersion_Evidence", " \"Number\" > 0 AND \"Size\" BETWEEN 1 AND 2097152 AND length(\"Sha256\") = 64 ");
                    table.ForeignKey(
                        name: "FK_RegulatoryAuthorizationVersions_RegulatoryAuthorizations_Su~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId },
                        principalTable: "RegulatoryAuthorizations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryAuthorizationFiles",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryAuthorizationFiles", x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number });
                    table.CheckConstraint("CK_RegulatoryFile_Size", " octet_length(\"Content\") BETWEEN 1 AND 2097152 ");
                    table.ForeignKey(
                        name: "FK_RegulatoryAuthorizationFiles_RegulatoryAuthorizationVersion~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number },
                        principalTable: "RegulatoryAuthorizationVersions",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "AuthorizationId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryAuthorizationReviews",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Reviewer = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Decision = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryAuthorizationReviews", x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number, x.Sequence });
                    table.CheckConstraint("CK_RegulatoryReview_Decision", " \"Sequence\" > 0 AND \"Decision\" IN ('ReviewAccepted', 'AdditionalEvidence', 'Rejected', 'ExternalAuthorizationDocumented', 'Suspended', 'Revoked') ");
                    table.ForeignKey(
                        name: "FK_RegulatoryAuthorizationReviews_RegulatoryAuthorizationVersi~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number },
                        principalTable: "RegulatoryAuthorizationVersions",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "AuthorizationId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PermissionDefinitions",
                column: "Id",
                values: new object[]
                {
                    "Regulatory.Authorization.RecordExternal",
                    "Regulatory.Authorization.Revoke",
                    "Regulatory.Authorization.Suspend",
                    "Regulatory.Evidence.History",
                    "Regulatory.Evidence.Reject",
                    "Regulatory.Evidence.RequestMore",
                    "Regulatory.Evidence.Review",
                    "Regulatory.Evidence.Submit",
                    "Regulatory.Evidence.View"
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Name" },
                values: new object[] { "regulatory-reviewer", "Regulatory Reviewer" });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { "Regulatory.Evidence.History", "auditor" },
                    { "Regulatory.Evidence.View", "auditor" },
                    { "Regulatory.Evidence.History", "company-admin" },
                    { "Regulatory.Evidence.Submit", "company-admin" },
                    { "Regulatory.Evidence.View", "company-admin" },
                    { "Regulatory.Evidence.History", "owner" },
                    { "Regulatory.Evidence.Submit", "owner" },
                    { "Regulatory.Evidence.View", "owner" },
                    { "Branch.View", "regulatory-reviewer" },
                    { "Company.View", "regulatory-reviewer" },
                    { "Regulatory.Authorization.RecordExternal", "regulatory-reviewer" },
                    { "Regulatory.Authorization.Revoke", "regulatory-reviewer" },
                    { "Regulatory.Authorization.Suspend", "regulatory-reviewer" },
                    { "Regulatory.Evidence.History", "regulatory-reviewer" },
                    { "Regulatory.Evidence.Reject", "regulatory-reviewer" },
                    { "Regulatory.Evidence.RequestMore", "regulatory-reviewer" },
                    { "Regulatory.Evidence.Review", "regulatory-reviewer" },
                    { "Regulatory.Evidence.View", "regulatory-reviewer" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryAuthorizations_SubscriberId_CompanyId_BranchId_Ki~",
                table: "RegulatoryAuthorizations",
                columns: new[] { "SubscriberId", "CompanyId", "BranchId", "Kind", "DocumentType" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION efiscal_regulatory_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Regulatory evidence records are append-only';
                END $$;
                CREATE TRIGGER regulatory_authorization_immutable BEFORE UPDATE OR DELETE ON "RegulatoryAuthorizations"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                CREATE TRIGGER regulatory_version_immutable BEFORE UPDATE OR DELETE ON "RegulatoryAuthorizationVersions"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                CREATE TRIGGER regulatory_file_immutable BEFORE UPDATE OR DELETE ON "RegulatoryAuthorizationFiles"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                CREATE TRIGGER regulatory_review_immutable BEFORE UPDATE OR DELETE ON "RegulatoryAuthorizationReviews"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP FUNCTION efiscal_regulatory_immutable() CASCADE;""");
            migrationBuilder.DropTable(
                name: "RegulatoryAuthorizationFiles");

            migrationBuilder.DropTable(
                name: "RegulatoryAuthorizationReviews");

            migrationBuilder.DropTable(
                name: "RegulatoryAuthorizationVersions");

            migrationBuilder.DropTable(
                name: "RegulatoryAuthorizations");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.History", "auditor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.View", "auditor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.History", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.Submit", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.View", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.History", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.Submit", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.View", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Branch.View", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Company.View", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Authorization.RecordExternal", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Authorization.Revoke", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Authorization.Suspend", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.History", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.Reject", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.RequestMore", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.Review", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Regulatory.Evidence.View", "regulatory-reviewer" });

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Regulatory.Authorization.RecordExternal");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Regulatory.Authorization.Revoke");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Regulatory.Authorization.Suspend");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Regulatory.Evidence.History");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Regulatory.Evidence.Reject");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Regulatory.Evidence.RequestMore");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Regulatory.Evidence.Review");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Regulatory.Evidence.Submit");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Regulatory.Evidence.View");

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: "regulatory-reviewer");
        }
    }
}
