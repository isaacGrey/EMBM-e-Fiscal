using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConnectorRegistryAndCompanyConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectionCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedValue = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionCredentials", x => x.Id);
                    table.UniqueConstraint("AK_ConnectionCredentials_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConnectionCredentials_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Connectors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: false),
                    Disabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connectors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DesktopAgentRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicKeyFingerprint = table.Column<string>(type: "text", nullable: false),
                    AgentVersion = table.Column<string>(type: "text", nullable: false),
                    InstalledAdapters = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesktopAgentRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DesktopAgentRegistrations_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorConnectorGrants",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorConnectorGrants", x => new { x.UserId, x.Permission });
                    table.ForeignKey(
                        name: "FK_VendorConnectorGrants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConnectorVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Adapter = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Availability = table.Column<string>(type: "text", nullable: false),
                    Configuration = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    Specification = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    Validation = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorVersions", x => x.Id);
                    table.UniqueConstraint("AK_ConnectorVersions_ConnectorId_Id", x => new { x.ConnectorId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConnectorVersions_Connectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "Connectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConnectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ExternalAccountId = table.Column<string>(type: "text", nullable: false),
                    ExternalResourceId = table.Column<string>(type: "text", nullable: false),
                    ExternalResourceName = table.Column<string>(type: "text", nullable: false),
                    ResourceType = table.Column<string>(type: "text", nullable: false),
                    CredentialReference = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Authorization = table.Column<string>(type: "text", nullable: false),
                    MappingStatus = table.Column<string>(type: "text", nullable: false),
                    MappingVersion = table.Column<int>(type: "integer", nullable: true),
                    LastSuccessfulTest = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalConnections", x => x.Id);
                    table.UniqueConstraint("AK_ExternalConnections_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ExternalConnections_Branches_SubscriberId_CompanyId_BranchId",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalConnections_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalConnections_ConnectionCredentials_SubscriberId_Comp~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.CredentialReference },
                        principalTable: "ConnectionCredentials",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalConnections_ConnectorVersions_ConnectorId_Connector~",
                        columns: x => new { x.ConnectorId, x.ConnectorVersionId },
                        principalTable: "ConnectorVersions",
                        principalColumns: new[] { "ConnectorId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConnectionMappings",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldMetadata = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionMappings", x => new { x.SubscriberId, x.CompanyId, x.ConnectionId, x.Number });
                    table.ForeignKey(
                        name: "FK_ConnectionMappings_ExternalConnections_SubscriberId_Company~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ConnectionId },
                        principalTable: "ExternalConnections",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalOrderId = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceOrders_ExternalConnections_SubscriberId_CompanyId_Con~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ConnectionId },
                        principalTable: "ExternalConnections",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connectors_Code",
                table: "Connectors",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorVersions_ConnectorId_Number",
                table: "ConnectorVersions",
                columns: new[] { "ConnectorId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesktopAgentRegistrations_SubscriberId_CompanyId_PublicKeyF~",
                table: "DesktopAgentRegistrations",
                columns: new[] { "SubscriberId", "CompanyId", "PublicKeyFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalConnections_ConnectorId_ConnectorVersionId",
                table: "ExternalConnections",
                columns: new[] { "ConnectorId", "ConnectorVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalConnections_CredentialReference",
                table: "ExternalConnections",
                column: "CredentialReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalConnections_SubscriberId_CompanyId_BranchId",
                table: "ExternalConnections",
                columns: new[] { "SubscriberId", "CompanyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalConnections_SubscriberId_CompanyId_CredentialRefere~",
                table: "ExternalConnections",
                columns: new[] { "SubscriberId", "CompanyId", "CredentialReference" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalConnections_SubscriberId_CompanyId_ExternalAccountI~",
                table: "ExternalConnections",
                columns: new[] { "SubscriberId", "CompanyId", "ExternalAccountId", "ExternalResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceOrders_SubscriberId_CompanyId_ConnectionId_ExternalOr~",
                table: "SourceOrders",
                columns: new[] { "SubscriberId", "CompanyId", "ConnectionId", "ExternalOrderId" },
                unique: true);
            migrationBuilder.Sql("""
            INSERT INTO "Connectors" ("Id", "Code", "Disabled") VALUES
                ('f15ca107-0000-4000-8000-000000000001', 'myob-business', FALSE),
                ('f15ca107-0000-4000-8000-000000000002', 'myob-accountright-online', FALSE),
                ('f15ca107-0000-4000-8000-000000000003', 'myob-accountright-desktop', FALSE),
                ('f15ca107-0000-4000-8000-000000000004', 'qbd', FALSE),
                ('f15ca107-0000-4000-8000-000000000005', 'qbo', FALSE),
                ('f15ca107-0000-4000-8000-000000000006', 'autocount', FALSE),
                ('f15ca107-0000-4000-8000-000000000007', 'accountingsuite', FALSE),
                ('f15ca107-0000-4000-8000-000000000008', 'shopee', FALSE),
                ('f15ca107-0000-4000-8000-000000000009', 'lazada', FALSE),
                ('f15ca107-0000-4000-8000-000000000010', 'tiktok', FALSE),
                ('f15ca107-0000-4000-8000-000000000011', 'development-accounting', FALSE),
                ('f15ca107-0000-4000-8000-000000000012', 'development-commerce', FALSE);

            INSERT INTO "ConnectorVersions" ("Id", "ConnectorId", "Number", "Revision", "State", "Adapter", "Category",
                "Availability", "Configuration", "CreatedAt", "PublishedAt") VALUES
                ('f15ca107-0000-4000-8001-000000000001', 'f15ca107-0000-4000-8000-000000000001', 1, 1, 'Published', 'review-only', 'Accounting Software', 'Under Investigation', '{"name":"MYOB Business","description":"Catalog entry for connecting an existing accounting system to electronic invoicing.","deployment":"Dedicated Cloud Adapter","apiVersion":"","editions":["MYOB Business"],"regions":[],"documentation":["https://developer.myob.com/api/myob-business-api/api-overview/getting-started/"],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":["OAuth Authorization Code"],"priority":"Primary","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000002', 'f15ca107-0000-4000-8000-000000000002', 1, 1, 'Published', 'review-only', 'Accounting Software', 'Under Investigation', '{"name":"MYOB AccountRight Online","description":"Catalog entry for connecting an existing accounting system to electronic invoicing.","deployment":"Dedicated Cloud Adapter","apiVersion":"","editions":["AccountRight Online"],"regions":[],"documentation":["https://developer.myob.com/api/myob-business-api/api-overview/getting-started/"],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":["OAuth Authorization Code"],"priority":"Primary","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000003', 'f15ca107-0000-4000-8000-000000000003', 1, 1, 'Published', 'review-only', 'Accounting Software', 'Under Investigation', '{"name":"MYOB AccountRight Desktop","description":"Catalog entry for connecting an existing accounting system to electronic invoicing.","deployment":"Desktop Agent Adapter","apiVersion":"","editions":["Supported AccountRight Desktop editions"],"regions":[],"documentation":["https://developer.myob.com/api/myob-business-api/api-overview/system-requirements/"],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":["Desktop Agent Authorization"],"priority":"Primary","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000004', 'f15ca107-0000-4000-8000-000000000004', 1, 1, 'Published', 'review-only', 'Accounting Software', 'Partner Approval Required', '{"name":"QuickBooks Desktop","description":"Catalog entry for connecting an existing accounting system to electronic invoicing.","deployment":"QuickBooks Web Connector","apiVersion":"","editions":["Supported QuickBooks Desktop editions"],"regions":[],"documentation":["https://static.developer.intuit.com/qbSDK-current/doc/pdf/QBSDK_ProGuide.pdf"],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":["QuickBooks Web Connector Authorization"],"priority":"Primary","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000005', 'f15ca107-0000-4000-8000-000000000005', 1, 1, 'Published', 'review-only', 'Accounting Software', 'Partner Approval Required', '{"name":"QuickBooks Online","description":"Catalog entry for connecting an existing accounting system to electronic invoicing.","deployment":"Dedicated Cloud Adapter","apiVersion":"","editions":["QuickBooks Online"],"regions":[],"documentation":["https://developer.intuit.com/app/developer/qbo/docs/develop/authentication-and-authorization/oauth-2.0"],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":["OAuth Authorization Code","OAuth Refresh"],"priority":"Primary","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000006', 'f15ca107-0000-4000-8000-000000000006', 1, 1, 'Published', 'review-only', 'Accounting Software', 'Planned', '{"name":"AutoCount Accounting","description":"Catalog entry for connecting an existing accounting system to electronic invoicing.","deployment":"Desktop Agent Adapter","apiVersion":"","editions":[],"regions":[],"documentation":[],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":[],"priority":"Planned","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000007', 'f15ca107-0000-4000-8000-000000000007', 1, 1, 'Published', 'review-only', 'Accounting Software', 'Planned', '{"name":"AccountingSuite / 1C","description":"Catalog entry for connecting an existing accounting system to electronic invoicing.","deployment":"Other Approved Adapter","apiVersion":"","editions":[],"regions":[],"documentation":[],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":[],"priority":"Planned","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000008', 'f15ca107-0000-4000-8000-000000000008', 1, 1, 'Published', 'review-only', 'E-Commerce', 'Partner Approval Required', '{"name":"Shopee Philippines","description":"Catalog entry for authorized Philippine marketplace orders and electronic invoicing.","deployment":"Dedicated Cloud Adapter","apiVersion":"","editions":[],"regions":["PH"],"documentation":["https://open.shopee.com/documents/v2/OpenAPI"],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":[],"priority":"Primary","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000009', 'f15ca107-0000-4000-8000-000000000009', 1, 1, 'Published', 'review-only', 'E-Commerce', 'Partner Approval Required', '{"name":"Lazada Philippines","description":"Catalog entry for authorized Philippine marketplace orders and electronic invoicing.","deployment":"Dedicated Cloud Adapter","apiVersion":"","editions":[],"regions":["PH"],"documentation":["https://open.lazada.com/apps/doc/doc"],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":[],"priority":"Primary","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000010', 'f15ca107-0000-4000-8000-000000000010', 1, 1, 'Published', 'review-only', 'E-Commerce', 'Partner Approval Required', '{"name":"TikTok Shop Philippines","description":"Catalog entry for authorized Philippine marketplace orders and electronic invoicing.","deployment":"Dedicated Cloud Adapter","apiVersion":"","editions":[],"regions":["PH"],"documentation":["https://partner.tiktokshop.com/docv2/page/tts-developer-types"],"requirements":["Verify supported editions, Philippine eligibility, provider authorization and commercial requirements before activation."],"authentication":[],"priority":"Primary","logoLabel":"","capabilities":[],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000011', 'f15ca107-0000-4000-8000-000000000011', 1, 1, 'Published', 'mock-accounting', 'Accounting Software', 'Development Verified', '{"name":"Development Accounting","description":"Explore synthetic company file authorization, master data and external Sales Orders.","deployment":"Other Approved Adapter","apiVersion":"1","editions":["Synthetic company files"],"regions":["PH"],"documentation":[],"requirements":["A paid development subscriber with an eligible plan and an authorized company file.","Map the fictional source codes to company master records."],"authentication":["Synthetic Authorization"],"priority":"Additional","logoLabel":"DA","capabilities":[{"code":"DiscoverCompanyFiles","verification":"Development Verified","documentation":null},{"code":"CustomerRead","verification":"Development Verified","documentation":null},{"code":"ItemRead","verification":"Development Verified","documentation":null},{"code":"TaxCodeRead","verification":"Development Verified","documentation":null},{"code":"UnitOfMeasureRead","verification":"Development Verified","documentation":null},{"code":"AccountingDimensionRead","verification":"Development Verified","documentation":null},{"code":"SalesOrderRead","verification":"Development Verified","documentation":null},{"code":"SalesOrderLineRead","verification":"Development Verified","documentation":null},{"code":"InvoiceOrderLink","verification":"Development Verified","documentation":null},{"code":"PartialInvoicing","verification":"Development Verified","documentation":null}],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z'),
                ('f15ca107-0000-4000-8001-000000000012', 'f15ca107-0000-4000-8000-000000000012', 1, 1, 'Published', 'mock-commerce', 'E-Commerce', 'Development Verified', '{"name":"Development Commerce","description":"Explore synthetic store authorization and marketplace orders for electronic invoicing.","deployment":"Other Approved Adapter","apiVersion":"1","editions":["Synthetic stores"],"regions":["PH"],"documentation":[],"requirements":["A paid development subscriber with an eligible plan and an authorized company file.","Map the fictional source codes to company master records."],"authentication":["Synthetic Authorization"],"priority":"Additional","logoLabel":"DC","capabilities":[{"code":"DiscoverShops","verification":"Development Verified","documentation":null},{"code":"OrderRead","verification":"Development Verified","documentation":null},{"code":"OrderLineRead","verification":"Development Verified","documentation":null},{"code":"ProductRead","verification":"Development Verified","documentation":null},{"code":"CustomerRead","verification":"Development Verified","documentation":null},{"code":"DiscountRead","verification":"Development Verified","documentation":null},{"code":"ShippingChargeRead","verification":"Development Verified","documentation":null},{"code":"TaxRead","verification":"Development Verified","documentation":null},{"code":"PaymentStatusRead","verification":"Development Verified","documentation":null},{"code":"FulfillmentStatusRead","verification":"Development Verified","documentation":null},{"code":"RefundRead","verification":"Development Verified","documentation":null},{"code":"CancellationRead","verification":"Development Verified","documentation":null}],"origins":[],"manualOperations":[]}'::jsonb, '2026-11-27T00:00:00Z', '2026-11-27T00:00:00Z');

            INSERT INTO "VendorConnectorGrants" ("UserId", "Permission")
            SELECT u."Id", permission.name FROM "Users" u
            CROSS JOIN (VALUES ('Connectors.View'), ('Connectors.Edit'), ('Connectors.Publish')) permission(name)
            WHERE u."Kind" = 'Vendor' AND u."DevelopmentFixture" AND EXISTS
                (SELECT 1 FROM "SecurityAudits" a WHERE a."ActorId" = u."Id" AND a."Action" = 'LocalVendorBootstrapped')
            ON CONFLICT DO NOTHING;

            CREATE FUNCTION connect_protect_version() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD."State" = 'Published' THEN RAISE EXCEPTION 'Published connector versions are immutable'; END IF;
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER protect_connector_version BEFORE UPDATE OR DELETE ON "ConnectorVersions"
                FOR EACH ROW EXECUTE FUNCTION connect_protect_version();

            CREATE FUNCTION connect_protect_history() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Connection history and source snapshots are immutable';
            END;
            $$;
            CREATE TRIGGER protect_connection_mapping BEFORE UPDATE OR DELETE ON "ConnectionMappings"
                FOR EACH ROW EXECUTE FUNCTION connect_protect_history();
            CREATE TRIGGER protect_source_order BEFORE UPDATE OR DELETE ON "SourceOrders"
                FOR EACH ROW EXECUTE FUNCTION connect_protect_history();

            CREATE FUNCTION connect_protect_binding() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF (NEW."SubscriberId", NEW."CompanyId", NEW."ConnectorId", NEW."ConnectorVersionId",
                    NEW."CredentialReference", NEW."ExternalAccountId", NEW."ExternalResourceId", NEW."ResourceType", NEW."BranchId")
                    IS DISTINCT FROM (OLD."SubscriberId", OLD."CompanyId", OLD."ConnectorId", OLD."ConnectorVersionId",
                    OLD."CredentialReference", OLD."ExternalAccountId", OLD."ExternalResourceId", OLD."ResourceType", OLD."BranchId")
                THEN RAISE EXCEPTION 'External connections are pinned to their original company, provider and version'; END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER protect_connection_binding BEFORE UPDATE ON "ExternalConnections"
                FOR EACH ROW EXECUTE FUNCTION connect_protect_binding();
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP FUNCTION connect_protect_version() CASCADE;
            DROP FUNCTION connect_protect_history() CASCADE;
            DROP FUNCTION connect_protect_binding() CASCADE;
            """);
            migrationBuilder.DropTable(
                name: "ConnectionMappings");

            migrationBuilder.DropTable(
                name: "DesktopAgentRegistrations");

            migrationBuilder.DropTable(
                name: "SourceOrders");

            migrationBuilder.DropTable(
                name: "VendorConnectorGrants");

            migrationBuilder.DropTable(
                name: "ExternalConnections");

            migrationBuilder.DropTable(
                name: "ConnectionCredentials");

            migrationBuilder.DropTable(
                name: "ConnectorVersions");

            migrationBuilder.DropTable(
                name: "Connectors");
        }
    }
}
