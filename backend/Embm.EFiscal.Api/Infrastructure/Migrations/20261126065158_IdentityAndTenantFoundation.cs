using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAndTenantFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PermissionDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RdoReferences",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    DevelopmentFixture = table.Column<bool>(type: "boolean", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RdoReferences", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PlanId = table.Column<string>(type: "text", nullable: false),
                    Billing = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CompanyLimit = table.Column<int>(type: "integer", nullable: false),
                    UserLimit = table.Column<int>(type: "integer", nullable: false),
                    DevelopmentFixture = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscribers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Mobile = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    DevelopmentFixture = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    PermissionId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_PermissionDefinitions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "PermissionDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    RegisteredName = table.Column<string>(type: "text", nullable: false),
                    TradeName = table.Column<string>(type: "text", nullable: true),
                    BusinessClassification = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    ConfigurationStatus = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.UniqueConstraint("AK_Companies_SubscriberId_Id", x => new { x.SubscriberId, x.Id });
                    table.ForeignKey(
                        name: "FK_Companies_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdentityTokens",
                columns: table => new
                {
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityTokens", x => x.Digest);
                    table.ForeignKey(
                        name: "FK_IdentityTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Registrations",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Classification = table.Column<string>(type: "text", nullable: false),
                    BusinessName = table.Column<string>(type: "text", nullable: true),
                    PlanId = table.Column<string>(type: "text", nullable: false),
                    Billing = table.Column<string>(type: "text", nullable: false),
                    TermsAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PrivacyAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registrations", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Registrations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecurityStamp = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActiveSubscriberId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveCompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveBranchId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriberMemberships",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriberMemberships", x => new { x.SubscriberId, x.UserId });
                    table.ForeignKey(
                        name: "FK_SubscriberMemberships_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubscriberMemberships_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubscriberMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserIdentities",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    SecurityStamp = table.Column<Guid>(type: "uuid", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserIdentities", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserIdentities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    TaxRegistration = table.Column<string>(type: "text", nullable: true),
                    Contact = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.UniqueConstraint("AK_Branches_SubscriberId_CompanyId_Id", x => new { x.SubscriberId, x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Branches_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrationEvidence_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaxpayerProfiles",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tin = table.Column<string>(type: "text", nullable: false),
                    VatStatus = table.Column<string>(type: "text", nullable: false),
                    RegistrationDetails = table.Column<string>(type: "text", nullable: true),
                    PermitReference = table.Column<string>(type: "text", nullable: true),
                    VerificationStatus = table.Column<string>(type: "text", nullable: false),
                    IssuanceStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxpayerProfiles", x => new { x.SubscriberId, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_TaxpayerProfiles_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyMemberships",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyMemberships", x => new { x.SubscriberId, x.CompanyId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CompanyMemberships_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanyMemberships_SubscriberMemberships_SubscriberId_UserId",
                        columns: x => new { x.SubscriberId, x.UserId },
                        principalTable: "SubscriberMemberships",
                        principalColumns: new[] { "SubscriberId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.UniqueConstraint("AK_Invitations_SubscriberId_Id", x => new { x.SubscriberId, x.Id });
                    table.ForeignKey(
                        name: "FK_Invitations_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invitations_SubscriberMemberships_SubscriberId_InvitedById",
                        columns: x => new { x.SubscriberId, x.InvitedById },
                        principalTable: "SubscriberMemberships",
                        principalColumns: new[] { "SubscriberId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RdoAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    RegistrationStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RdoAssignments", x => x.Id);
                    table.CheckConstraint("CK_RdoAssignment_Dates", "\"EffectiveUntil\" IS NULL OR \"EffectiveUntil\" > \"EffectiveFrom\"");
                    table.ForeignKey(
                        name: "FK_RdoAssignments_Branches_SubscriberId_CompanyId_BranchId",
                        columns: x => new { x.SubscriberId, x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "SubscriberId", "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_RdoAssignments_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RdoAssignments_RdoReferences_Code",
                        column: x => x.Code,
                        principalTable: "RdoReferences",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvitationCompanies",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationCompanies", x => new { x.SubscriberId, x.InvitationId, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_InvitationCompanies_Companies_SubscriberId_CompanyId",
                        columns: x => new { x.SubscriberId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvitationCompanies_Invitations_SubscriberId_InvitationId",
                        columns: x => new { x.SubscriberId, x.InvitationId },
                        principalTable: "Invitations",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "PermissionDefinitions",
                column: "Id",
                values: new object[]
                {
                    "Billing.Manage",
                    "Billing.View",
                    "Branch.Manage",
                    "Branch.View",
                    "Company.Manage",
                    "Company.View",
                    "CreditMemo.SaveAndSend",
                    "CreditMemo.View",
                    "Credits.Purchase",
                    "Credits.View",
                    "Integration.Manage",
                    "Invoice.Create",
                    "Invoice.SaveAndSend",
                    "Invoice.View",
                    "Mapping.Manage",
                    "Reconciliation.View",
                    "Reports.View",
                    "SalesOrder.Save",
                    "SalesOrder.View",
                    "Users.Invite",
                    "Users.Manage"
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { "accountant", "Accountant" },
                    { "auditor", "Auditor" },
                    { "company-admin", "Company Administrator" },
                    { "encoder", "Transaction Encoder" },
                    { "owner", "Account Owner" },
                    { "readonly", "Read-Only User" }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { "Branch.View", "accountant" },
                    { "Company.View", "accountant" },
                    { "CreditMemo.SaveAndSend", "accountant" },
                    { "CreditMemo.View", "accountant" },
                    { "Invoice.Create", "accountant" },
                    { "Invoice.SaveAndSend", "accountant" },
                    { "Invoice.View", "accountant" },
                    { "Reconciliation.View", "accountant" },
                    { "Reports.View", "accountant" },
                    { "SalesOrder.Save", "accountant" },
                    { "SalesOrder.View", "accountant" },
                    { "Branch.View", "auditor" },
                    { "Company.View", "auditor" },
                    { "CreditMemo.View", "auditor" },
                    { "Invoice.View", "auditor" },
                    { "Reconciliation.View", "auditor" },
                    { "Reports.View", "auditor" },
                    { "SalesOrder.View", "auditor" },
                    { "Branch.Manage", "company-admin" },
                    { "Branch.View", "company-admin" },
                    { "Company.Manage", "company-admin" },
                    { "Company.View", "company-admin" },
                    { "CreditMemo.SaveAndSend", "company-admin" },
                    { "CreditMemo.View", "company-admin" },
                    { "Integration.Manage", "company-admin" },
                    { "Invoice.Create", "company-admin" },
                    { "Invoice.SaveAndSend", "company-admin" },
                    { "Invoice.View", "company-admin" },
                    { "Mapping.Manage", "company-admin" },
                    { "Reconciliation.View", "company-admin" },
                    { "Reports.View", "company-admin" },
                    { "SalesOrder.Save", "company-admin" },
                    { "SalesOrder.View", "company-admin" },
                    { "Users.Invite", "company-admin" },
                    { "Branch.View", "encoder" },
                    { "Company.View", "encoder" },
                    { "CreditMemo.SaveAndSend", "encoder" },
                    { "CreditMemo.View", "encoder" },
                    { "Invoice.Create", "encoder" },
                    { "Invoice.SaveAndSend", "encoder" },
                    { "Invoice.View", "encoder" },
                    { "SalesOrder.Save", "encoder" },
                    { "SalesOrder.View", "encoder" },
                    { "Billing.Manage", "owner" },
                    { "Billing.View", "owner" },
                    { "Branch.Manage", "owner" },
                    { "Branch.View", "owner" },
                    { "Company.Manage", "owner" },
                    { "Company.View", "owner" },
                    { "CreditMemo.SaveAndSend", "owner" },
                    { "CreditMemo.View", "owner" },
                    { "Credits.Purchase", "owner" },
                    { "Credits.View", "owner" },
                    { "Integration.Manage", "owner" },
                    { "Invoice.Create", "owner" },
                    { "Invoice.SaveAndSend", "owner" },
                    { "Invoice.View", "owner" },
                    { "Mapping.Manage", "owner" },
                    { "Reconciliation.View", "owner" },
                    { "Reports.View", "owner" },
                    { "SalesOrder.Save", "owner" },
                    { "SalesOrder.View", "owner" },
                    { "Users.Invite", "owner" },
                    { "Users.Manage", "owner" },
                    { "Branch.View", "readonly" },
                    { "Company.View", "readonly" },
                    { "CreditMemo.View", "readonly" },
                    { "Invoice.View", "readonly" },
                    { "SalesOrder.View", "readonly" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_SubscriberId_CompanyId",
                table: "Branches",
                columns: new[] { "SubscriberId", "CompanyId" },
                unique: true,
                filter: "\"Type\" = 'HeadOffice'");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_SubscriberId_CompanyId_Code",
                table: "Branches",
                columns: new[] { "SubscriberId", "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_SubscriberId_Code",
                table: "Companies",
                columns: new[] { "SubscriberId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMemberships_SubscriberId_UserId",
                table: "CompanyMemberships",
                columns: new[] { "SubscriberId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityTokens_UserId_Purpose",
                table: "IdentityTokens",
                columns: new[] { "UserId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_InvitationCompanies_SubscriberId_CompanyId",
                table: "InvitationCompanies",
                columns: new[] { "SubscriberId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_Digest",
                table: "Invitations",
                column: "Digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_RoleId",
                table: "Invitations",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_SubscriberId_InvitedById",
                table: "Invitations",
                columns: new[] { "SubscriberId", "InvitedById" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_SubscriberId_NormalizedEmail",
                table: "Invitations",
                columns: new[] { "SubscriberId", "NormalizedEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_RdoAssignments_Code",
                table: "RdoAssignments",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_RdoAssignments_SubscriberId_CompanyId_BranchId",
                table: "RdoAssignments",
                columns: new[] { "SubscriberId", "CompanyId", "BranchId" },
                unique: true,
                filter: "\"EffectiveUntil\" IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_RdoAssignments_SubscriberId_CompanyId_BranchId_EffectiveFrom",
                table: "RdoAssignments",
                columns: new[] { "SubscriberId", "CompanyId", "BranchId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationEvidence_SubscriberId_CompanyId",
                table: "RegistrationEvidence",
                columns: new[] { "SubscriberId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAudits_SubscriberId_At",
                table: "SecurityAudits",
                columns: new[] { "SubscriberId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UserId_ExpiresAt",
                table: "Sessions",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberMemberships_RoleId",
                table: "SubscriberMemberships",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberMemberships_UserId",
                table: "SubscriberMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_NormalizedEmail",
                table: "UserIdentities",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyMemberships");

            migrationBuilder.DropTable(
                name: "IdentityTokens");

            migrationBuilder.DropTable(
                name: "InvitationCompanies");

            migrationBuilder.DropTable(
                name: "RdoAssignments");

            migrationBuilder.DropTable(
                name: "RegistrationEvidence");

            migrationBuilder.DropTable(
                name: "Registrations");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "SecurityAudits");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "TaxpayerProfiles");

            migrationBuilder.DropTable(
                name: "UserIdentities");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "RdoReferences");

            migrationBuilder.DropTable(
                name: "PermissionDefinitions");

            migrationBuilder.DropTable(
                name: "SubscriberMemberships");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Subscribers");
        }
    }
}
