using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class TaxpayerExport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; init; }
    public Guid CompanyId { get; init; }
    public Guid ActorId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly Through { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public int DocumentCount { get; init; }
    public string Sha256 { get; init; } = "";
    public byte[] Archive { get; init; } = [];
}

internal static class TaxpayerPortability
{
    private const int DocumentLimit = 100;
    private const int ByteLimit = 24 * 1024 * 1024;
    private static readonly string[] Grants = ["Invoice.View", "CreditMemo.View", "SalesOrder.View", "Reports.View", Coverage.View, Coverage.Export];
    internal sealed record Request(DateOnly From, DateOnly Through);
    private sealed record Entry(string Path, int Bytes, string Sha256, int Records);
    private sealed class ExportLimit : Exception;

    internal static void Configure(ModelBuilder model)
    {
        model.Entity<TaxpayerExport>(e =>
        {
            e.ToTable("TaxpayerExports");
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.GeneratedAt });
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, Id = x.CompanyId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }

    internal static void MapTaxpayerPortability(this WebApplication app)
    {
        var group = app.MapGroup("/api/workspace/{subscriber:guid}/companies/{company:guid}/portability")
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect).WithTags("Taxpayer data portability");
        group.MapGet("/", History);
        group.MapPost("/", Generate).RequireRateLimiting("connector-operation");
        group.MapGet("/{id:guid}/archive", Download).RequireRateLimiting("connector-operation");
    }

    private static async Task<bool> Allowed(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
        await DocumentEndpoints.Access(subscriber, company, DocumentEndpoints.ExportPermission, http, db, ct, existingRecords: true) is { } access &&
        Grants.All(access.Grant.Grants.Contains);

    private static IQueryable<TaxpayerExport> Scoped(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.Set<TaxpayerExport>().AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    private static async Task<IResult> History(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!await Allowed(subscriber, company, http, db, ct)) return TenantAccess.Missing();
        var items = await Scoped(db, subscriber, company).OrderByDescending(x => x.GeneratedAt).ThenBy(x => x.Id).Take(100)
            .Select(x => new { x.Id, x.From, x.Through, x.DocumentCount, x.GeneratedAt, x.Sha256 }).ToArrayAsync(ct);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> Download(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!await Allowed(subscriber, company, http, db, ct) ||
            await Scoped(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == id, ct) is not { } row) return TenantAccess.Missing();
        if (Hash(row.Archive) != row.Sha256) return ApiSupport.Problem(409, "Export integrity verification failed.");
        db.Audit("Portability.Downloaded", http.UserId(), subscriber, id);
        await db.SaveChangesAsync(ct);
        http.Response.Headers.CacheControl = "private, no-store";
        return Results.File(row.Archive, "application/zip", $"efiscal-export-{id:N}.zip");
    }

    private static async Task<IResult> Generate(Guid subscriber, Guid company, Request input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!await Allowed(subscriber, company, http, db, ct)) return TenantAccess.Missing();
        if (input.From.Year < 2000 || input.Through < input.From || input.Through.DayNumber - input.From.DayNumber > 366)
            return ApiSupport.Invalid("period", "Choose an inclusive period of up to one year.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (!await Allowed(subscriber, company, http, db, ct)) return TenantAccess.Missing();
        var documents = db.FiscalDocuments.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.Date >= input.From && x.Date <= input.Through);
        var ids = await documents.OrderBy(x => x.Id).Select(x => x.Id).Take(DocumentLimit + 1).ToArrayAsync(ct);
        if (ids.Length > DocumentLimit) return ApiSupport.Invalid("period", $"Narrow this export to {DocumentLimit} documents or fewer.");

        var generatedAt = DateTimeOffset.UtcNow;
        byte[] bytes;
        try
        {
            bytes = await Archive(db, http, subscriber, company, input, generatedAt, ids, documents, ct);
        }
        catch (ExportLimit) { return ApiSupport.Invalid("period", "Narrow the period to reduce the archive size."); }
        catch (CryptographicException) { return ApiSupport.Problem(409, "Stored evidence could not be verified for export."); }

        var row = new TaxpayerExport { SubscriberId = subscriber, CompanyId = company, ActorId = http.UserId(),
            From = input.From, Through = input.Through, GeneratedAt = generatedAt, DocumentCount = ids.Length, Archive = bytes, Sha256 = Hash(bytes) };
        db.Add(row);
        db.Audit("Portability.Generated", row.ActorId, subscriber, row.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Created($"portability/{row.Id}/archive", new { row.Id, row.DocumentCount, row.GeneratedAt, row.Sha256 });
    }

    private static async Task<byte[]> Archive(EfiscalDbContext db, HttpContext http, Guid subscriber, Guid company, Request period,
        DateTimeOffset generatedAt, Guid[] ids, IQueryable<FiscalDocument> documents, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entries = new List<Entry>();
            var totalBytes = 0;
            var docs = await documents.Include(x => x.Snapshot).Include(x => x.Lines).OrderBy(x => x.Id).ToArrayAsync(ct);
            AddRows("documents.json", docs.Select(d => new
            {
                document = DocumentDtos.Receipt(d), d.HistoricalInvoiceReferenceId, d.ExternalOrderKey,
                totals = DocumentDtos.Totals(d), lines = d.Lines.OrderBy(x => x.Position).Select(DocumentDtos.Line),
                d.Snapshot.Parties, d.Snapshot.Header, d.Snapshot.Numbering
            }).ToArray());
            var files = db.IssuedDocumentFiles.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && ids.Contains(x.DocumentId));
            if (await files.SumAsync(x => (long)x.Content.Length, ct) > ByteLimit - totalBytes) throw new ExportLimit();
            foreach (var file in await files.OrderBy(x => x.DocumentId).ToArrayAsync(ct))
            {
                if (!Hash(file.Content).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException();
                Add($"pdf/{file.DocumentId:N}.pdf", file.Content, 1);
            }
            var payloads = await db.ReportingPreparations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
                ids.Contains(x.DocumentId)).OrderBy(x => x.Id).ToArrayAsync(ct);
            foreach (var payload in payloads)
            {
                var content = Encoding.UTF8.GetBytes(payload.Content);
                if (!Hash(content).Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException();
                Add($"reporting/{payload.DocumentId:N}/{payload.Id:N}.json", content, 1);
            }
            AddRows("reporting-index.json", payloads.Select(x => new { x.Id, x.DocumentId, x.SchemaVersion, x.Sha256, x.CreatedAt }).ToArray());
            var preparations = payloads.Select(x => x.Id).ToArray();
            AddRows("reporting-simulations.json", await db.ReportingSimulations.AsNoTracking().Where(x => x.SubscriberId == subscriber &&
                x.CompanyId == company && preparations.Contains(x.PreparationId)).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.PreparationId, x.Outcome, x.CreatedAt }).ToArrayAsync(ct));
            AddRows("accounting-identities.json", await db.ExternalDocumentIdentities.AsNoTracking().Where(x => x.SubscriberId == subscriber &&
                x.CompanyId == company && ids.Contains(x.DocumentId)).OrderBy(x => x.DocumentId).ToArrayAsync(ct));
            var operations = await db.Set<ReportingOperation>().AsNoTracking().Where(x => x.SubscriberId == subscriber &&
                x.CompanyId == company && ids.Contains(x.DocumentId)).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.DocumentId, x.BranchId, x.ConnectorVersion, x.Mode, x.SchemaVersion, x.MappingVersion,
                    x.SourceHash, x.PayloadHash, x.State, x.Failure, x.At }).ToArrayAsync(ct);
            AddRows("reporting-operations.json", operations);
            var operationIds = operations.Select(x => x.Id).ToArray();
            AddRows("reporting-attempts.json", await db.Set<ReportingOperationAttempt>().AsNoTracking()
                .Where(x => operationIds.Contains(x.OperationId)).OrderBy(x => x.OperationId).ThenBy(x => x.Sequence).ToArrayAsync(ct));
            var checks = await db.ReconciliationChecks.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
                (x.DocumentId != null && ids.Contains(x.DocumentId.Value) ||
                    x.DocumentId == null && x.BusinessDate >= period.From && x.BusinessDate <= period.Through))
                .OrderBy(x => x.Id).Take(5001).ToArrayAsync(ct);
            if (checks.Length > 5000) throw new ExportLimit();
            var engine = http.RequestServices.GetService<ReconciliationEngine>();
            AddRows("reconciliation.json", checks.Select(x => new
            {
                x.Id, x.DocumentId, x.ConnectionId, x.ObservationId, x.CoverageId, x.Type, x.BusinessDate, x.At, x.Status, x.Retrieval,
                x.Completeness, x.Coverage, x.ExpectedTotal, x.ActualTotal, x.Currency, x.Categories,
                comparison = engine?.ReadCells(x)
            }).ToArray());
            var observationIds = checks.Where(x => x.ObservationId != null).Select(x => x.ObservationId!.Value).Distinct().ToArray();
            var observations = await db.AccountingObservations.AsNoTracking().Where(x => x.SubscriberId == subscriber &&
                x.CompanyId == company && observationIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(ct);
            AddRows("accounting-evidence.json", observations.Select(x => new
            {
                x.Id, x.ConnectionId, x.ConnectorVersionId, x.Account, x.ExternalCompany, x.ExternalId, x.Number, x.BusinessDate,
                x.Type, x.Hash, x.RetrievedAt, values = engine?.ReadSnapshot(x)
            }).ToArray());
            var checkIds = checks.Select(x => x.Id).ToArray();
            var findings = await db.ReconciliationFindings.AsNoTracking().Where(x => x.SubscriberId == subscriber &&
                x.CompanyId == company && checkIds.Contains(x.CheckId)).OrderBy(x => x.Id).Take(5001).ToArrayAsync(ct);
            if (findings.Length > 5000) throw new ExportLimit();
            AddRows("reconciliation-findings.json", findings);
            var findingIds = findings.Select(x => x.Id).ToArray();
            var reviews = await db.ReconciliationReviews.AsNoTracking().Where(x => x.SubscriberId == subscriber &&
                x.CompanyId == company && findingIds.Contains(x.FindingId)).OrderBy(x => x.Id).Take(5001).ToArrayAsync(ct);
            if (reviews.Length > 5000) throw new ExportLimit();
            var protector = engine?.Evidence(subscriber, company);
            AddRows("reconciliation-reviews.json", reviews.Select(x => new
            {
                x.Id, x.FindingId, x.Reviewer, x.Actor, x.VerificationId, x.Status, x.At,
                note = x.ProtectedNote is null ? null : protector?.Unprotect(x.ProtectedNote)
            }).ToArray());
            var coverageIds = checks.Where(x => x.CoverageId != null).Select(x => x.CoverageId!.Value).Distinct().ToArray();
            AddRows("reconciliation-coverage.json", await Coverage.For(db, subscriber, company).AsNoTracking()
                .Where(x => coverageIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(ct));
            var targets = ids.Concat(preparations).Concat(operationIds).Concat(checkIds).Append(company).ToArray();
            var audit = await db.SecurityAudits.AsNoTracking().Where(x => x.SubscriberId == subscriber &&
                x.TargetId != null && targets.Contains(x.TargetId.Value)).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ActorId, x.TargetId, x.Action, x.At }).Take(5001).ToArrayAsync(ct);
            if (audit.Length > 5000) throw new ExportLimit();
            AddRows("audit.json", audit);
            Add("manifest.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                format = "embm-taxpayer-portability-v1", companyId = company, period.From, period.Through, generatedAt,
                documentCount = ids.Length, governmentStatus = "NotSubmitted", files = entries.ToArray()
            }, DocumentRules.Json), 1);

            void AddRows<T>(string path, T[] rows) => Add(path, JsonSerializer.SerializeToUtf8Bytes(rows, DocumentRules.Json), rows.Length);
            void Add(string path, byte[] data, int records)
            {
                totalBytes += data.Length;
                if (totalBytes > ByteLimit) throw new ExportLimit();
                using var target = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
                target.Write(data);
                entries.Add(new(path, data.Length, Hash(data), records));
            }
        }
        return stream.ToArray();
    }

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
