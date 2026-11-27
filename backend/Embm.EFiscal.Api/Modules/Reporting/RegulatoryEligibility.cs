using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal static class RegulatoryEligibility
{
    internal static DateOnly ManilaDate(DateTimeOffset? instant = null) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(instant ?? DateTimeOffset.UtcNow, "Asia/Manila").DateTime);

    internal static async Task<object> Evaluate(EfiscalDbContext db, Guid subscriber, Guid company, Guid branch, string documentType, CancellationToken ct)
    {
        var date = ManilaDate();
        var candidates = await Candidates(db, subscriber, company, branch, documentType, ct);
        return new
        {
            policyVersion = "preparation-only-v1", evaluatedOn = date, issuance = "Disabled",
            preparation = "InternalPreparationOnly", transmission = "Disabled",
            applicableAuthorizations = candidates.Where(x => x.IsCurrent(date)),
            expiredAuthorizations = candidates.Where(x => x.ExpiresOn < date).Select(x => new { x.Id, x.Kind }),
            evidenceLimitations = candidates.Where(x => !x.IsCurrent(date)),
            missingRequirements = new[] { "Verified official applicability and runtime approval policy" },
            blockingReasons = new[] { "Production issuance is disabled", "Live government transmission is disabled" }
        };
    }

    internal sealed record Candidate(Guid Id, string Kind, int Number, string Reference, DateOnly EffectiveFrom, DateOnly? ExpiresOn, string? Decision)
    {
        internal bool IsCurrent(DateOnly date) => EffectiveFrom <= date && (ExpiresOn == null || ExpiresOn >= date) &&
            Decision == "ExternalAuthorizationDocumented";
    }

    internal static Task<Candidate[]> Candidates(EfiscalDbContext db, Guid subscriber, Guid company, Guid branch, string documentType, CancellationToken ct)
    {
        var versions = db.Set<RegulatoryAuthorizationVersion>().AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
        var reviews = db.Set<RegulatoryAuthorizationReview>().AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
        return db.Set<RegulatoryAuthorization>().AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.BranchId == branch && x.DocumentType == documentType)
            .Select(x => new
            {
                x.Id, x.Kind,
                version = versions.Where(v => v.AuthorizationId == x.Id).OrderByDescending(v => v.Number).First()
            })
            .OrderBy(x => x.Kind).ThenBy(x => x.Id)
            .Select(x => new Candidate(
                x.Id, x.Kind, x.version.Number, x.version.Reference, x.version.EffectiveFrom, x.version.ExpiresOn,
                reviews.Where(r => r.AuthorizationId == x.Id && r.Number == x.version.Number)
                    .OrderByDescending(r => r.Sequence).Select(r => r.Decision).FirstOrDefault()))
            .ToArrayAsync(ct);
    }
}
