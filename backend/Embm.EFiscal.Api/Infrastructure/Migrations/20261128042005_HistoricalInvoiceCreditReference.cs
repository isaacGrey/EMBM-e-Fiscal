using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalInvoiceCreditReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_Action",
                table: "FiscalDocuments");

            migrationBuilder.AddColumn<Guid>(
                name: "HistoricalInvoiceReferenceId",
                table: "FiscalDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AccountingReferences_SubscriberId_CompanyId_Id",
                table: "AccountingReferences",
                columns: new[] { "SubscriberId", "CompanyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_SubscriberId_CompanyId_HistoricalInvoiceRef~",
                table: "FiscalDocuments",
                columns: new[] { "SubscriberId", "CompanyId", "HistoricalInvoiceReferenceId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_Action",
                table: "FiscalDocuments",
                sql: "(\"Type\" = 'SalesOrder' AND \"Credits\" = 0 AND \"SourceDocumentId\" IS NULL AND \"ExternalOrderKey\" IS NULL AND \"HistoricalInvoiceReferenceId\" IS NULL) OR\n(\"Type\" = 'SalesInvoice' AND \"Credits\" = 1 AND \"HistoricalInvoiceReferenceId\" IS NULL AND (\"SourceDocumentId\" IS NULL OR \"ExternalOrderKey\" IS NULL)) OR\n(\"Type\" = 'CreditMemo' AND \"Credits\" = 1 AND \"ExternalOrderKey\" IS NULL AND\n  ((\"SourceDocumentId\" IS NOT NULL AND \"HistoricalInvoiceReferenceId\" IS NULL) OR\n   (\"DevelopmentFixture\" AND \"SourceDocumentId\" IS NULL AND \"HistoricalInvoiceReferenceId\" IS NOT NULL)))");

            migrationBuilder.AddForeignKey(
                name: "FK_FiscalDocuments_AccountingReferences_SubscriberId_CompanyId~",
                table: "FiscalDocuments",
                columns: new[] { "SubscriberId", "CompanyId", "HistoricalInvoiceReferenceId" },
                principalTable: "AccountingReferences",
                principalColumns: new[] { "SubscriberId", "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FiscalDocuments_AccountingReferences_SubscriberId_CompanyId~",
                table: "FiscalDocuments");

            migrationBuilder.DropIndex(
                name: "IX_FiscalDocuments_SubscriberId_CompanyId_HistoricalInvoiceRef~",
                table: "FiscalDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_Action",
                table: "FiscalDocuments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AccountingReferences_SubscriberId_CompanyId_Id",
                table: "AccountingReferences");

            migrationBuilder.DropColumn(
                name: "HistoricalInvoiceReferenceId",
                table: "FiscalDocuments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_Action",
                table: "FiscalDocuments",
                sql: "(\"Type\" = 'SalesOrder' AND \"Credits\" = 0 AND \"SourceDocumentId\" IS NULL AND \"ExternalOrderKey\" IS NULL) OR\n(\"Type\" = 'SalesInvoice' AND \"Credits\" = 1 AND (\"SourceDocumentId\" IS NULL OR \"ExternalOrderKey\" IS NULL)) OR\n(\"Type\" = 'CreditMemo' AND \"Credits\" = 1 AND \"SourceDocumentId\" IS NOT NULL AND \"ExternalOrderKey\" IS NULL)");
        }
    }
}
