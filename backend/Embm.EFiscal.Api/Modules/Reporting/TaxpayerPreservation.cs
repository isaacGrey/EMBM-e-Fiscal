using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class RetentionVersion
{
    public Guid SubscriberId { get; init; }
    public Guid CompanyId { get; init; }
    public string Category { get; init; } = "";
    public int Version { get; init; }
    public Guid ActorId { get; init; }
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateOnly EffectiveFrom { get; init; }
    public int? ProposedYears { get; init; }
    public string? RequirementKey { get; init; }
    public int? RequirementVersion { get; init; }
    public string Preservation { get; init; } = "";
    public string? MigrationEvidence { get; init; }
}

internal sealed class LegalHold
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SubscriberId { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? BranchId { get; init; }
    public string Category { get; init; } = "";
    public DateOnly From { get; init; }
    public DateOnly Through { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Reason { get; init; } = "";
}

internal sealed class LegalHoldRelease
{
    public Guid HoldId { get; init; }
    public Guid ActorId { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public string Reason { get; init; } = "";
}

internal static class TaxpayerPreservation
{
    private static readonly string[] Categories = ["Documents", "ReportingEvidence", "ReconciliationEvidence", "Audit"];
    internal sealed record PolicyInput(string Category, DateOnly EffectiveFrom, int? ProposedYears, string? RequirementKey,
        int? RequirementVersion, string Preservation, string? MigrationEvidence, int ExpectedVersion);
    internal sealed record HoldInput(Guid? BranchId, string Category, DateOnly From, DateOnly Through, string Reason);
    internal sealed record ReleaseInput(string Reason);

    internal static void Configure(ModelBuilder model)
    {
        model.Entity<RetentionVersion>(e =>
        {
            e.ToTable("RetentionVersions");
            e.HasKey(x => new { x.SubscriberId, x.CompanyId, x.Category, x.Version });
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, Id = x.CompanyId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LegalHold>(e =>
        {
            e.ToTable("LegalHolds");
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.CreatedAt });
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, Id = x.CompanyId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LegalHoldRelease>(e =>
        {
            e.ToTable("LegalHoldReleases");
            e.HasKey(x => x.HoldId);
            e.HasOne<LegalHold>().WithOne().HasForeignKey<LegalHoldRelease>(x => x.HoldId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    internal static void MapTaxpayerPreservation(this WebApplication app)
    {
        var group = app.MapGroup("/api/workspace/{subscriber:guid}/companies/{company:guid}/preservation")
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect).WithTags("Taxpayer record preservation");
        group.MapGet("/", Register);
        group.MapPost("/policies", Publish);
        group.MapPost("/holds", Place);
        group.MapPost("/holds/{id:guid}/release", Release);
    }

    private static IQueryable<RetentionVersion> Policies(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.Set<RetentionVersion>().AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
    private static IQueryable<LegalHold> Holds(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.Set<LegalHold>().AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    private static async Task<IResult> Register(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, "Reports.View", http, db, ct, existingRecords: true) is not { } access)
            return TenantAccess.Missing();
        var holds = await Holds(db, subscriber, company).OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new { hold = x, release = db.Set<LegalHoldRelease>().SingleOrDefault(r => r.HoldId == x.Id) }).ToArrayAsync(ct);
        var policies = await Policies(db, subscriber, company).OrderByDescending(x => x.RecordedAt).Take(100).ToArrayAsync(ct);
        return Results.Ok(new { holds, policies, categories = Categories, canManage = access.Grant.Grants.Contains(Permissions.CompanyManage),
            canRelease = access.Grant.IsOwner, disposalMode = "PreserveAll", legalApplicability = "RequiresVerifiedReview" });
    }

    private static async Task<IResult> Publish(Guid subscriber, Guid company, PolicyInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct, existingRecords: true) is not { Grant.IsOwner: true })
            return TenantAccess.Missing();
        if (!Categories.Contains(input.Category) || input.EffectiveFrom.Year < 2000 || input.ProposedYears is < 1 or > 100 ||
            !ApiSupport.Text(input.Preservation, 1000) || !ApiSupport.Optional(input.MigrationEvidence, 500))
            return ApiSupport.Invalid("policy", "Specify a category, effective date and preservation requirement.");
        if (input.RequirementKey is not null || input.RequirementVersion is not null)
        {
            if (!await db.Set<AccreditationVersion>().AsNoTracking().AnyAsync(x => x.Category == "Requirement" &&
                x.Key == input.RequirementKey && x.Number == input.RequirementVersion, ct))
                return ApiSupport.Invalid("requirement", "Select an existing requirements registry version.");
        }
        var latest = await Policies(db, subscriber, company).Where(x => x.Category == input.Category).MaxAsync(x => (int?)x.Version, ct) ?? 0;
        if (latest != input.ExpectedVersion) return ApiSupport.Problem(409, "Reload the current retention policy.");
        var version = new RetentionVersion { SubscriberId = subscriber, CompanyId = company, Category = input.Category,
            Version = latest + 1, ActorId = http.UserId(), EffectiveFrom = input.EffectiveFrom, ProposedYears = input.ProposedYears,
            Preservation = input.Preservation.Trim(), MigrationEvidence = input.MigrationEvidence?.Trim(),
            RequirementKey = input.RequirementKey, RequirementVersion = input.RequirementVersion };
        db.Add(version);
        db.Audit("Preservation.PolicyRecorded", http.UserId(), subscriber, company);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(version);
    }

    private static async Task<IResult> Place(Guid subscriber, Guid company, HoldInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct, existingRecords: true) is null)
            return TenantAccess.Missing();
        if (!Categories.Contains(input.Category) || input.From.Year < 2000 || input.Through < input.From || !ApiSupport.Text(input.Reason, 1000))
            return ApiSupport.Invalid("hold", "Specify a valid category, inclusive date range and reason.");
        if (input.BranchId is Guid branch && !await db.Branches.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == branch, ct))
            return TenantAccess.Missing();
        var hold = new LegalHold { SubscriberId = subscriber, CompanyId = company, BranchId = input.BranchId,
            Category = input.Category, From = input.From, Through = input.Through, CreatedBy = http.UserId(), Reason = input.Reason.Trim() };
        db.Add(hold);
        db.Audit("Preservation.HoldPlaced", http.UserId(), subscriber, hold.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(hold);
    }

    private static async Task<IResult> Release(Guid subscriber, Guid company, Guid id, ReleaseInput input, HttpContext http, EfiscalDbContext db,
        CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct, existingRecords: true) is not { Grant.IsOwner: true } ||
            !await Holds(db, subscriber, company).AnyAsync(x => x.Id == id, ct)) return TenantAccess.Missing();
        if (!ApiSupport.Text(input.Reason, 1000)) return ApiSupport.Invalid("reason", "Record the authorization and reason for release.");
        if (await db.Set<LegalHoldRelease>().AnyAsync(x => x.HoldId == id, ct)) return ApiSupport.Problem(409, "This hold has already been released.");
        var release = new LegalHoldRelease { HoldId = id, ActorId = http.UserId(), Reason = input.Reason.Trim() };
        db.Add(release);
        db.Audit("Preservation.HoldReleased", http.UserId(), subscriber, id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(release);
    }
}
