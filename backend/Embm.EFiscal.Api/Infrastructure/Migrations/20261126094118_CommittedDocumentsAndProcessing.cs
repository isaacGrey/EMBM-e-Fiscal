using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommittedDocumentsAndProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentNumberSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Digits = table.Column<int>(type: "integer", nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    InitialNumber = table.Column<long>(type: "bigint", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberSeries", x => x.Id);
                    table.UniqueConstraint("AK_DocumentNumberSeries_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.CheckConstraint("CK_DocumentSeries_Sequence", "\"Digits\" BETWEEN 3 AND 12 AND \"InitialNumber\" > 0 AND \"NextNumber\" >= \"InitialNumber\"");
                    table.ForeignKey(
                        name: "FK_DocumentNumberSeries_Branches_SubscriberId_CompanyId_Branch~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentNumberSeries_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FiscalDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Number = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    CustomerCode = table.Column<string>(type: "text", nullable: false),
                    CustomerName = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LineCount = table.Column<int>(type: "integer", nullable: false),
                    Credits = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Precision = table.Column<int>(type: "integer", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    GrossSales = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    CommercialDiscount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    StatutoryDiscount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    VatableSales = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    ExemptSales = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    ZeroRatedSales = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    NonVatSales = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    IncomeWithholding = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    VatWithholding = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    AmountDue = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Rounding = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    DevelopmentFixture = table.Column<bool>(type: "boolean", nullable: false),
                    PartialFulfillment = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalOrderKey = table.Column<string>(type: "text", nullable: true),
                    ExternalOrderNumber = table.Column<string>(type: "text", nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalDocuments", x => x.Id);
                    table.UniqueConstraint("AK_FiscalDocuments_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.CheckConstraint("CK_Documents_Action", "(\"Type\" = 'SalesOrder' AND \"Credits\" = 0 AND \"SourceDocumentId\" IS NULL AND \"ExternalOrderKey\" IS NULL) OR\n(\"Type\" = 'SalesInvoice' AND \"Credits\" = 1 AND (\"SourceDocumentId\" IS NULL OR \"ExternalOrderKey\" IS NULL)) OR\n(\"Type\" = 'CreditMemo' AND \"Credits\" = 1 AND \"SourceDocumentId\" IS NOT NULL AND \"ExternalOrderKey\" IS NULL)");
                    table.CheckConstraint("CK_Documents_Amounts", "\"LineCount\" BETWEEN 1 AND 100 AND \"Precision\" BETWEEN 0 AND 6 AND \"ExchangeRate\" > 0\nAND \"Total\" >= 0 AND \"AmountDue\" = \"Total\" - \"IncomeWithholding\" - \"VatWithholding\"\nAND \"AmountDue\" >= 0");
                    table.ForeignKey(
                        name: "FK_FiscalDocuments_Branches_SubscriberId_CompanyId_BranchId",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDocuments_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDocuments_DocumentNumberSeries_SubscriberId_CompanyId~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.SeriesId },
                        principalTable: "DocumentNumberSeries",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDocuments_FiscalDocuments_SubscriberId_CompanyId_Sour~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.SourceDocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDocuments_MasterRecords_SubscriberId_CompanyId_Custom~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.CustomerId },
                        principalTable: "MasterRecords",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentCommitments",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCommitments", x => new { x.SubscriberId, x.CompanyId, x.ActorId, x.Key });
                    table.ForeignKey(
                        name: "FK_DocumentCommitments_FiscalDocuments_SubscriberId_CompanyId_~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    Recipient = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentOutboxMessages", x => x.Id);
                    table.UniqueConstraint("AK_DocumentOutboxMessages_SubscriberId_CompanyId_DocumentId_Id", x => new { x.SubscriberId, x.CompanyId, x.DocumentId, x.Id });
                    table.ForeignKey(
                        name: "FK_DocumentOutboxMessages_FiscalDocuments_SubscriberId_Company~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentProcessing",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rendering = table.Column<string>(type: "text", nullable: false),
                    ElectronicDelivery = table.Column<string>(type: "text", nullable: false),
                    Accounting = table.Column<string>(type: "text", nullable: false),
                    Regulator = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentProcessing", x => new { x.SubscriberId, x.CompanyId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_DocumentProcessing_FiscalDocuments_SubscriberId_CompanyId_D~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FiscalDocumentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ItemCode = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    UnitCode = table.Column<string>(type: "text", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    BaseFactor = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Gross = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    CommercialDiscount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    StatutoryDiscount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    VatableSales = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    ExemptSales = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    ZeroRatedSales = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    NonVatSales = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    IncomeWithholding = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    VatWithholding = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Terms = table.Column<JsonElement>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalDocumentLines", x => x.Id);
                    table.UniqueConstraint("AK_FiscalDocumentLines_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.CheckConstraint("CK_DocumentLines_Amounts", "\"Position\" BETWEEN 1 AND 100 AND \"Quantity\" >= 0 AND \"BaseFactor\" > 0\nAND \"Total\" = \"VatableSales\" + \"ExemptSales\" + \"ZeroRatedSales\" + \"NonVatSales\" + \"Vat\" AND \"Total\" >= 0");
                    table.ForeignKey(
                        name: "FK_FiscalDocumentLines_FiscalDocuments_SubscriberId_CompanyId_~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDocumentLines_MasterRecords_SubscriberId_CompanyId_It~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ItemId },
                        principalTable: "MasterRecords",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalDocumentLines_MasterRecords_SubscriberId_CompanyId_Un~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.UnitId },
                        principalTable: "MasterRecords",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FiscalDocumentSnapshots",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Parties = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    Header = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    Numbering = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    Templates = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    Delivery = table.Column<JsonElement>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalDocumentSnapshots", x => new { x.SubscriberId, x.CompanyId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_FiscalDocumentSnapshots_FiscalDocuments_SubscriberId_Compan~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IssuedDocumentFiles",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    RenderedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedDocumentFiles", x => new { x.SubscriberId, x.CompanyId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_IssuedDocumentFiles_FiscalDocuments_SubscriberId_CompanyId_~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Recipient = table.Column<string>(type: "text", nullable: false),
                    ProviderReference = table.Column<string>(type: "text", nullable: true),
                    AttachmentSha256 = table.Column<string>(type: "text", nullable: true),
                    ErrorCategory = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentDeliveryAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentDeliveryAttempts_DocumentOutboxMessages_SubscriberI~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId, x.JobId },
                        principalTable: "DocumentOutboxMessages",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "DocumentId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentAllocations",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalOrderKey = table.Column<string>(type: "text", nullable: true),
                    ExternalLineKey = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAllocations", x => new { x.SubscriberId, x.CompanyId, x.LineId });
                    table.CheckConstraint("CK_DocumentAllocation_Source", "(\"SourceLineId\" IS NOT NULL AND \"ExternalOrderKey\" IS NULL AND \"ExternalLineKey\" IS NULL) OR\n(\"SourceLineId\" IS NULL AND \"ExternalOrderKey\" IS NOT NULL AND \"ExternalLineKey\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_DocumentAllocations_FiscalDocumentLines_SubscriberId_Compan~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.LineId },
                        principalTable: "FiscalDocumentLines",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentAllocations_FiscalDocumentLines_SubscriberId_Compa~1",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.SourceLineId },
                        principalTable: "FiscalDocumentLines",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PermissionDefinitions",
                column: "Id",
                values: new object[]
                {
                    "Documents.Export",
                    "Documents.Resend"
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { "Documents.Export", "accountant" },
                    { "Documents.Resend", "accountant" },
                    { "Documents.Export", "company-admin" },
                    { "Documents.Resend", "company-admin" },
                    { "Documents.Export", "owner" },
                    { "Documents.Resend", "owner" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAllocations_SubscriberId_CompanyId_ExternalOrderKey~",
                table: "DocumentAllocations",
                columns: new[] { "SubscriberId", "CompanyId", "ExternalOrderKey", "ExternalLineKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAllocations_SubscriberId_CompanyId_SourceLineId_Kind",
                table: "DocumentAllocations",
                columns: new[] { "SubscriberId", "CompanyId", "SourceLineId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCommitments_SubscriberId_CompanyId_DocumentId",
                table: "DocumentCommitments",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentDeliveryAttempts_SubscriberId_CompanyId_DocumentId_~",
                table: "DocumentDeliveryAttempts",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentId", "JobId", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberSeries_SubscriberId_CompanyId_BranchId",
                table: "DocumentNumberSeries",
                columns: new[] { "SubscriberId", "CompanyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberSeries_SubscriberId_CompanyId_Type_BranchId_R~",
                table: "DocumentNumberSeries",
                columns: new[] { "SubscriberId", "CompanyId", "Type", "BranchId", "Revision" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentOutboxMessages_Kind_State_AvailableAt",
                table: "DocumentOutboxMessages",
                columns: new[] { "Kind", "State", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentOutboxMessages_SubscriberId_CompanyId_DocumentId_Ki~",
                table: "DocumentOutboxMessages",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentId", "Kind", "Operation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocumentLines_SubscriberId_CompanyId_DocumentId_Posit~",
                table: "FiscalDocumentLines",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocumentLines_SubscriberId_CompanyId_ItemId",
                table: "FiscalDocumentLines",
                columns: new[] { "SubscriberId", "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocumentLines_SubscriberId_CompanyId_UnitId",
                table: "FiscalDocumentLines",
                columns: new[] { "SubscriberId", "CompanyId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_SubscriberId_CompanyId_BranchId",
                table: "FiscalDocuments",
                columns: new[] { "SubscriberId", "CompanyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_SubscriberId_CompanyId_CustomerId",
                table: "FiscalDocuments",
                columns: new[] { "SubscriberId", "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_SubscriberId_CompanyId_SeriesId",
                table: "FiscalDocuments",
                columns: new[] { "SubscriberId", "CompanyId", "SeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_SubscriberId_CompanyId_SourceDocumentId",
                table: "FiscalDocuments",
                columns: new[] { "SubscriberId", "CompanyId", "SourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_SubscriberId_CompanyId_Type_Date_Id",
                table: "FiscalDocuments",
                columns: new[] { "SubscriberId", "CompanyId", "Type", "Date", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_SubscriberId_CompanyId_Type_Number",
                table: "FiscalDocuments",
                columns: new[] { "SubscriberId", "CompanyId", "Type", "Number" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_committed_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Committed document history is immutable';
                END;
                $$;

                CREATE TRIGGER document_immutable BEFORE UPDATE OR DELETE ON "FiscalDocuments"
                    FOR EACH ROW EXECUTE FUNCTION reject_committed_change();
                CREATE TRIGGER document_line_immutable BEFORE UPDATE OR DELETE ON "FiscalDocumentLines"
                    FOR EACH ROW EXECUTE FUNCTION reject_committed_change();
                CREATE TRIGGER document_snapshot_immutable BEFORE UPDATE OR DELETE ON "FiscalDocumentSnapshots"
                    FOR EACH ROW EXECUTE FUNCTION reject_committed_change();
                CREATE TRIGGER document_commitment_immutable BEFORE UPDATE OR DELETE ON "DocumentCommitments"
                    FOR EACH ROW EXECUTE FUNCTION reject_committed_change();
                CREATE TRIGGER document_allocation_immutable BEFORE UPDATE OR DELETE ON "DocumentAllocations"
                    FOR EACH ROW EXECUTE FUNCTION reject_committed_change();
                CREATE TRIGGER issued_document_file_immutable BEFORE UPDATE OR DELETE ON "IssuedDocumentFiles"
                    FOR EACH ROW EXECUTE FUNCTION reject_committed_change();
                CREATE TRIGGER document_delivery_attempt_immutable BEFORE UPDATE OR DELETE ON "DocumentDeliveryAttempts"
                    FOR EACH ROW EXECUTE FUNCTION reject_committed_change();
                CREATE TRIGGER company_template_version_immutable BEFORE UPDATE OR DELETE ON "CompanyTemplateVersions"
                    FOR EACH ROW EXECUTE FUNCTION reject_committed_change();

                CREATE FUNCTION guard_document_number_series() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Number series revisions are retained';
                    END IF;
                    IF to_jsonb(NEW) - 'NextNumber' <> to_jsonb(OLD) - 'NextNumber'
                        OR NEW."NextNumber" < OLD."NextNumber" THEN
                        RAISE EXCEPTION 'Only forward allocation may change a number series revision';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER document_series_guard BEFORE UPDATE OR DELETE ON "DocumentNumberSeries"
                    FOR EACH ROW EXECUTE FUNCTION guard_document_number_series();

                CREATE FUNCTION guard_document_line_insert() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM "FiscalDocuments" d WHERE d."SubscriberId" = NEW."SubscriberId"
                        AND d."CompanyId" = NEW."CompanyId" AND d."Id" = NEW."DocumentId" AND NEW."Position" <= d."LineCount"
                        AND (d."Type" = 'CreditMemo' OR NEW."Quantity" > 0)) THEN
                        RAISE EXCEPTION 'Document line exceeds committed structure';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER document_line_insert_guard BEFORE INSERT ON "FiscalDocumentLines"
                    FOR EACH ROW EXECUTE FUNCTION guard_document_line_insert();

                CREATE FUNCTION verify_document_commitment() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    line_count integer;
                    line_total numeric;
                    credit_count integer;
                    credit_quantity integer;
                BEGIN
                    SELECT COUNT(*), COALESCE(SUM("Total"), 0) INTO line_count, line_total FROM "FiscalDocumentLines"
                    WHERE "SubscriberId" = NEW."SubscriberId" AND "CompanyId" = NEW."CompanyId" AND "DocumentId" = NEW."Id";
                    SELECT COUNT(*), COALESCE(SUM("Quantity"), 0) INTO credit_count, credit_quantity FROM "CreditLedgerEntries"
                    WHERE "SubscriberId" = NEW."SubscriberId" AND "CompanyId" = NEW."CompanyId" AND "DocumentId" = NEW."Id"
                       AND "DocumentType" = NEW."Type" AND "EventType" = 'Consumption';
                    IF line_count <> NEW."LineCount" OR line_total <> NEW."Total" OR credit_count <> NEW."Credits"
                       OR credit_quantity <> NEW."Credits"
                       OR NOT EXISTS (SELECT 1 FROM "FiscalDocumentSnapshots" s WHERE s."SubscriberId" = NEW."SubscriberId"
                           AND s."CompanyId" = NEW."CompanyId" AND s."DocumentId" = NEW."Id")
                       OR NOT EXISTS (SELECT 1 FROM "DocumentCommitments" c WHERE c."SubscriberId" = NEW."SubscriberId"
                           AND c."CompanyId" = NEW."CompanyId" AND c."DocumentId" = NEW."Id") THEN
                        RAISE EXCEPTION 'Document amounts, credit consumption, or snapshot are incomplete';
                    END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER document_commitment_consistency AFTER INSERT ON "FiscalDocuments"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_document_commitment();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION verify_document_commitment() CASCADE;
                DROP FUNCTION guard_document_line_insert() CASCADE;
                DROP FUNCTION guard_document_number_series() CASCADE;
                DROP FUNCTION reject_committed_change() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "DocumentAllocations");

            migrationBuilder.DropTable(
                name: "DocumentCommitments");

            migrationBuilder.DropTable(
                name: "DocumentDeliveryAttempts");

            migrationBuilder.DropTable(
                name: "DocumentProcessing");

            migrationBuilder.DropTable(
                name: "FiscalDocumentSnapshots");

            migrationBuilder.DropTable(
                name: "IssuedDocumentFiles");

            migrationBuilder.DropTable(
                name: "FiscalDocumentLines");

            migrationBuilder.DropTable(
                name: "DocumentOutboxMessages");

            migrationBuilder.DropTable(
                name: "FiscalDocuments");

            migrationBuilder.DropTable(
                name: "DocumentNumberSeries");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Documents.Export", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Documents.Resend", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Documents.Export", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Documents.Resend", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Documents.Export", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Documents.Resend", "owner" });

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Documents.Export");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Documents.Resend");
        }
    }
}
