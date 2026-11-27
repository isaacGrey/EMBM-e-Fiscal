using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

public sealed class ReportingPreparation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string SchemaVersion { get; set; } = "embm-preparation-v1";
    public string Content { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class ReportingSimulation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PreparationId { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Outcome { get; set; } = "SimulationValidated";
}

internal static class ReportingGateway
{
    public static void Configure(ModelBuilder b)
    {
        var preparation = b.Entity<ReportingPreparation>();
        preparation.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        preparation.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentId, x.SchemaVersion }).IsUnique();
        preparation.HasOne<FiscalDocument>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        preparation.Property(x => x.SchemaVersion).HasMaxLength(40);
        preparation.Property(x => x.Sha256).HasMaxLength(64);
        var simulation = b.Entity<ReportingSimulation>();
        simulation.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.PreparationId }).IsUnique();
        simulation.HasOne<ReportingPreparation>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.PreparationId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        simulation.Property(x => x.Outcome).HasMaxLength(40);
        simulation.ToTable(t => t.HasCheckConstraint("CK_ReportingSimulation_Outcome", "\"Outcome\" = 'SimulationValidated'"));
    }

    public static void MapReporting(this WebApplication app)
    {
        var group = app.MapGroup("/api/workspace/{subscriber:guid}/companies/{company:guid}/reporting")
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect).WithTags("Reporting preparation");
        group.MapGet("/", Register);
        group.MapGet("/readiness", Readiness);
        group.MapGet("/{document:guid}", Detail);
        group.MapPost("/{document:guid}/prepare", Prepare);
        if (app.Environment.IsDevelopment()) group.MapPost("/{document:guid}/simulate", Simulate);
    }

    private static async Task<IResult> Readiness(Guid subscriber, Guid company, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, "Reports.View", http, db, ct) is not { } access)
            return TenantAccess.Missing();

        var taxpayer = await db.TaxpayerProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company, ct);
        var branches = await db.Branches.AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company)
            .OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name, x.Type }).ToArrayAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Manila").DateTime);
        var assignments = await db.RdoAssignments.AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.EffectiveFrom <= today &&
                (x.EffectiveUntil == null || x.EffectiveUntil > today))
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new { x.BranchId, x.Code, x.RegistrationStatus }).ToArrayAsync(ct);
        var evidenceCount = await db.RegistrationEvidence.CountAsync(x => x.SubscriberId == subscriber && x.CompanyId == company, ct);

        return Results.Ok(new
        {
            access.Company.RegisteredName,
            taxpayerVerification = taxpayer?.VerificationStatus.ToString() ?? "NotVerified",
            registrationEvidenceCount = evidenceCount,
            companyRdo = assignments.FirstOrDefault(x => x.BranchId == null),
            branches = branches.Select(branch => new { branch.Code, branch.Name, branch.Type,
                rdo = assignments.FirstOrDefault(x => x.BranchId == branch.Id) }),
            productionIssuanceEnabled = false,
            liveTransmissionEnabled = false
        });
    }

    private static IQueryable<ReportingPreparation> Preparations(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.ReportingPreparations.Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    private static IQueryable<FiscalDocument> Documents(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.FiscalDocuments.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            (x.Type == "SalesInvoice" || x.Type == "CreditMemo"));

    private static async Task<IResult> Register(Guid subscriber, Guid company, int? page, HttpContext http,
        EfiscalDbContext db, IWebHostEnvironment environment, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, "Reports.View", http, db, ct) is not { } access)
            return TenantAccess.Missing();
        if (page is < 1 or > 10000) return ApiSupport.Invalid("page", "Use a page between 1 and 10000.");
        var query = Documents(db, subscriber, company);
        var preparations = Preparations(db, subscriber, company);
        var currentPage = page ?? 1;
        var items = await query.OrderByDescending(x => x.CommittedAt).ThenBy(x => x.Id).Skip((currentPage - 1) * 25).Take(25)
            .Select(x => new { x.Id, x.Type, x.Number, x.Date, x.DevelopmentFixture,
                prepared = preparations.Any(p => p.DocumentId == x.Id),
                simulated = db.ReportingSimulations.Any(s => s.SubscriberId == subscriber && s.CompanyId == company &&
                    preparations.Any(p => p.Id == s.PreparationId && p.DocumentId == x.Id)) }).ToArrayAsync(ct);
        return Results.Ok(new { mode = "PreparationOnly", liveTransmissionEnabled = false, officialApiAvailable = false,
            schemaVersion = "embm-preparation-v1", canPrepare = access.Grant.Grants.Contains(Permissions.CompanyManage),
            canSimulate = environment.IsDevelopment() && access.Grant.Subscriber.DevelopmentFixture && access.Grant.Grants.Contains(Permissions.CompanyManage),
            page = currentPage, total = await query.CountAsync(ct), items });
    }

    private static async Task<IResult> Detail(Guid subscriber, Guid company, Guid document, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, "Reports.View", http, db, ct) is null ||
            !await Documents(db, subscriber, company).AnyAsync(x => x.Id == document, ct)) return TenantAccess.Missing();
        var preparation = await Preparations(db, subscriber, company).AsNoTracking().SingleOrDefaultAsync(x => x.DocumentId == document, ct);
        if (preparation is null) return TenantAccess.Missing();
        var simulation = await db.ReportingSimulations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.PreparationId == preparation.Id).Select(x => new { x.Outcome, x.CreatedAt }).SingleOrDefaultAsync(ct);
        return Results.Ok(new { preparation.Id, preparation.DocumentId, preparation.SchemaVersion, preparation.CreatedAt,
            preparation.Sha256, content = JsonSerializer.Deserialize<JsonElement>(preparation.Content), simulation,
            governmentStatus = "NotSubmitted", liveTransmissionEnabled = false });
    }

    private static async Task<IResult> Prepare(Guid subscriber, Guid company, Guid document, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct) is not { } access ||
            !access.Grant.Grants.Contains("Reports.View")) return TenantAccess.Missing();
        var doc = await Documents(db, subscriber, company).Include(x => x.Lines).Include(x => x.Snapshot).SingleOrDefaultAsync(x => x.Id == document, ct);
        if (doc is null) return TenantAccess.Missing();
        var row = await Preparations(db, subscriber, company).SingleOrDefaultAsync(x => x.DocumentId == document, ct);
        if (row is null)
        {
            // Internal evidence envelope, deliberately NOT an official government payload.
            var content = JsonSerializer.Serialize(new { schemaVersion = "embm-preparation-v1", purpose = "InternalPreparationOnly",
                governmentStatus = "NotSubmitted", doc.DevelopmentFixture, doc.Id, doc.Type, doc.Number, doc.Date, doc.Currency,
                exchangeRate = DocumentRules.Money(doc.ExchangeRate), doc.SourceDocumentId, totals = DocumentDtos.Totals(doc),
                parties = doc.Snapshot.Parties, header = doc.Snapshot.Header, numbering = doc.Snapshot.Numbering,
                lines = doc.Lines.OrderBy(x => x.Position).Select(DocumentDtos.Line) },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            row = new ReportingPreparation { SubscriberId = subscriber, CompanyId = company, DocumentId = document,
                ActorId = http.UserId(), CreatedAt = DateTimeOffset.UtcNow, Content = content,
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))) };
            db.ReportingPreparations.Add(row);
            db.Audit("Reporting.Prepared", http.UserId(), subscriber, document);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Results.Ok(new { row.Id, row.Sha256, governmentStatus = "NotSubmitted" });
    }

    private static async Task<IResult> Simulate(Guid subscriber, Guid company, Guid document, HttpContext http, EfiscalDbContext db,
        IWebHostEnvironment environment, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (!environment.IsDevelopment() || await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct) is not { } access ||
            !access.Grant.Subscriber.DevelopmentFixture || !access.Grant.Grants.Contains("Reports.View")) return TenantAccess.Missing();
        if (!await Documents(db, subscriber, company).AnyAsync(x => x.Id == document && x.DevelopmentFixture, ct)) return TenantAccess.Missing();
        var preparation = await Preparations(db, subscriber, company).SingleOrDefaultAsync(x => x.DocumentId == document, ct);
        if (preparation is null) return ApiSupport.Problem(409, "Prepare the document before running a local simulation.");
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preparation.Content))) != preparation.Sha256)
            return ApiSupport.Problem(409, "Preparation integrity validation failed.");
        var row = await db.ReportingSimulations.SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.PreparationId == preparation.Id, ct);
        if (row is null)
        {
            row = new ReportingSimulation { SubscriberId = subscriber, CompanyId = company, PreparationId = preparation.Id,
                ActorId = http.UserId(), CreatedAt = DateTimeOffset.UtcNow };
            db.ReportingSimulations.Add(row);
            db.Audit("Reporting.SimulationValidated", http.UserId(), subscriber, document);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Results.Ok(new { row.Id, row.Outcome, governmentStatus = "NotSubmitted" });
    }
}
