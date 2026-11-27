using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionPaymentsAndCreditWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutoTopUpPreferences",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Threshold = table.Column<int>(type: "integer", nullable: false),
                    PackageVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaximumAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PeriodMaximumSpend = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTopUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailure = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoTopUpPreferences", x => x.SubscriberId);
                    table.ForeignKey(
                        name: "FK_AutoTopUpPreferences_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillingPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPaymentMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingPaymentMethods_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CreditWallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditWallets", x => x.Id);
                    table.UniqueConstraint("AK_CreditWallets_SubscriberId_Id", x => new { x.SubscriberId, x.Id });
                    table.ForeignKey(
                        name: "FK_CreditWallets_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackageVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Credits = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    ValidityDays = table.Column<int>(type: "integer", nullable: true),
                    EligiblePlans = table.Column<string[]>(type: "text[]", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MonthlyCredits = table.Column<int>(type: "integer", nullable: false),
                    AnnualCredits = table.Column<int>(type: "integer", nullable: false),
                    AllocationFrequency = table.Column<string>(type: "text", nullable: false),
                    CompanyLimit = table.Column<int>(type: "integer", nullable: false),
                    UserLimit = table.Column<int>(type: "integer", nullable: false),
                    ConnectorLimit = table.Column<int>(type: "integer", nullable: false),
                    Integrations = table.Column<string[]>(type: "text[]", nullable: false),
                    ConnectorCategories = table.Column<string[]>(type: "text[]", nullable: false),
                    Features = table.Column<string[]>(type: "text[]", nullable: false),
                    CreditValidityDays = table.Column<int>(type: "integer", nullable: true),
                    Rollover = table.Column<bool>(type: "boolean", nullable: false),
                    RenewalAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    AutoTopUpAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingFrequency = table.Column<string>(type: "text", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutOrders_PackageVersions_PackageVersionId",
                        column: x => x.PackageVersionId,
                        principalTable: "PackageVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CheckoutOrders_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CheckoutOrders_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CheckoutOrders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "text", nullable: false),
                    ProviderTransactionReference = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefundedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_CheckoutOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "CheckoutOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CreditLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PolicyVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Slice = table.Column<int>(type: "integer", nullable: false),
                    Granted = table.Column<int>(type: "integer", nullable: false),
                    Remaining = table.Column<int>(type: "integer", nullable: false),
                    Reserved = table.Column<int>(type: "integer", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditLots", x => x.Id);
                    table.UniqueConstraint("AK_CreditLots_SubscriberId_WalletId_Id", x => new { x.SubscriberId, x.WalletId, x.Id });
                    table.CheckConstraint("CK_CreditLot_Balances", "\"Granted\" > 0 AND \"Reserved\" >= 0 AND \"Remaining\" >= \"Reserved\" AND \"Remaining\" <= \"Granted\"");
                    table.ForeignKey(
                        name: "FK_CreditLots_CreditWallets_SubscriberId_WalletId",
                        columns: x => new { x.SubscriberId, x.WalletId },
                        principalTable: "CreditWallets",
                        principalColumns: new[] { "SubscriberId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreditLots_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    EventKey = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    PayloadDigest = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriberSubscriptions",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingFrequency = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Suspended = table.Column<bool>(type: "boolean", nullable: false),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    NextPlanVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NextFrequency = table.Column<string>(type: "text", nullable: true),
                    PaymentMethodId = table.Column<Guid>(type: "uuid", nullable: true),
                    RenewalConsentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AllocatedSlices = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriberSubscriptions", x => x.SubscriberId);
                    table.ForeignKey(
                        name: "FK_SubscriberSubscriptions_Payments_PeriodPaymentId",
                        column: x => x.PeriodPaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubscriberSubscriptions_PlanVersions_NextPlanVersionId",
                        column: x => x.NextPlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriberSubscriptions_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubscriberSubscriptions_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CreditLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentType = table.Column<string>(type: "text", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditLedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_CreditLedger_Positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_CreditLedgerEntries_CreditLots_SubscriberId_WalletId_LotId",
                        columns: x => new { x.SubscriberId, x.WalletId, x.LotId },
                        principalTable: "CreditLots",
                        principalColumns: new[] { "SubscriberId", "WalletId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CreditReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    OperationKey = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditReservations_CreditLots_SubscriberId_WalletId_LotId",
                        columns: x => new { x.SubscriberId, x.WalletId, x.LotId },
                        principalTable: "CreditLots",
                        principalColumns: new[] { "SubscriberId", "WalletId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "PackageVersions",
                columns: new[] { "Id", "Active", "Code", "Credits", "Currency", "EffectiveAt", "EligiblePlans", "Name", "Price", "ValidityDays", "Version" },
                values: new object[,]
                {
                    { new Guid("e415ca10-0000-4000-8000-000000000011"), true, "credits-100", 100, "PHP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new string[0], "100 Save & Send Credits", 200m, null, 1 },
                    { new Guid("e415ca10-0000-4000-8000-000000000012"), true, "credits-500", 500, "PHP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new string[0], "500 Save & Send Credits", 900m, null, 1 },
                    { new Guid("e415ca10-0000-4000-8000-000000000013"), true, "credits-1000", 1000, "PHP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new string[0], "1000 Save & Send Credits", 1700m, null, 1 }
                });

            migrationBuilder.InsertData(
                table: "PlanVersions",
                columns: new[] { "Id", "Active", "AllocationFrequency", "AnnualCredits", "AnnualPrice", "AutoTopUpAllowed", "Code", "CompanyLimit", "ConnectorCategories", "ConnectorLimit", "CreditValidityDays", "Description", "EffectiveAt", "Features", "GraceDays", "Integrations", "MonthlyCredits", "MonthlyPrice", "Name", "RenewalAllowed", "Rollover", "UserLimit", "Version" },
                values: new object[,]
                {
                    { new Guid("e415ca10-0000-4000-8000-000000000001"), true, "period", 3000, 4990m, false, "starter", 1, new[] { "Accounting" }, 2, null, "Electronic invoicing for a single company.", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new[] { "Electronic invoice issuance and delivery", "Philippine tax configuration", "Secure invoice records and audit history" }, 0, new[] { "autocount", "qbo" }, 250, 499m, "Starter", true, true, 2, 1 },
                    { new Guid("e415ca10-0000-4000-8000-000000000002"), true, "period", 12000, 14990m, true, "business", 3, new[] { "Accounting", "Commerce" }, 5, null, "Invoice issuance and reconciliation across company files.", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new[] { "Electronic invoice issuance and delivery", "Philippine tax configuration", "Secure invoice records and audit history", "Accounting field mapping", "Electronic invoice reconciliation", "BIR reporting readiness" }, 0, new[] { "autocount", "qbd", "qbo", "accountingsuite", "shopee" }, 1000, 1499m, "Business", true, true, 10, 1 },
                    { new Guid("e415ca10-0000-4000-8000-000000000003"), true, "period", 120000, 49990m, true, "enterprise", 20, new[] { "Accounting", "Commerce" }, 6, null, "Larger invoice volumes and broader team access.", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new[] { "Electronic invoice issuance and delivery", "Philippine tax configuration", "Secure invoice records and audit history", "Accounting field mapping", "Electronic invoice reconciliation", "BIR reporting readiness" }, 0, new[] { "autocount", "qbd", "qbo", "accountingsuite", "shopee", "tiktok" }, 10000, 4999m, "Enterprise", true, true, 50, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentMethods_SubscriberId_Provider",
                table: "BillingPaymentMethods",
                columns: new[] { "SubscriberId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutOrders_PackageVersionId",
                table: "CheckoutOrders",
                column: "PackageVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutOrders_PlanVersionId",
                table: "CheckoutOrders",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutOrders_SubscriberId_CreatedAt",
                table: "CheckoutOrders",
                columns: new[] { "SubscriberId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutOrders_UserId_IdempotencyKey",
                table: "CheckoutOrders",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedgerEntries_SubscriberId_DocumentType_DocumentId",
                table: "CreditLedgerEntries",
                columns: new[] { "SubscriberId", "DocumentType", "DocumentId" },
                unique: true,
                filter: "\"EventType\" = 'Consumption'");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedgerEntries_SubscriberId_WalletId_LotId",
                table: "CreditLedgerEntries",
                columns: new[] { "SubscriberId", "WalletId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedgerEntries_WalletId_EventType_IdempotencyKey",
                table: "CreditLedgerEntries",
                columns: new[] { "WalletId", "EventType", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditLots_PaymentId",
                table: "CreditLots",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLots_SubscriberId_PaymentId_Slice",
                table: "CreditLots",
                columns: new[] { "SubscriberId", "PaymentId", "Slice" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditReservations_SubscriberId_WalletId_LotId",
                table: "CreditReservations",
                columns: new[] { "SubscriberId", "WalletId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditReservations_WalletId_OperationKey",
                table: "CreditReservations",
                columns: new[] { "WalletId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditWallets_SubscriberId",
                table: "CreditWallets",
                column: "SubscriberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageVersions_Code_Version",
                table: "PackageVersions",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_PaymentId",
                table: "PaymentEvents",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_Provider_EventKey",
                table: "PaymentEvents",
                columns: new[] { "Provider", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Provider_ProviderPaymentId",
                table: "Payments",
                columns: new[] { "Provider", "ProviderPaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_Code_Version",
                table: "PlanVersions",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberSubscriptions_NextPlanVersionId",
                table: "SubscriberSubscriptions",
                column: "NextPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberSubscriptions_PeriodPaymentId",
                table: "SubscriberSubscriptions",
                column: "PeriodPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberSubscriptions_PlanVersionId",
                table: "SubscriberSubscriptions",
                column: "PlanVersionId");
            migrationBuilder.Sql("""
                CREATE FUNCTION billing_reject_history_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Billing history is append-only';
                END;
                $$;
                CREATE TRIGGER billing_credit_history_immutable BEFORE UPDATE OR DELETE ON "CreditLedgerEntries"
                    FOR EACH ROW EXECUTE FUNCTION billing_reject_history_change();
                CREATE TRIGGER billing_payment_history_immutable BEFORE UPDATE OR DELETE ON "PaymentEvents"
                    FOR EACH ROW EXECUTE FUNCTION billing_reject_history_change();
                CREATE TRIGGER billing_plan_terms_immutable BEFORE UPDATE OR DELETE ON "PlanVersions"
                    FOR EACH ROW EXECUTE FUNCTION billing_reject_history_change();
                CREATE TRIGGER billing_package_terms_immutable BEFORE UPDATE OR DELETE ON "PackageVersions"
                    FOR EACH ROW EXECUTE FUNCTION billing_reject_history_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION billing_reject_history_change() CASCADE;");
            migrationBuilder.DropTable(
                name: "AutoTopUpPreferences");

            migrationBuilder.DropTable(
                name: "BillingPaymentMethods");

            migrationBuilder.DropTable(
                name: "CreditLedgerEntries");

            migrationBuilder.DropTable(
                name: "CreditReservations");

            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropTable(
                name: "SubscriberSubscriptions");

            migrationBuilder.DropTable(
                name: "CreditLots");

            migrationBuilder.DropTable(
                name: "CreditWallets");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "CheckoutOrders");

            migrationBuilder.DropTable(
                name: "PackageVersions");

            migrationBuilder.DropTable(
                name: "PlanVersions");
        }
    }
}
