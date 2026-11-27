using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MappingExecutionAndAccountingSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_DocumentOutboxMessages_SubscriberId_CompanyId_Id",
                table: "DocumentOutboxMessages",
                columns: new[] { "SubscriberId", "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "ExecutionPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capability = table.Column<string>(type: "text", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Authentication = table.Column<string>(type: "text", nullable: false),
                    TimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    PageLimit = table.Column<int>(type: "integer", nullable: false),
                    RequestSchema = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    ResponseSchema = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionPolicies_ConnectorVersions_ConnectorVersionId",
                        column: x => x.ConnectorVersionId,
                        principalTable: "ConnectorVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MappingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    ActiveVersion = table.Column<int>(type: "integer", nullable: true),
                    Disabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingProfiles", x => x.Id);
                    table.UniqueConstraint("AK_MappingProfiles_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MappingProfiles_ExternalConnections_SubscriberId_CompanyId_~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ConnectionId },
                        principalTable: "ExternalConnections",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferenceMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    LocalKey = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    ExternalCode = table.Column<string>(type: "text", nullable: false),
                    ExternalName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferenceMappings_ExternalConnections_SubscriberId_CompanyI~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ConnectionId },
                        principalTable: "ExternalConnections",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorMappingTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Rules = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    Validation = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorMappingTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorMappingTemplates_ConnectorVersions_ConnectorVersionId",
                        column: x => x.ConnectorVersionId,
                        principalTable: "ConnectorVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConnectorTestResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    Diagnostics = table.Column<JsonElement>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorTestResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectorTestResults_ExecutionPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "ExecutionPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MappingVersions",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    ConnectorVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rules = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    Validation = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    ReferenceSnapshot = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    Validated = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingVersions", x => new { x.SubscriberId, x.CompanyId, x.ProfileId, x.Number });
                    table.ForeignKey(
                        name: "FK_MappingVersions_ConnectorVersions_ConnectorVersionId",
                        column: x => x.ConnectorVersionId,
                        principalTable: "ConnectorVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MappingVersions_MappingProfiles_SubscriberId_CompanyId_Prof~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ProfileId },
                        principalTable: "MappingProfiles",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncDestinations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false),
                    Scenario = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncDestinations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncDestinations_Branches_SubscriberId_CompanyId_BranchId",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncDestinations_MappingProfiles_SubscriberId_CompanyId_Pro~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ProfileId },
                        principalTable: "MappingProfiles",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingNumber = table.Column<int>(type: "integer", nullable: false),
                    Scenario = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncOperations", x => x.Id);
                    table.UniqueConstraint("AK_SyncOperations_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_SyncOperations_DocumentOutboxMessages_SubscriberId_CompanyI~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.Id },
                        principalTable: "DocumentOutboxMessages",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncOperations_ExternalConnections_SubscriberId_CompanyId_C~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ConnectionId },
                        principalTable: "ExternalConnections",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncOperations_MappingVersions_SubscriberId_CompanyId_Profi~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ProfileId, x.MappingNumber },
                        principalTable: "MappingVersions",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "ProfileId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalDocumentIdentities",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InternalNumber = table.Column<string>(type: "text", nullable: false),
                    ExternalAccount = table.Column<string>(type: "text", nullable: false),
                    ExternalCompany = table.Column<string>(type: "text", nullable: false),
                    ExternalType = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    ExternalNumber = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ConnectorVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingNumber = table.Column<int>(type: "integer", nullable: false),
                    EvidenceHash = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalDocumentIdentities", x => new { x.SubscriberId, x.CompanyId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_ExternalDocumentIdentities_FiscalDocuments_SubscriberId_Com~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalDocumentIdentities_SyncOperations_SubscriberId_Comp~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.OperationId },
                        principalTable: "SyncOperations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MockAccountingRecords",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    ExternalType = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    ExternalNumber = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Relationship = table.Column<string>(type: "text", nullable: true),
                    Total = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockAccountingRecords", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_MockAccountingRecords_SyncOperations_SubscriberId_CompanyId~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.OperationId },
                        principalTable: "SyncOperations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    MappingNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: true),
                    Evidence = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncAttempts_SyncOperations_SubscriberId_CompanyId_Operatio~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.OperationId },
                        principalTable: "SyncOperations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorTestResults_PolicyId",
                table: "ConnectorTestResults",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPolicies_ConnectorVersionId_Capability_Method_Path",
                table: "ExecutionPolicies",
                columns: new[] { "ConnectorVersionId", "Capability", "Method", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalDocumentIdentities_ConnectionId_ExternalType_Extern~",
                table: "ExternalDocumentIdentities",
                columns: new[] { "ConnectionId", "ExternalType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalDocumentIdentities_SubscriberId_CompanyId_Operation~",
                table: "ExternalDocumentIdentities",
                columns: new[] { "SubscriberId", "CompanyId", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingProfiles_SubscriberId_CompanyId_ConnectionId_Documen~",
                table: "MappingProfiles",
                columns: new[] { "SubscriberId", "CompanyId", "ConnectionId", "DocumentType", "Direction", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingVersions_ConnectorVersionId",
                table: "MappingVersions",
                column: "ConnectorVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MockAccountingRecords_ConnectionId_ExternalType_Number",
                table: "MockAccountingRecords",
                columns: new[] { "ConnectionId", "ExternalType", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockAccountingRecords_SubscriberId_CompanyId_OperationId",
                table: "MockAccountingRecords",
                columns: new[] { "SubscriberId", "CompanyId", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceMappings_SubscriberId_CompanyId_ConnectionId_Kind_~",
                table: "ReferenceMappings",
                columns: new[] { "SubscriberId", "CompanyId", "ConnectionId", "Kind", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceMappings_SubscriberId_CompanyId_ConnectionId_Kind~1",
                table: "ReferenceMappings",
                columns: new[] { "SubscriberId", "CompanyId", "ConnectionId", "Kind", "LocalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncAttempts_OperationId_Number",
                table: "SyncAttempts",
                columns: new[] { "OperationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncAttempts_SubscriberId_CompanyId_OperationId",
                table: "SyncAttempts",
                columns: new[] { "SubscriberId", "CompanyId", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncDestinations_SubscriberId_CompanyId_BranchId",
                table: "SyncDestinations",
                columns: new[] { "SubscriberId", "CompanyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncDestinations_SubscriberId_CompanyId_DocumentType_Branch~",
                table: "SyncDestinations",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentType", "BranchId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_SyncDestinations_SubscriberId_CompanyId_ProfileId",
                table: "SyncDestinations",
                columns: new[] { "SubscriberId", "CompanyId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_SubscriberId_CompanyId_ConnectionId",
                table: "SyncOperations",
                columns: new[] { "SubscriberId", "CompanyId", "ConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_SubscriberId_CompanyId_DocumentId",
                table: "SyncOperations",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_SubscriberId_CompanyId_ProfileId_MappingNumb~",
                table: "SyncOperations",
                columns: new[] { "SubscriberId", "CompanyId", "ProfileId", "MappingNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorMappingTemplates_ConnectorVersionId_Name_DocumentType~",
                table: "VendorMappingTemplates",
                columns: new[] { "ConnectorVersionId", "Name", "DocumentType", "Direction", "Version" },
                unique: true);

            migrationBuilder.Sql("""
            INSERT INTO "ConnectorVersions" ("Id","ConnectorId","Number","Revision","State","Adapter","Category","Availability","Configuration","CreatedAt","PublishedAt")
            SELECT 'f25ca108-0000-4000-8001-000000000001', "ConnectorId", 2, 1, 'Published', "Adapter","Category",'Development Verified',
                jsonb_set(jsonb_set("Configuration", '{capabilities}', "Configuration"->'capabilities' || (
                    SELECT jsonb_agg(jsonb_build_object('code',code,'verification','Development Verified'))
                    FROM (VALUES ('SalesInvoiceCreate'),('CreditMemoCreate'),('SalesOrderCreate'),('InvoiceRead'),('CreditMemoRead'),('ExternalDocumentStatusRead')) capabilities(code)
                )), '{origins}', '["https://accounting-sandbox.efiscal.example/"]'::jsonb),
                '2026-11-27T00:00:00Z','2026-11-27T00:00:00Z'
            FROM "ConnectorVersions" WHERE "Id" = 'f15ca107-0000-4000-8001-000000000011'
            ON CONFLICT ("Id") DO NOTHING;

            INSERT INTO "ExecutionPolicies" ("Id","ConnectorVersionId","Capability","Method","Origin","Path","Authentication","TimeoutMs","PageLimit","RequestSchema","ResponseSchema","ApprovedAt")
            SELECT policy_id::uuid, 'f25ca108-0000-4000-8001-000000000001', capability, method, 'https://accounting-sandbox.efiscal.example/', path,
                authentication, 2500, 3,
                CASE WHEN method = 'POST' THEN '{"type":"object","required":["header","lines"],"properties":{"header":{"type":"object"},"lines":{"type":"array","items":{"type":"object"}}}}'::jsonb END,
                '{"type":"object","required":["externalId","externalNumber","status"],"properties":{"externalId":{"type":"string"},"externalNumber":{"type":"string"},"status":{"type":"string"}}}'::jsonb,
                '2026-11-27T00:00:00Z'
            FROM (VALUES
                ('f25ca108-0000-4000-8002-000000000001','SalesInvoiceCreate','POST','/v1/accounting/documents','Synthetic Authorization'),
                ('f25ca108-0000-4000-8002-000000000002','CreditMemoCreate','POST','/v1/accounting/documents','Synthetic Authorization'),
                ('f25ca108-0000-4000-8002-000000000003','SalesOrderCreate','POST','/v1/accounting/documents','Synthetic Authorization'),
                ('f25ca108-0000-4000-8002-000000000004','ExternalDocumentStatusRead','GET','/v1/accounting/status','Bearer Token')
            ) values_list(policy_id,capability,method,path,authentication);

            CREATE FUNCTION connect_phase8_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'This integration record is immutable'; END;
            $$;
            CREATE TRIGGER protect_mapping_templates BEFORE UPDATE OR DELETE ON "VendorMappingTemplates"
                FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
            CREATE TRIGGER protect_execution_policies BEFORE UPDATE OR DELETE ON "ExecutionPolicies"
                FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
            CREATE TRIGGER protect_external_identities BEFORE UPDATE OR DELETE ON "ExternalDocumentIdentities"
                FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
            CREATE TRIGGER protect_mock_accounting_records BEFORE UPDATE OR DELETE ON "MockAccountingRecords"
                FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
            CREATE TRIGGER protect_connector_test_results BEFORE UPDATE OR DELETE ON "ConnectorTestResults"
                FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();

            CREATE FUNCTION connect_protect_mapping_version() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Mapping versions are immutable'; END IF;
                IF OLD."ActivatedAt" IS NOT NULL OR NEW."ActivatedAt" IS NULL OR
                    (to_jsonb(NEW) - 'ActivatedAt') IS DISTINCT FROM (to_jsonb(OLD) - 'ActivatedAt')
                THEN RAISE EXCEPTION 'Only first activation may change a mapping version'; END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER protect_mapping_versions BEFORE UPDATE OR DELETE ON "MappingVersions"
                FOR EACH ROW EXECUTE FUNCTION connect_protect_mapping_version();

            CREATE FUNCTION connect_protect_sync_operation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Synchronization operation history is permanent'; END IF;
                IF TG_OP = 'INSERT' AND (NOT EXISTS (
                    SELECT 1 FROM "DocumentOutboxMessages" j WHERE j."Id" = NEW."Id" AND j."DocumentId" = NEW."DocumentId" AND j."Kind" = 'Accounting'
                ) OR NOT EXISTS (
                    SELECT 1 FROM "MappingProfiles" p JOIN "ExternalConnections" c ON p."ConnectionId" = c."Id"
                    WHERE p."Id" = NEW."ProfileId" AND c."Id" = NEW."ConnectionId" AND c."ConnectorVersionId" = NEW."ConnectorVersionId"
                )) THEN RAISE EXCEPTION 'Synchronization identity or destination does not match'; END IF;
                IF TG_OP = 'UPDATE' AND ((to_jsonb(NEW)-'SubmittedAt'-'ConfirmedAt'-'MappingNumber') IS DISTINCT FROM
                    (to_jsonb(OLD)-'SubmittedAt'-'ConfirmedAt'-'MappingNumber') OR
                    (OLD."SubmittedAt" IS NOT NULL AND (NEW."SubmittedAt",NEW."MappingNumber") IS DISTINCT FROM (OLD."SubmittedAt",OLD."MappingNumber")) OR
                    (OLD."ConfirmedAt" IS NOT NULL AND NEW."ConfirmedAt" IS DISTINCT FROM OLD."ConfirmedAt"))
                THEN RAISE EXCEPTION 'The synchronization destination and submitted mapping are immutable'; END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER protect_sync_operations BEFORE INSERT OR UPDATE OR DELETE ON "SyncOperations"
                FOR EACH ROW EXECUTE FUNCTION connect_protect_sync_operation();

            CREATE FUNCTION connect_protect_sync_attempt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Synchronization attempts are permanent'; END IF;
                IF OLD."CompletedAt" IS NOT NULL OR NEW."CompletedAt" IS NULL OR
                    (to_jsonb(NEW)-'CompletedAt'-'Status'-'Category'-'Evidence') IS DISTINCT FROM
                    (to_jsonb(OLD)-'CompletedAt'-'Status'-'Category'-'Evidence')
                THEN RAISE EXCEPTION 'Only result recording may finalize an attempt'; END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER protect_sync_attempts BEFORE UPDATE OR DELETE ON "SyncAttempts"
                FOR EACH ROW EXECUTE FUNCTION connect_protect_sync_attempt();
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION connect_phase8_immutable() CASCADE;
                DROP FUNCTION connect_protect_mapping_version() CASCADE;
                DROP FUNCTION connect_protect_sync_operation() CASCADE;
                DROP FUNCTION connect_protect_sync_attempt() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "ConnectorTestResults");

            migrationBuilder.DropTable(
                name: "ExternalDocumentIdentities");

            migrationBuilder.DropTable(
                name: "MockAccountingRecords");

            migrationBuilder.DropTable(
                name: "ReferenceMappings");

            migrationBuilder.DropTable(
                name: "SyncAttempts");

            migrationBuilder.DropTable(
                name: "SyncDestinations");

            migrationBuilder.DropTable(
                name: "VendorMappingTemplates");

            migrationBuilder.DropTable(
                name: "ExecutionPolicies");

            migrationBuilder.DropTable(
                name: "SyncOperations");

            migrationBuilder.DropTable(
                name: "MappingVersions");

            migrationBuilder.DropTable(
                name: "MappingProfiles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DocumentOutboxMessages_SubscriberId_CompanyId_Id",
                table: "DocumentOutboxMessages");
        }
    }
}
