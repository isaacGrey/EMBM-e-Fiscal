using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliationCoverageAndEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonElement>(
                name: "PostedState",
                table: "MockAccountingRecords",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountingObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizationVersion = table.Column<int>(type: "integer", nullable: true),
                    Account = table.Column<string>(type: "text", nullable: false),
                    ExternalCompany = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProtectedValues = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingObservations", x => x.Id);
                    table.UniqueConstraint("AK_AccountingObservations_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AccountingObservations_ExternalConnections_SubscriberId_Com~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ConnectionId },
                        principalTable: "ExternalConnections",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CoverageRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: true),
                    Adoption = table.Column<DateOnly>(type: "date", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    Types = table.Column<string[]>(type: "text[]", nullable: false),
                    IssuanceScope = table.Column<string>(type: "text", nullable: false),
                    ExcludedSources = table.Column<string[]>(type: "text[]", nullable: false),
                    ExclusionReason = table.Column<string>(type: "text", nullable: true),
                    TimeZone = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AmountTolerance = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    FreshnessHours = table.Column<int>(type: "integer", nullable: false),
                    IntervalHours = table.Column<int>(type: "integer", nullable: true),
                    AfterSynchronization = table.Column<bool>(type: "boolean", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoverageRevisions", x => x.Id);
                    table.UniqueConstraint("AK_CoverageRevisions_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_CoverageRevisions_ExternalConnections_SubscriberId_CompanyI~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ConnectionId },
                        principalTable: "ExternalConnections",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MockAccountingRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Scenario = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockAccountingRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockAccountingRevisions_SyncOperations_SubscriberId_Company~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.OperationId },
                        principalTable: "SyncOperations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountingMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedReason = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingMatches_AccountingObservations_SubscriberId_Compa~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ObservationId },
                        principalTable: "AccountingObservations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingMatches_FiscalDocuments_SubscriberId_CompanyId_Do~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountingReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingReferences_AccountingObservations_SubscriberId_Co~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ObservationId },
                        principalTable: "AccountingObservations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CoverageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Failure = table.Column<string>(type: "text", nullable: true),
                    From = table.Column<DateOnly>(type: "date", nullable: true),
                    Through = table.Column<DateOnly>(type: "date", nullable: true),
                    Cursor = table.Column<string>(type: "text", nullable: true),
                    DocumentCursor = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Lease = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationJobs", x => x.Id);
                    table.UniqueConstraint("AK_ReconciliationJobs_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReconciliationJobs_CoverageRevisions_SubscriberId_CompanyId~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.CoverageId },
                        principalTable: "CoverageRevisions",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationJobs_ExternalConnections_SubscriberId_Company~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ConnectionId },
                        principalTable: "ExternalConnections",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CoverageId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitKey = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: true),
                    ExternalNumber = table.Column<string>(type: "text", nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    ExpectedTotal = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    ActualTotal = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    Coverage = table.Column<string>(type: "text", nullable: false),
                    Retrieval = table.Column<string>(type: "text", nullable: false),
                    Completeness = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProtectedComparison = table.Column<string>(type: "text", nullable: false),
                    Categories = table.Column<string[]>(type: "text[]", nullable: false),
                    FindingCount = table.Column<int>(type: "integer", nullable: false),
                    ExternalChanged = table.Column<bool>(type: "boolean", nullable: false),
                    FreshUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Synchronization = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationChecks", x => x.Id);
                    table.UniqueConstraint("AK_ReconciliationChecks_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReconciliationChecks_AccountingObservations_SubscriberId_Co~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.ObservationId },
                        principalTable: "AccountingObservations",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationChecks_CoverageRevisions_SubscriberId_Company~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.CoverageId },
                        principalTable: "CoverageRevisions",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationChecks_FiscalDocuments_SubscriberId_CompanyId~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.DocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationChecks_ReconciliationJobs_SubscriberId_Compan~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.JobId },
                        principalTable: "ReconciliationJobs",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitKey = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Field = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationFindings", x => x.Id);
                    table.UniqueConstraint("AK_ReconciliationFindings_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReconciliationFindings_ReconciliationChecks_SubscriberId_Co~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.CheckId },
                        principalTable: "ReconciliationChecks",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    FindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reviewer = table.Column<Guid>(type: "uuid", nullable: true),
                    Actor = table.Column<Guid>(type: "uuid", nullable: true),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProtectedNote = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationReviews_ReconciliationChecks_SubscriberId_Com~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.VerificationId },
                        principalTable: "ReconciliationChecks",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationReviews_ReconciliationFindings_SubscriberId_C~",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.FindingId },
                        principalTable: "ReconciliationFindings",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PermissionDefinitions",
                column: "Id",
                values: new object[]
                {
                    "Reconciliation.ApproveCoverage",
                    "Reconciliation.AssignReviewer",
                    "Reconciliation.BatchRun",
                    "Reconciliation.Export",
                    "Reconciliation.ManageCoverage",
                    "Reconciliation.ManagePolicy",
                    "Reconciliation.ManageSchedule",
                    "Reconciliation.MatchExternalRecord",
                    "Reconciliation.Review",
                    "Reconciliation.Run",
                    "Reconciliation.ViewHistoricalReferences"
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { "Reconciliation.BatchRun", "accountant" },
                    { "Reconciliation.Export", "accountant" },
                    { "Reconciliation.Review", "accountant" },
                    { "Reconciliation.Run", "accountant" },
                    { "Reconciliation.ViewHistoricalReferences", "accountant" },
                    { "Reconciliation.ApproveCoverage", "company-admin" },
                    { "Reconciliation.AssignReviewer", "company-admin" },
                    { "Reconciliation.BatchRun", "company-admin" },
                    { "Reconciliation.Export", "company-admin" },
                    { "Reconciliation.ManageCoverage", "company-admin" },
                    { "Reconciliation.ManagePolicy", "company-admin" },
                    { "Reconciliation.ManageSchedule", "company-admin" },
                    { "Reconciliation.MatchExternalRecord", "company-admin" },
                    { "Reconciliation.Review", "company-admin" },
                    { "Reconciliation.Run", "company-admin" },
                    { "Reconciliation.ViewHistoricalReferences", "company-admin" },
                    { "Reconciliation.ApproveCoverage", "owner" },
                    { "Reconciliation.AssignReviewer", "owner" },
                    { "Reconciliation.BatchRun", "owner" },
                    { "Reconciliation.Export", "owner" },
                    { "Reconciliation.ManageCoverage", "owner" },
                    { "Reconciliation.ManagePolicy", "owner" },
                    { "Reconciliation.ManageSchedule", "owner" },
                    { "Reconciliation.MatchExternalRecord", "owner" },
                    { "Reconciliation.Review", "owner" },
                    { "Reconciliation.Run", "owner" },
                    { "Reconciliation.ViewHistoricalReferences", "owner" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingMatches_ConnectionId_Type_ExternalId",
                table: "AccountingMatches",
                columns: new[] { "ConnectionId", "Type", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingMatches_SubscriberId_CompanyId_DocumentId",
                table: "AccountingMatches",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingMatches_SubscriberId_CompanyId_ObservationId",
                table: "AccountingMatches",
                columns: new[] { "SubscriberId", "CompanyId", "ObservationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingObservations_SubscriberId_CompanyId_ConnectionId_~",
                table: "AccountingObservations",
                columns: new[] { "SubscriberId", "CompanyId", "ConnectionId", "Type", "ExternalId", "RetrievedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingReferences_SubscriberId_CompanyId_ObservationId",
                table: "AccountingReferences",
                columns: new[] { "SubscriberId", "CompanyId", "ObservationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoverageRevisions_SubscriberId_CompanyId_ConnectionId_Number",
                table: "CoverageRevisions",
                columns: new[] { "SubscriberId", "CompanyId", "ConnectionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockAccountingRevisions_OperationId_Number",
                table: "MockAccountingRevisions",
                columns: new[] { "OperationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockAccountingRevisions_SubscriberId_CompanyId_OperationId",
                table: "MockAccountingRevisions",
                columns: new[] { "SubscriberId", "CompanyId", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationChecks_JobId_UnitKey",
                table: "ReconciliationChecks",
                columns: new[] { "JobId", "UnitKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationChecks_SubscriberId_CompanyId_CoverageId",
                table: "ReconciliationChecks",
                columns: new[] { "SubscriberId", "CompanyId", "CoverageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationChecks_SubscriberId_CompanyId_DocumentId",
                table: "ReconciliationChecks",
                columns: new[] { "SubscriberId", "CompanyId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationChecks_SubscriberId_CompanyId_JobId",
                table: "ReconciliationChecks",
                columns: new[] { "SubscriberId", "CompanyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationChecks_SubscriberId_CompanyId_ObservationId",
                table: "ReconciliationChecks",
                columns: new[] { "SubscriberId", "CompanyId", "ObservationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationChecks_SubscriberId_CompanyId_UnitKey_At",
                table: "ReconciliationChecks",
                columns: new[] { "SubscriberId", "CompanyId", "UnitKey", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationFindings_SubscriberId_CompanyId_CheckId",
                table: "ReconciliationFindings",
                columns: new[] { "SubscriberId", "CompanyId", "CheckId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationFindings_SubscriberId_CompanyId_UnitKey_Categ~",
                table: "ReconciliationFindings",
                columns: new[] { "SubscriberId", "CompanyId", "UnitKey", "Category", "Field" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationJobs_State_AvailableAt",
                table: "ReconciliationJobs",
                columns: new[] { "State", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationJobs_SubscriberId_CompanyId_ConnectionId",
                table: "ReconciliationJobs",
                columns: new[] { "SubscriberId", "CompanyId", "ConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationJobs_SubscriberId_CompanyId_CoverageId",
                table: "ReconciliationJobs",
                columns: new[] { "SubscriberId", "CompanyId", "CoverageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationJobs_SubscriberId_CompanyId_Key",
                table: "ReconciliationJobs",
                columns: new[] { "SubscriberId", "CompanyId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationReviews_FindingId_At",
                table: "ReconciliationReviews",
                columns: new[] { "FindingId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationReviews_SubscriberId_CompanyId_FindingId",
                table: "ReconciliationReviews",
                columns: new[] { "SubscriberId", "CompanyId", "FindingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationReviews_SubscriberId_CompanyId_VerificationId",
                table: "ReconciliationReviews",
                columns: new[] { "SubscriberId", "CompanyId", "VerificationId" });

            migrationBuilder.Sql("""
                CREATE TRIGGER protect_reconciliation_evidence BEFORE UPDATE OR DELETE ON "AccountingObservations"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE TRIGGER protect_reconciliation_evidence BEFORE UPDATE OR DELETE ON "ReconciliationChecks"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE TRIGGER protect_reconciliation_evidence BEFORE UPDATE OR DELETE ON "ReconciliationFindings"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE TRIGGER protect_reconciliation_evidence BEFORE UPDATE OR DELETE ON "ReconciliationReviews"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE TRIGGER protect_reconciliation_evidence BEFORE UPDATE OR DELETE ON "AccountingReferences"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE TRIGGER protect_reconciliation_evidence BEFORE UPDATE OR DELETE ON "AccountingMatches"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE TRIGGER protect_reconciliation_evidence BEFORE UPDATE OR DELETE ON "MockAccountingRevisions"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();
                CREATE FUNCTION protect_coverage_revision() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' AND OLD."EffectiveAt" IS NULL AND OLD."ApprovedBy" IS NULL
                       AND NEW."EffectiveAt" IS NOT NULL AND NEW."ApprovedBy" IS NOT NULL
                       AND (to_jsonb(NEW) - ARRAY['EffectiveAt', 'ApprovedBy']) = (to_jsonb(OLD) - ARRAY['EffectiveAt', 'ApprovedBy'])
                    THEN RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'Coverage revision evidence is immutable';
                END $$;
                CREATE TRIGGER protect_coverage_revision BEFORE UPDATE OR DELETE ON "CoverageRevisions"
                    FOR EACH ROW EXECUTE FUNCTION protect_coverage_revision();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER protect_coverage_revision ON \"CoverageRevisions\"; DROP FUNCTION protect_coverage_revision();");
            migrationBuilder.DropTable(
                name: "AccountingMatches");

            migrationBuilder.DropTable(
                name: "AccountingReferences");

            migrationBuilder.DropTable(
                name: "MockAccountingRevisions");

            migrationBuilder.DropTable(
                name: "ReconciliationReviews");

            migrationBuilder.DropTable(
                name: "ReconciliationFindings");

            migrationBuilder.DropTable(
                name: "ReconciliationChecks");

            migrationBuilder.DropTable(
                name: "AccountingObservations");

            migrationBuilder.DropTable(
                name: "ReconciliationJobs");

            migrationBuilder.DropTable(
                name: "CoverageRevisions");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.BatchRun", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.Export", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.Review", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.Run", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ViewHistoricalReferences", "accountant" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ApproveCoverage", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.AssignReviewer", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.BatchRun", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.Export", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ManageCoverage", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ManagePolicy", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ManageSchedule", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.MatchExternalRecord", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.Review", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.Run", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ViewHistoricalReferences", "company-admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ApproveCoverage", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.AssignReviewer", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.BatchRun", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.Export", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ManageCoverage", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ManagePolicy", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ManageSchedule", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.MatchExternalRecord", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.Review", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.Run", "owner" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { "Reconciliation.ViewHistoricalReferences", "owner" });

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.ApproveCoverage");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.AssignReviewer");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.BatchRun");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.Export");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.ManageCoverage");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.ManagePolicy");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.ManageSchedule");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.MatchExternalRecord");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.Review");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.Run");

            migrationBuilder.DeleteData(
                table: "PermissionDefinitions",
                keyColumn: "Id",
                keyValue: "Reconciliation.ViewHistoricalReferences");

            migrationBuilder.DropColumn(
                name: "PostedState",
                table: "MockAccountingRecords");
        }
    }
}
