using System.Text.Json;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Documents;

internal static class DocumentModel
{
    public static void Configure(ModelBuilder b)
    {
        var document = b.Entity<FiscalDocument>();
        document.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        document.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Type, x.Number }).IsUnique();
        document.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Type, x.Date, x.Id });
        document.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.SourceDocumentId });
        document.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        document.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.BranchId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        document.HasOne<MasterRecord>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.CustomerId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        document.HasOne<DocumentNumberSeries>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.SeriesId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        document.HasOne(x => x.SourceDocument).WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.SourceDocumentId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        document.Property(x => x.Type).HasMaxLength(16);
        document.Property(x => x.Number).HasMaxLength(42);
        document.Property(x => x.Currency).HasMaxLength(3);
        document.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Documents_Action", """
                ("Type" = 'SalesOrder' AND "Credits" = 0 AND "SourceDocumentId" IS NULL AND "ExternalOrderKey" IS NULL) OR
                ("Type" = 'SalesInvoice' AND "Credits" = 1 AND ("SourceDocumentId" IS NULL OR "ExternalOrderKey" IS NULL)) OR
                ("Type" = 'CreditMemo' AND "Credits" = 1 AND "SourceDocumentId" IS NOT NULL AND "ExternalOrderKey" IS NULL)
                """);
            t.HasCheckConstraint("CK_Documents_Amounts", """
                "LineCount" BETWEEN 1 AND 100 AND "Precision" BETWEEN 0 AND 6 AND "ExchangeRate" > 0
                AND "Total" >= 0 AND "AmountDue" = "Total" - "IncomeWithholding" - "VatWithholding"
                AND "AmountDue" >= 0
                """);
        });
        var line = b.Entity<FiscalDocumentLine>();
        line.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        line.HasOne<FiscalDocument>().WithMany(x => x.Lines).HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        line.HasOne<MasterRecord>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ItemId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        line.HasOne<MasterRecord>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.UnitId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        line.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentId, x.Position }).IsUnique();
        line.Property(x => x.Terms).HasColumnType("jsonb");
        line.ToTable(t => t.HasCheckConstraint("CK_DocumentLines_Amounts", """
            "Position" BETWEEN 1 AND 100 AND "Quantity" >= 0 AND "BaseFactor" > 0
            AND "Total" = "VatableSales" + "ExemptSales" + "ZeroRatedSales" + "NonVatSales" + "Vat" AND "Total" >= 0
            """));

        var snapshot = b.Entity<FiscalDocumentSnapshot>();
        snapshot.HasKey(x => new { x.SubscriberId, x.CompanyId, x.DocumentId });
        snapshot.HasOne<FiscalDocument>().WithOne(x => x.Snapshot).HasForeignKey<FiscalDocumentSnapshot>(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
            .HasPrincipalKey<FiscalDocument>(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var field in new[] { "Parties", "Header", "Numbering", "Templates", "Delivery" })
            snapshot.Property<JsonElement>(field).HasColumnType("jsonb");

        var series = b.Entity<DocumentNumberSeries>();
        series.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        series.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Type, x.BranchId, x.Revision }).IsUnique().AreNullsDistinct(false);
        series.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        series.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.BranchId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        series.Property(x => x.Prefix).HasMaxLength(24);
        series.ToTable(t => t.HasCheckConstraint("CK_DocumentSeries_Sequence", "\"Digits\" BETWEEN 3 AND 12 AND \"InitialNumber\" > 0 AND \"NextNumber\" >= \"InitialNumber\""));

        var operation = b.Entity<DocumentCommitment>();
        operation.HasKey(x => new { x.SubscriberId, x.CompanyId, x.ActorId, x.Key });
        operation.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentId }).IsUnique();
        operation.Property(x => x.Key).HasMaxLength(80);
        operation.Property(x => x.Digest).HasMaxLength(64);
        operation.HasOne<FiscalDocument>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var allocation = b.Entity<DocumentAllocation>();
        allocation.HasKey(x => new { x.SubscriberId, x.CompanyId, x.LineId });
        allocation.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.SourceLineId, x.Kind });
        allocation.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.ExternalOrderKey, x.ExternalLineKey });
        allocation.HasOne<FiscalDocumentLine>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.LineId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        allocation.HasOne<FiscalDocumentLine>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.SourceLineId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        allocation.ToTable(t => t.HasCheckConstraint("CK_DocumentAllocation_Source", """
            ("SourceLineId" IS NOT NULL AND "ExternalOrderKey" IS NULL AND "ExternalLineKey" IS NULL) OR
            ("SourceLineId" IS NULL AND "ExternalOrderKey" IS NOT NULL AND "ExternalLineKey" IS NOT NULL)
            """));

        var processing = b.Entity<DocumentProcessing>();
        processing.HasKey(x => new { x.SubscriberId, x.CompanyId, x.DocumentId });
        processing.HasOne<FiscalDocument>().WithOne(x => x.Processing).HasForeignKey<DocumentProcessing>(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
            .HasPrincipalKey<FiscalDocument>(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var outbox = b.Entity<DocumentOutboxMessage>();
        outbox.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.DocumentId, x.Id });
        outbox.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentId, x.Kind, x.Operation }).IsUnique();
        outbox.HasIndex(x => new { x.Kind, x.State, x.AvailableAt });
        outbox.HasOne<FiscalDocument>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var file = b.Entity<IssuedDocumentFile>();
        file.HasKey(x => new { x.SubscriberId, x.CompanyId, x.DocumentId });
        file.HasOne<FiscalDocument>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var attempt = b.Entity<DocumentDeliveryAttempt>();
        attempt.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentId, x.JobId, x.Attempt }).IsUnique();
        attempt.HasOne<DocumentOutboxMessage>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, x.DocumentId, Id = x.JobId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.DocumentId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        foreach (var type in new[] { typeof(FiscalDocument), typeof(FiscalDocumentLine), typeof(DocumentAllocation) })
            foreach (var prop in b.Entity(type).Metadata.GetProperties().Where(p => p.ClrType == typeof(decimal)))
                b.Entity(type).Property(prop.Name).HasPrecision(28, 8);
    }
}

