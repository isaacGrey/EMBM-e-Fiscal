using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompanyMasterDataAndFinancialRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MasterImportOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Rows = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterImportOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MasterImportOperations_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MasterRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterRecords", x => x.Id);
                    table.UniqueConstraint("AK_MasterRecords_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.CheckConstraint("CK_MasterRecord_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_MasterRecords_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalMasterReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Connector = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExternalAccount = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExternalCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SourceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalMasterReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalMasterReferences_MasterRecords_SubscriberId_Company~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.RecordId },
                        principalTable: "MasterRecords",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MasterLinks",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Field = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterLinks", x => new { x.SubscriberId, x.CompanyId, x.RecordId, x.Field, x.TargetId });
                    table.ForeignKey(
                        name: "FK_MasterLinks_MasterRecords_SubscriberId_CompanyId_RecordId",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.RecordId },
                        principalTable: "MasterRecords",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MasterLinks_MasterRecords_SubscriberId_CompanyId_TargetId",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.TargetId },
                        principalTable: "MasterRecords",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MasterRevisions",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    Data = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterRevisions", x => new { x.SubscriberId, x.CompanyId, x.RecordId, x.Revision });
                    table.CheckConstraint("CK_MasterRevision_Dates", "\"EffectiveThrough\" IS NULL OR \"EffectiveThrough\" >= \"EffectiveFrom\"");
                    table.ForeignKey(
                        name: "FK_MasterRevisions_MasterRecords_SubscriberId_CompanyId_Record~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.RecordId },
                        principalTable: "MasterRecords",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PermissionDefinitions",
                column: "Id",
                values: new object[]
                {
                    "Customers.Import",
                    "Customers.Manage",
                    "Customers.View",
                    "CustomFields.Manage",
                    "DiscountGroups.Manage",
                    "DiscountGroups.View",
                    "FinancialConfiguration.Manage",
                    "Items.Import",
                    "Items.Manage",
                    "Items.View",
                    "MasterData.Calculate",
                    "MasterData.Export",
                    "MasterData.View",
                    "PriceLists.Manage",
                    "PriceLists.View",
                    "TaxGroups.Manage",
                    "TaxGroups.View"
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { "Customers.Import", "accountant" },
                    { "Customers.Manage", "accountant" },
                    { "Customers.View", "accountant" },
                    { "CustomFields.Manage", "accountant" },
                    { "DiscountGroups.Manage", "accountant" },
                    { "DiscountGroups.View", "accountant" },
                    { "FinancialConfiguration.Manage", "accountant" },
                    { "Items.Import", "accountant" },
                    { "Items.Manage", "accountant" },
                    { "Items.View", "accountant" },
                    { "MasterData.Calculate", "accountant" },
                    { "MasterData.Export", "accountant" },
                    { "MasterData.View", "accountant" },
                    { "PriceLists.Manage", "accountant" },
                    { "PriceLists.View", "accountant" },
                    { "TaxGroups.Manage", "accountant" },
                    { "TaxGroups.View", "accountant" },
                    { "Customers.View", "auditor" },
                    { "DiscountGroups.View", "auditor" },
                    { "Items.View", "auditor" },
                    { "MasterData.View", "auditor" },
                    { "PriceLists.View", "auditor" },
                    { "TaxGroups.View", "auditor" },
                    { "Customers.Import", "company-admin" },
                    { "Customers.Manage", "company-admin" },
                    { "Customers.View", "company-admin" },
                    { "CustomFields.Manage", "company-admin" },
                    { "DiscountGroups.Manage", "company-admin" },
                    { "DiscountGroups.View", "company-admin" },
                    { "FinancialConfiguration.Manage", "company-admin" },
                    { "Items.Import", "company-admin" },
                    { "Items.Manage", "company-admin" },
                    { "Items.View", "company-admin" },
                    { "MasterData.Calculate", "company-admin" },
                    { "MasterData.Export", "company-admin" },
                    { "MasterData.View", "company-admin" },
                    { "PriceLists.Manage", "company-admin" },
                    { "PriceLists.View", "company-admin" },
                    { "TaxGroups.Manage", "company-admin" },
                    { "TaxGroups.View", "company-admin" },
                    { "Customers.View", "encoder" },
                    { "DiscountGroups.View", "encoder" },
                    { "Items.View", "encoder" },
                    { "MasterData.Calculate", "encoder" },
                    { "MasterData.View", "encoder" },
                    { "PriceLists.View", "encoder" },
                    { "TaxGroups.View", "encoder" },
                    { "Customers.Import", "owner" },
                    { "Customers.Manage", "owner" },
                    { "Customers.View", "owner" },
                    { "CustomFields.Manage", "owner" },
                    { "DiscountGroups.Manage", "owner" },
                    { "DiscountGroups.View", "owner" },
                    { "FinancialConfiguration.Manage", "owner" },
                    { "Items.Import", "owner" },
                    { "Items.Manage", "owner" },
                    { "Items.View", "owner" },
                    { "MasterData.Calculate", "owner" },
                    { "MasterData.Export", "owner" },
                    { "MasterData.View", "owner" },
                    { "PriceLists.Manage", "owner" },
                    { "PriceLists.View", "owner" },
                    { "TaxGroups.Manage", "owner" },
                    { "TaxGroups.View", "owner" },
                    { "Customers.View", "readonly" },
                    { "DiscountGroups.View", "readonly" },
                    { "Items.View", "readonly" },
                    { "MasterData.View", "readonly" },
                    { "PriceLists.View", "readonly" },
                    { "TaxGroups.View", "readonly" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalMasterReferences_SubscriberId_CompanyId_Connector_E~",
                table: "ExternalMasterReferences",
                columns: new[] { "SubscriberId", "CompanyId", "Connector", "ExternalAccount", "RecordType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalMasterReferences_SubscriberId_CompanyId_RecordId",
                table: "ExternalMasterReferences",
                columns: new[] { "SubscriberId", "CompanyId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_MasterImportOperations_SubscriberId_CompanyId_Kind_Digest",
                table: "MasterImportOperations",
                columns: new[] { "SubscriberId", "CompanyId", "Kind", "Digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasterLinks_SubscriberId_CompanyId_TargetId",
                table: "MasterLinks",
                columns: new[] { "SubscriberId", "CompanyId", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_MasterRecords_SubscriberId_CompanyId_Kind_Active_Name",
                table: "MasterRecords",
                columns: new[] { "SubscriberId", "CompanyId", "Kind", "Active", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_MasterRecords_SubscriberId_CompanyId_Kind_Code",
                table: "MasterRecords",
                columns: new[] { "SubscriberId", "CompanyId", "Kind", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasterRevisions_SubscriberId_CompanyId_RecordId_EffectiveFr~",
                table: "MasterRevisions",
                columns: new[] { "SubscriberId", "CompanyId", "RecordId", "EffectiveFrom", "Revision" });
            migrationBuilder.Sql("""
                CREATE FUNCTION master_reject_revision_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Master data revisions are append-only';
                END;
                $$;
                CREATE TRIGGER master_revision_immutable BEFORE UPDATE OR DELETE ON "MasterRevisions"
                    FOR EACH ROW EXECUTE FUNCTION master_reject_revision_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER master_revision_immutable ON \"MasterRevisions\"; DROP FUNCTION master_reject_revision_change();");
            migrationBuilder.DropTable(
                name: "ExternalMasterReferences");

            migrationBuilder.DropTable(
                name: "MasterImportOperations");

            migrationBuilder.DropTable(
                name: "MasterLinks");

            migrationBuilder.DropTable(
                name: "MasterRevisions");

            migrationBuilder.DropTable(
                name: "MasterRecords");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.Import", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.Manage", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.View", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "CustomFields.Manage", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "DiscountGroups.Manage", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "DiscountGroups.View", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "FinancialConfiguration.Manage", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.Import", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.Manage", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.View", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.Calculate", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.Export", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.View", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "PriceLists.Manage", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "PriceLists.View", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "TaxGroups.Manage", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "TaxGroups.View", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.View", "auditor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "DiscountGroups.View", "auditor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.View", "auditor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.View", "auditor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "PriceLists.View", "auditor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "TaxGroups.View", "auditor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.Import", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.Manage", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.View", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "CustomFields.Manage", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "DiscountGroups.Manage", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "DiscountGroups.View", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "FinancialConfiguration.Manage", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.Import", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.Manage", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.View", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.Calculate", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.Export", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.View", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "PriceLists.Manage", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "PriceLists.View", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "TaxGroups.Manage", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "TaxGroups.View", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.View", "encoder" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "DiscountGroups.View", "encoder" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.View", "encoder" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.Calculate", "encoder" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.View", "encoder" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "PriceLists.View", "encoder" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "TaxGroups.View", "encoder" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.Import", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.Manage", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.View", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "CustomFields.Manage", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "DiscountGroups.Manage", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "DiscountGroups.View", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "FinancialConfiguration.Manage", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.Import", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.Manage", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.View", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.Calculate", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.Export", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.View", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "PriceLists.Manage", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "PriceLists.View", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "TaxGroups.Manage", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "TaxGroups.View", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Customers.View", "readonly" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "DiscountGroups.View", "readonly" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Items.View", "readonly" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "MasterData.View", "readonly" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "PriceLists.View", "readonly" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "TaxGroups.View", "readonly" });

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Customers.Import");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Customers.Manage");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Customers.View");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "CustomFields.Manage");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "DiscountGroups.Manage");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "DiscountGroups.View");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "FinancialConfiguration.Manage");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Items.Import");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Items.Manage");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Items.View");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "MasterData.Calculate");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "MasterData.Export");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "MasterData.View");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "PriceLists.Manage");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "PriceLists.View");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "TaxGroups.Manage");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "TaxGroups.View");
        }
    }
}
