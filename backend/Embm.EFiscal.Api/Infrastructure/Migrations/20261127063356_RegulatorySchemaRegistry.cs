using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegulatorySchemaRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegulatorySchemaVersions",
                columns: table => new
                {
                    Version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Classification = table.Column<string>(type: "text", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    DocumentTypes = table.Column<string[]>(type: "text[]", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Verification = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Reviewer = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatorySchemaVersions", x => x.Version);
                    table.CheckConstraint("CK_RegulatorySchema_Classification", "\"Classification\" IN ('InternalCanonical','DevelopmentSimulation','FutureOfficialCertification','FutureOfficialProduction')");
                    table.CheckConstraint("CK_RegulatorySchema_Integrity", "length(\"Sha256\") = 64 AND (\"EffectiveUntil\" IS NULL OR \"EffectiveUntil\" > \"EffectiveFrom\")");
                    table.CheckConstraint("CK_RegulatorySchema_Use", "(\"Classification\" = 'InternalCanonical' AND \"Environment\" = 'Internal' AND \"Verification\" = 'InternalOnly') OR\n(\"Classification\" = 'DevelopmentSimulation' AND \"Environment\" = 'Development' AND \"Verification\" = 'InternalOnly') OR\n(\"Classification\" IN ('FutureOfficialCertification','FutureOfficialProduction') AND \"Environment\" = 'Disabled' AND \"Verification\" = 'Unverified')");
                });

            migrationBuilder.InsertData(
                table: "RegulatorySchemaVersions",
                columns: new[] { "Version", "Classification", "Content", "DocumentTypes", "EffectiveFrom", "EffectiveUntil", "Environment", "Name", "Reviewer", "Sha256", "Source", "Verification" },
                values: new object[,]
                {
                    { "embm-invoice-v1", "InternalCanonical", "{\"properties\":{\"documentedAuthorizationReferences\":{\"type\":[\"array\",\"null\"]},\"evidenceLimitations\":{\"items\":{\"type\":\"string\"},\"type\":\"array\"},\"fiscalSnapshot\":{\"properties\":{\"buyer\":{\"required\":[\"customerId\",\"name\",\"tin\",\"classification\"],\"type\":\"object\"},\"document\":{\"properties\":{\"type\":{\"enum\":[\"SalesInvoice\",\"CreditMemo\"]}},\"required\":[\"id\",\"number\",\"type\",\"date\",\"committedAt\",\"currency\",\"exchangeRate\",\"precision\",\"seriesId\",\"numbering\",\"originalInvoiceId\"],\"type\":\"object\"},\"lines\":{\"items\":{\"required\":[\"id\",\"position\",\"itemId\",\"unitId\",\"quantity\",\"unitPrice\",\"total\",\"taxClassification\",\"financialTerms\"],\"type\":\"object\"},\"minItems\":1,\"type\":\"array\"},\"seller\":{\"required\":[\"companyId\",\"branchId\",\"name\",\"branchCode\",\"tin\",\"rdo\"],\"type\":\"object\"},\"totals\":{\"required\":[\"grossSales\",\"commercialDiscount\",\"statutoryDiscount\",\"vatableSales\",\"vatExemptSales\",\"zeroRatedSales\",\"nonVatSales\",\"vat\",\"incomeWithholding\",\"vatWithholding\",\"total\",\"amountDue\",\"rounding\"],\"type\":\"object\"}},\"required\":[\"document\",\"seller\",\"buyer\",\"lines\",\"totals\"],\"type\":\"object\"},\"format\":{\"const\":\"InternalCanonical\"},\"originalFinancialSnapshotSha256\":{\"pattern\":\"^[A-F0-9]{64}$\",\"type\":\"string\"},\"schemaVersion\":{\"const\":\"embm-invoice-v1\"}},\"required\":[\"schemaVersion\",\"format\",\"fiscalSnapshot\",\"originalFinancialSnapshotSha256\",\"documentedAuthorizationReferences\",\"evidenceLimitations\"],\"type\":\"object\"}", new[] { "SalesInvoice", "CreditMemo" }, new DateOnly(2026, 11, 27), null, "Internal", "EMBM eFiscal canonical invoice", null, "B445F1ACBB90D4961FB61BCE74C61D633CC7EBB9F635AA08DABAC3FAEFF0BBDC", "EMBM eFiscal software release", "InternalOnly" },
                    { "embm-reporting-simulation-v1", "DevelopmentSimulation", "{\"properties\":{\"governmentStatus\":{\"const\":\"NotSubmitted\"},\"id\":{\"format\":\"uuid\",\"type\":\"string\"},\"outcome\":{\"const\":\"SimulationValidated\"}},\"required\":[\"id\",\"outcome\",\"governmentStatus\"],\"type\":\"object\"}", new[] { "SalesInvoice", "CreditMemo" }, new DateOnly(2026, 11, 27), null, "Development", "EMBM eFiscal local reporting integrity simulation", null, "3CA1A439EB95ADFD46571F753C1ADBC2AFF92AAF1660AA38A71CA380E790D00D", "EMBM eFiscal software release", "InternalOnly" }
                });
            migrationBuilder.Sql("""
                CREATE TRIGGER regulatory_schema_immutable BEFORE UPDATE OR DELETE ON "RegulatorySchemaVersions"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegulatorySchemaVersions");
        }
    }
}
