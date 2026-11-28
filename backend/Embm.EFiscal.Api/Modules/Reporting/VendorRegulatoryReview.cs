using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class VendorReviewCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AuthorizationId { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class VendorReviewAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CaseId { get; set; }
    public Guid Reviewer { get; set; }
    public Guid AssignedBy { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class VendorReviewRevocation
{
    public Guid AssignmentId { get; set; }
    public Guid RevokedBy { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset RevokedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal static class VendorRegulatoryReview
{
    public const string Manage = "Vendor.Regulatory.Assign";
    public const string Review = "Vendor.Regulatory.ReviewAssigned";
    internal sealed record AssignmentInput(Guid Reviewer, DateTimeOffset StartsAt, DateTimeOffset ExpiresAt, string Reason);
    internal sealed record ReasonInput(string Reason);
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<VendorReviewCase>(e =>
        {
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId }).IsUnique();
            e.HasOne<RegulatoryAuthorization>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.AuthorizationId })
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<VendorReviewAssignment>(e =>
        {
            e.HasIndex(x => new { x.CaseId, x.Reviewer, x.ExpiresAt });
            e.HasOne<VendorReviewCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.Reviewer).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_VendorReviewAssignment_Time", """ "StartsAt" < "ExpiresAt" """));
        });
        model.Entity<VendorReviewRevocation>(e =>
        {
            e.HasKey(x => x.AssignmentId);
            e.HasOne<VendorReviewAssignment>().WithOne().HasForeignKey<VendorReviewRevocation>(x => x.AssignmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    internal static void MapVendorRegulatoryReview(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        var vendor = app.MapGroup("/api/vendor/taxpayer-review").RequireAuthorization("vendor").AddEndpointFilter(ApiSupport.Protect);
        vendor.MapGet("/", Queue);
        vendor.MapGet("/reviewers", Reviewers);
        vendor.MapPut("/reviewers/{id:guid}", Enroll);
        vendor.MapPost("/{id:guid}/assignments", Assign);
        vendor.MapPost("/{id:guid}/assignments/{assignment:guid}/revoke", Revoke);
        vendor.MapGet("/{id:guid}", Detail);
        vendor.MapGet("/{id:guid}/versions/{number:int}/evidence", Download);
        vendor.MapPost("/{id:guid}/versions/{number:int}/reviews", RecordReview);
        app.MapPost("/api/workspace/{subscriber:guid}/companies/{company:guid}/regulatory-authorizations/{authorization:guid}/vendor-case", Open)
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect);
    }

    internal static Task<bool> Has(EfiscalDbContext db, Guid user, string grant, CancellationToken ct) =>
        db.VendorConnectorGrants.AnyAsync(x => x.UserId == user && x.Permission == grant, ct);
    private static IQueryable<VendorReviewCase> Cases(EfiscalDbContext db) => db.Set<VendorReviewCase>().AsNoTracking();
    private static IQueryable<VendorReviewAssignment> Assignments(EfiscalDbContext db) => db.Set<VendorReviewAssignment>().AsNoTracking();
    private static IQueryable<VendorReviewAssignment> Unrevoked(EfiscalDbContext db) =>
        Assignments(db).Where(a => !db.Set<VendorReviewRevocation>().Any(r => r.AssignmentId == a.Id));
    private static IQueryable<VendorReviewAssignment> Active(EfiscalDbContext db, Guid reviewer, DateTimeOffset now) =>
        Unrevoked(db).Where(x => x.Reviewer == reviewer && x.StartsAt <= now && x.ExpiresAt > now);

    private static async Task<IResult> Open(Guid subscriber, Guid company, Guid authorization, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, RegulatoryAuthorizations.Submit, http, db, ct) is not { } access ||
            !access.Grant.Subscriber.DevelopmentFixture) return TenantAccess.Missing();
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, RegulatoryAuthorizations.Submit, http, db, ct) is null ||
            !await db.Set<RegulatoryAuthorization>().AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == authorization, ct))
            return TenantAccess.Missing();
        var existing = await Cases(db).SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.AuthorizationId == authorization, ct);
        if (existing is not null) return Results.Ok(new { existing.Id });
        var row = new VendorReviewCase { SubscriberId = subscriber, CompanyId = company, AuthorizationId = authorization, RequestedBy = http.UserId() };
        db.Add(row);
        db.Audit("Regulatory.VendorReviewRequested", http.UserId(), subscriber, row.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { row.Id });
    }

    private static async Task<IResult> Queue(HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var actor = http.UserId();
        var manage = await Has(db, actor, Manage, ct);
        var review = await Has(db, actor, Review, ct);
        if (!manage && !review) return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        var active = Active(db, actor, now);
        var rows = from c in Cases(db)
                   join a in db.Set<RegulatoryAuthorization>() on new { c.SubscriberId, c.CompanyId, Id = c.AuthorizationId }
                       equals new { a.SubscriberId, a.CompanyId, a.Id }
                   where manage || active.Any(x => x.CaseId == c.Id)
                   orderby c.RequestedAt descending
                   select new { c.Id, a.BranchId, a.Kind, a.DocumentType, c.RequestedAt,
                       canReview = review && active.Any(x => x.CaseId == c.Id) };
        return Results.Ok(new { items = await rows.Take(100).ToArrayAsync(ct), canAssign = manage });
    }

    private static async Task<IResult> Reviewers(HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!await Has(db, http.UserId(), Manage, ct)) return TenantAccess.Missing();
        var items = await db.Users.AsNoTracking().Where(u => u.Kind == IdentityKind.Vendor && u.DevelopmentFixture &&
            u.Status == IdentityStatus.Verified && u.Id != http.UserId()).OrderBy(u => u.FullName).Take(100)
            .Select(u => new { u.Id, u.FullName, enrolled = db.VendorConnectorGrants.Any(g => g.UserId == u.Id && g.Permission == Review) }).ToArrayAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> Enroll(Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var actor = http.UserId();
        if (id == actor || !await Has(db, actor, Manage, ct) || !await db.Users.AnyAsync(x => x.Id == id &&
            x.Kind == IdentityKind.Vendor && x.DevelopmentFixture && x.Status == IdentityStatus.Verified, ct)) return TenantAccess.Missing();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 1041))", ct);
        if (!await Has(db, id, Review, ct))
        {
            db.VendorConnectorGrants.Add(new() { UserId = id, Permission = Review });
            db.Audit("Regulatory.VendorReviewerEnrolled", actor, target: id);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Assign(Guid id, AssignmentInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var actor = http.UserId();
        var row = await Cases(db).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null || actor == input.Reviewer || !await Has(db, actor, Manage, ct) || !await Has(db, input.Reviewer, Review, ct))
            return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        if (!ApiSupport.Text(input.Reason, 600) || input.ExpiresAt <= now || input.StartsAt >= input.ExpiresAt ||
            input.StartsAt < now.AddMinutes(-10) || input.ExpiresAt > now.AddDays(90))
            return ApiSupport.Invalid("assignment", "Supply a reason and a current or future assignment of at most 90 days.");
        await using var tx = await db.LockSubscriberAsync(row.SubscriberId, ct);
        if (await Unrevoked(db).AnyAsync(a => a.CaseId == id && a.Reviewer == input.Reviewer &&
            a.StartsAt < input.ExpiresAt && a.ExpiresAt > input.StartsAt, ct)) return ApiSupport.Problem(409, "An assignment already covers that period.");
        var assignment = new VendorReviewAssignment { CaseId = id, Reviewer = input.Reviewer, AssignedBy = actor,
            StartsAt = input.StartsAt.ToUniversalTime(), ExpiresAt = input.ExpiresAt.ToUniversalTime(), Reason = input.Reason.Trim() };
        db.Add(assignment);
        db.Audit("Regulatory.VendorReviewerAssigned", actor, row.SubscriberId, id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(assignment);
    }

    private static async Task<IResult> Revoke(Guid id, Guid assignment, ReasonInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!await Has(db, http.UserId(), Manage, ct)) return TenantAccess.Missing();
        if (!ApiSupport.Text(input.Reason, 600)) return ApiSupport.Invalid("reason", "Record a revocation reason.");
        var row = await Cases(db).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return TenantAccess.Missing();
        await using var tx = await db.LockSubscriberAsync(row.SubscriberId, ct);
        if (!await Unrevoked(db).AnyAsync(x => x.CaseId == id && x.Id == assignment, ct)) return TenantAccess.Missing();
        db.Add(new VendorReviewRevocation { AssignmentId = assignment, RevokedBy = http.UserId(), Reason = input.Reason.Trim() });
        db.Audit("Regulatory.VendorAssignmentRevoked", http.UserId(), row.SubscriberId, id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Detail(Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var actor = http.UserId();
        var manage = await Has(db, actor, Manage, ct);
        var row = await Cases(db).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return TenantAccess.Missing();
        await using var tx = await db.LockSubscriberAsync(row.SubscriberId, ct);
        var assigned = await IsAssigned(db, id, actor, ct);
        if (!manage && !assigned) return TenantAccess.Missing();
        var history = manage ? await Assignments(db).Where(x => x.CaseId == id).OrderByDescending(x => x.StartsAt)
            .Select(x => new { assignment = x, revocation = db.Set<VendorReviewRevocation>().FirstOrDefault(r => r.AssignmentId == x.Id) })
            .Take(100).ToArrayAsync(ct) : [];
        if (!assigned) return Results.Ok(new { row.Id, canReview = false, assignments = history });
        var authorization = await db.Set<RegulatoryAuthorization>().AsNoTracking().SingleAsync(x => x.SubscriberId == row.SubscriberId &&
            x.CompanyId == row.CompanyId && x.Id == row.AuthorizationId, ct);
        var company = await db.Companies.AsNoTracking().Where(x => x.SubscriberId == row.SubscriberId && x.Id == row.CompanyId)
            .Select(x => x.RegisteredName).SingleAsync(ct);
        var branch = await db.Branches.AsNoTracking().Where(x => x.SubscriberId == row.SubscriberId && x.CompanyId == row.CompanyId && x.Id == authorization.BranchId)
            .Select(x => new { x.Name, x.Code }).SingleAsync(ct);
        var rdo = await db.RdoAssignments.AsNoTracking().Where(x => x.SubscriberId == row.SubscriberId && x.CompanyId == row.CompanyId &&
            (x.BranchId == null || x.BranchId == authorization.BranchId)).OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new { x.Code, x.BranchId, x.RegistrationStatus, x.EffectiveFrom, x.EffectiveUntil }).Take(50).ToArrayAsync(ct);
        return Results.Ok(new { row.Id, canReview = true, assignments = history, company, branch, rdo, authorization.Kind, authorization.DocumentType,
            versions = await RegulatoryAuthorizations.CaseVersions(db, row, ct), reviews = await RegulatoryAuthorizations.CaseReviews(db, row, ct) });
    }

    private static async Task<bool> IsAssigned(EfiscalDbContext db, Guid id, Guid actor, CancellationToken ct) =>
        await Has(db, actor, Review, ct) && await Active(db, actor, DateTimeOffset.UtcNow).AnyAsync(x => x.CaseId == id, ct);

    private static async Task<IResult> Download(Guid id, int number, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var row = await Cases(db).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null || !await IsAssigned(db, id, http.UserId(), ct)) return TenantAccess.Missing();
        await using var tx = await db.LockSubscriberAsync(row.SubscriberId, ct);
        if (!await IsAssigned(db, id, http.UserId(), ct)) return TenantAccess.Missing();
        var result = await RegulatoryAuthorizations.DownloadCaseEvidence(db, row, number, http, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private static async Task<IResult> RecordReview(Guid id, int number, RegulatoryAuthorizations.ReviewCommand input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var row = await Cases(db).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null || !await IsAssigned(db, id, http.UserId(), ct)) return TenantAccess.Missing();
        if (input.Decision is not ("ReviewAccepted" or "AdditionalEvidence" or "Rejected") ||
            !ApiSupport.Text(input.Reason, 1000) || input.ExpectedSequence < 0)
            return ApiSupport.Invalid("decision", "Choose an internal review decision and record a reason.");
        await using var tx = await db.LockSubscriberAsync(row.SubscriberId, ct);
        if (!await IsAssigned(db, id, http.UserId(), ct)) return TenantAccess.Missing();
        var result = await RegulatoryAuthorizations.RecordScopedReview(row.SubscriberId, row.CompanyId, row.AuthorizationId, number, input, http, db, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
