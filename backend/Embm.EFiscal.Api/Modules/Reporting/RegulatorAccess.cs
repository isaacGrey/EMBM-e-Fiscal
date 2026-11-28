using System.Data;
using System.Text.RegularExpressions;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class RegulatorGrant
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public required string Scope { get; init; }
    public Guid? SubscriberId { get; init; }
    public Guid? CompanyId { get; init; }
    public Guid? BranchId { get; init; }
    public string? RdoCode { get; init; }
    public required string[] Categories { get; init; }
    public bool CanExport { get; init; }
    public required string Reference { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class RegulatorReadAudit
{
    public long Id { get; init; }
    public Guid GrantId { get; init; }
    public Guid UserId { get; init; }
    public Guid SubscriberId { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? BranchId { get; init; }
    public string? RdoCode { get; init; }
    public required string Category { get; init; }
    public DateOnly From { get; init; }
    public DateOnly Through { get; init; }
    public int Page { get; init; }
    public int Count { get; init; }
    public bool Export { get; init; }
    public DateTimeOffset At { get; init; }
}

internal static partial class RegulatorAccess
{
    internal const string Setting = "Identity:EnableLocalRegulatorLogin";
    internal static readonly string[] Categories = ["documents", "tax-summary", "reconciliation", "reporting", "audit"];
    private const int PageSize = 50;
    private sealed record Batch(Array Items, bool HasMore);

    internal static bool Enabled(IWebHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>(Setting);

    internal static void Configure(ModelBuilder model)
    {
        model.Entity<RegulatorGrant>(e =>
        {
            e.ToTable("RegulatorGrants", table =>
            {
                table.HasCheckConstraint("CK_RegulatorGrant_Period", """ "ExpiresAt" > "StartsAt" """);
                table.HasCheckConstraint("CK_RegulatorGrant_Scope", """
                    ("Scope" = 'Company' AND "SubscriberId" IS NOT NULL AND "CompanyId" IS NOT NULL AND "RdoCode" IS NULL) OR
                    ("Scope" = 'Rdo' AND "SubscriberId" IS NULL AND "CompanyId" IS NULL AND "BranchId" IS NULL AND "RdoCode" IS NOT NULL) OR
                    ("Scope" = 'Nationwide' AND "SubscriberId" IS NULL AND "CompanyId" IS NULL AND "BranchId" IS NULL AND "RdoCode" IS NULL)
                    """);
            });
            e.HasIndex(x => new { x.UserId, x.ExpiresAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, x.BranchId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RdoReference>().WithMany().HasForeignKey(x => x.RdoCode).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RegulatorReadAudit>(e =>
        {
            e.ToTable("RegulatorReadAudits");
            e.HasOne<RegulatorGrant>().WithMany().HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    internal static void MapRegulatorAccess(this WebApplication app)
    {
        if (!Enabled(app.Environment, app.Configuration)) return;
        var group = app.MapGroup("/api/bir").RequireAuthorization("regulator")
            .AddEndpointFilter(ApiSupport.Protect).RequireRateLimiting("connector-operation").WithTags("Development regulator");
        group.MapGet("/grants", Grants);
        group.MapGet("/records/{category}", Read);
    }

    private static IQueryable<RegulatorGrant> Active(EfiscalDbContext db, Guid user, DateTimeOffset now) =>
        db.Set<RegulatorGrant>().AsNoTracking().Where(x =>
            x.UserId == user && x.RevokedAt == null && x.StartsAt <= now && x.ExpiresAt > now);

    private static async Task<IResult> Grants(HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var items = await Active(db, http.UserId(), DateTimeOffset.UtcNow).OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Scope, x.SubscriberId, x.CompanyId, x.BranchId, x.RdoCode, x.Categories, x.CanExport, x.ExpiresAt })
            .ToArrayAsync(ct);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> Read(string category, Guid grantId, Guid subscriberId, Guid companyId, Guid? branchId,
        DateOnly from, DateOnly through, string? rdo, int? page, bool? export, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!Categories.Contains(category)) return Results.NotFound();
        if (from.Year < 2000 || through < from || through.DayNumber - from.DayNumber > 366 || page is < 1 or > 10000 ||
            rdo is not null && !ValidRdoCode().IsMatch(rdo))
            return ApiSupport.Invalid("scope", "Use an inclusive range of up to one year and valid page and RDO filters.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var now = DateTimeOffset.UtcNow;
        var user = http.UserId();
        var grant = await Active(db, user, now).SingleOrDefaultAsync(x => x.Id == grantId, ct);
        if (grant is null || !grant.Categories.Contains(category) || export == true && !grant.CanExport ||
            grant.Scope == "Company" && (grant.SubscriberId != subscriberId || grant.CompanyId != companyId) ||
            grant.BranchId.HasValue && branchId.HasValue && grant.BranchId != branchId ||
            grant.RdoCode is not null && rdo is not null && grant.RdoCode != rdo)
            return Results.NotFound();

        var branch = grant.BranchId ?? branchId;
        var district = grant.RdoCode ?? rdo;
        var docs = db.FiscalDocuments.AsNoTracking().Where(x => x.SubscriberId == subscriberId && x.CompanyId == companyId &&
            x.DevelopmentFixture && db.Subscribers.Any(s => s.Id == x.SubscriberId && s.DevelopmentFixture) &&
            db.TaxpayerProfiles.Any(t => t.SubscriberId == x.SubscriberId && t.CompanyId == x.CompanyId) &&
            (x.Type == "SalesInvoice" || x.Type == "CreditMemo") && x.Date >= from && x.Date <= through);
        if (branch.HasValue) docs = docs.Where(x => x.BranchId == branch);
        if (district is not null)
        {
            var assignments = db.RdoAssignments.Where(x => x.SubscriberId == subscriberId && x.CompanyId == companyId);
            docs = docs.Where(doc => assignments.Where(a => (a.BranchId == null || a.BranchId == doc.BranchId) &&
                    a.EffectiveFrom <= doc.Date && (a.EffectiveUntil == null || a.EffectiveUntil > doc.Date))
                .OrderByDescending(a => a.BranchId.HasValue).ThenByDescending(a => a.EffectiveFrom).ThenBy(a => a.Id)
                .Select(a => a.Code).FirstOrDefault() == district);
        }

        var currentPage = page ?? 1;
        var result = await ReadPage(db, category, docs, currentPage, ct);
        db.Add(new RegulatorReadAudit { GrantId = grant.Id, UserId = user, SubscriberId = subscriberId, CompanyId = companyId,
            BranchId = branch, RdoCode = district, Category = category, From = from, Through = through, Page = currentPage,
            Count = result.Items.Length, Export = export == true, At = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var response = new { mode = "DevelopmentFixture", category, page = currentPage, result.HasMore,
            scope = new { grantId, subscriberId, companyId, branchId = branch, rdoCode = district, from, through },
            result.Items };
        return export == true
            ? Results.File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(response, DocumentRules.Json),
                "application/json", $"efiscal-regulator-{category}-page-{currentPage}.json")
            : Results.Ok(response);
    }

    private static Task<Batch> ReadPage(EfiscalDbContext db, string category, IQueryable<FiscalDocument> docs, int page, CancellationToken ct)
    {
        return category switch
        {
            "documents" => Page(docs.OrderByDescending(x => x.Date).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Type, x.Number, x.Date, x.BranchId, x.Currency, x.Vat, x.Total }), page, ct),
            "tax-summary" => Page(docs.GroupBy(x => new { x.Currency, x.Type }).OrderBy(x => x.Key.Currency).ThenBy(x => x.Key.Type)
                .Select(x => new { x.Key.Currency, x.Key.Type, count = x.Count(), vatableSales = x.Sum(d => d.VatableSales),
                    exemptSales = x.Sum(d => d.ExemptSales), zeroRatedSales = x.Sum(d => d.ZeroRatedSales),
                    nonVatSales = x.Sum(d => d.NonVatSales), vat = x.Sum(d => d.Vat), total = x.Sum(d => d.Total) }), page, ct),
            "reconciliation" => Page(db.ReconciliationChecks.AsNoTracking().Where(x => x.DocumentId != null &&
                    docs.Any(d => d.SubscriberId == x.SubscriberId && d.CompanyId == x.CompanyId && d.Id == x.DocumentId))
                .OrderByDescending(x => x.At).ThenBy(x => x.Id).Select(x => new { x.DocumentId, x.Status, x.Coverage,
                    x.Completeness, x.Currency, x.ExpectedTotal, x.ActualTotal, x.At }), page, ct),
            "reporting" => Page(db.Set<ReportingOperation>().AsNoTracking().Where(x =>
                    docs.Any(d => d.SubscriberId == x.SubscriberId && d.CompanyId == x.CompanyId && d.Id == x.DocumentId))
                .OrderByDescending(x => x.At).ThenBy(x => x.Id).Select(x => new { x.DocumentId, x.Mode, x.State, x.SchemaVersion, x.At,
                    governmentStatus = "NotSubmitted" }), page, ct),
            "audit" => Page(db.SecurityAudits.AsNoTracking().Where(x => x.TargetId != null && x.SubscriberId != null &&
                    docs.Any(d => d.Id == x.TargetId && d.SubscriberId == x.SubscriberId))
                .OrderByDescending(x => x.At).ThenBy(x => x.Id).Select(x => new { documentId = x.TargetId, x.Action, x.At }), page, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
    }

    private static async Task<Batch> Page<T>(IQueryable<T> rows, int page, CancellationToken ct)
    {
        var items = await rows.Skip((page - 1) * PageSize).Take(PageSize + 1).ToArrayAsync(ct);
        return new(items[..Math.Min(items.Length, PageSize)], items.Length > PageSize);
    }

    [GeneratedRegex(@"\A[0-9]{3}[A-Z]?\z")]
    private static partial Regex ValidRdoCode();
}
