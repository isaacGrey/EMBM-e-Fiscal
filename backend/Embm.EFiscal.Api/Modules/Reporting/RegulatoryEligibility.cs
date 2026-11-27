using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal static class RegulatoryEligibility
{
    internal static DateOnly ManilaDate() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Manila").DateTime);

    internal static async Task<object> Evaluate(EfiscalDbContext db, Guid subscriber, Guid company, Guid branch, string documentType, CancellationToken ct)
    {
        var date = ManilaDate();
        var versions = db.Set<RegulatoryAuthorizationVersion>().AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
        var reviews = db.Set<RegulatoryAuthorizationReview>().AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
        var candidates = await db.Set<RegulatoryAuthorization>().AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.BranchId == branch && x.DocumentType == documentType)
            .Select(x => new
            {
                x.Id, x.Kind,
                version = versions.Where(v => v.AuthorizationId == x.Id).OrderByDescending(v => v.Number).First()
            })
            .Select(x => new
            {
                x.Id, x.Kind, x.version.Number, x.version.Reference, x.version.EffectiveFrom, x.version.ExpiresOn,
                decision = reviews.Where(r => r.AuthorizationId == x.Id && r.Number == x.version.Number)
                    .OrderByDescending(r => r.Sequence).Select(r => r.Decision).FirstOrDefault()
            }).ToArrayAsync(ct);

        var current = candidates.Where(x => x.EffectiveFrom <= date && (x.ExpiresOn == null || x.ExpiresOn >= date) &&
            x.decision == "ExternalAuthorizationDocumented").ToArray();
        return new
        {
            policyVersion = "preparation-only-v1", evaluatedOn = date, issuance = "Disabled",
            preparation = "InternalPreparationOnly", transmission = "Disabled",
            applicableAuthorizations = current,
            expiredAuthorizations = candidates.Where(x => x.ExpiresOn < date).Select(x => new { x.Id, x.Kind }),
            evidenceLimitations = candidates.Where(x => !current.Contains(x)),
            missingRequirements = new[] { "Verified official applicability and runtime approval policy" },
            blockingReasons = new[] { "Production issuance is disabled", "Live government transmission is disabled" }
        };
    }
}
