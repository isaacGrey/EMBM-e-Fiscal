using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Reconciliation;

internal sealed record CoverageInput(Guid ConnectionId, DateOnly Adoption, DateOnly? Start, DateOnly? End,
    string? Mode, string[]? Types, string[]? ExcludedSources, string? ExclusionReason, string? Reason,
    string? TimeZone, string? IssuanceScope, decimal AmountTolerance = 0, int FreshnessHours = 48, int? IntervalHours = null,
    bool AfterSynchronization = false, int BatchSize = 25, int ExpectedVersion = 0);

internal static class Coverage
{
    internal const string View = "Reconciliation.View";
    internal const string Run = "Reconciliation.Run";
    internal const string Batch = "Reconciliation.BatchRun";
    internal const string Review = "Reconciliation.Review";
    internal const string Assign = "Reconciliation.AssignReviewer";
    internal const string Manage = "Reconciliation.ManageCoverage";
    internal const string Approve = "Reconciliation.ApproveCoverage";
    internal const string Policy = "Reconciliation.ManagePolicy";
    internal const string Schedule = "Reconciliation.ManageSchedule";
    internal const string Export = "Reconciliation.Export";
    internal const string References = "Reconciliation.ViewHistoricalReferences";
    internal const string Match = "Reconciliation.MatchExternalRecord";

    internal static IQueryable<CoverageRevision> For(EfiscalDbContext db, Guid subscriber, Guid company)
        => db.CoverageRevisions.Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    internal static IQueryable<CoverageRevision> Active(IQueryable<CoverageRevision> scope, DateTimeOffset now)
        => scope.Where(x => x.EffectiveAt <= now && !scope.Any(next => next.ConnectionId == x.ConnectionId &&
            next.Number > x.Number && next.EffectiveAt <= now));

    internal static CoverageRevision Validate(CoverageInput input, ExternalConnection connection, Guid actor)
    {
        var start = input.Start ?? input.Adoption;
        var types = input.Types ?? ["SalesInvoice", "CreditMemo"];
        var mode = input.Mode ?? "EfiscalOnly";
        var scope = input.IssuanceScope ?? "Adopted";
        var exclusions = input.ExcludedSources ?? [];
        var reason = SpecImport.Text(input.Reason ?? "", 300);
        var exclusionReason = SpecImport.Text(input.ExclusionReason ?? "", 300);
        Require(input.Adoption.Year >= 2000 && start.Year >= 2000 && (input.End is null || input.End >= start), "Enter a valid inclusive coverage period and adoption date.");
        Require(mode is "EfiscalOnly" or "FullCoverage" && scope is "Adopted" or "AllVerifiedSources", "Choose an approved coverage mode and issuance scope.");
        Require(types.Length is >= 1 and <= 3 && types.Distinct().Count() == types.Length && types.All(DocumentRules.Types.Contains), "Choose supported document types.");
        Require(exclusions.Length <= 1 && exclusions.All(x => x == "ApprovedExternal") && (exclusions.Length == 0 || exclusionReason.Length > 0),
            "An approved external issuance exclusion requires a reason.");
        Require(reason.Length > 0, "Record the reason for the coverage configuration.");
        Require(input.TimeZone is null or "Asia/Manila", "Choose the supported company business timezone.");
        Require(input.AmountTolerance is >= 0 and <= 0.01m && decimal.Round(input.AmountTolerance, 2) == input.AmountTolerance,
            "Use exact comparison or the approved development-only PHP rounding limit of 0.01.");
        Require(input.FreshnessHours is >= 1 and <= 720 && input.IntervalHours is null or 24 or 168 &&
            input.BatchSize is >= 1 and <= 50, "Choose a supported schedule and verification freshness.");
        return new()
        {
            SubscriberId = connection.SubscriberId, CompanyId = connection.CompanyId, ConnectionId = connection.Id,
            Adoption = input.Adoption, Start = start, End = input.End, Mode = mode, Types = types, IssuanceScope = scope,
            ExcludedSources = exclusions, ExclusionReason = exclusionReason.Length == 0 ? null : exclusionReason, Reason = reason,
            CreatedBy = actor, AmountTolerance = input.AmountTolerance, FreshnessHours = input.FreshnessHours, IntervalHours = input.IntervalHours,
            AfterSynchronization = input.AfterSynchronization, BatchSize = input.BatchSize
        };
    }

    internal static string Classify(CoverageRevision coverage, RetrievedAccounting record)
    {
        if (record.Date is not DateOnly date || string.IsNullOrWhiteSpace(record.Id)) return "UnknownCoverage";
        if (date < coverage.Start) return "HistoricalExcluded";
        if (coverage.End is DateOnly end && date > end) return "OutsideCoveragePeriod";
        if (coverage.Mode != "FullCoverage" || record.Type == "SalesOrder" || !coverage.Types.Contains(record.Type) ||
            coverage.ExcludedSources.Contains(record.Source) || coverage.IssuanceScope == "Adopted" && record.Source != "Adopted")
            return "NotExpected";
        return record.Source is "Adopted" or "ApprovedExternal" ? "Eligible" : "UnknownCoverage";
    }

    internal static async Task<bool> Conflicts(EfiscalDbContext db, ExternalConnection link, CoverageRevision proposed, DateTimeOffset now, CancellationToken ct)
    {
        var current = await Active(For(db, link.SubscriberId, link.CompanyId), now).Include(x => x.Connection)
            .Where(x => x.ConnectionId != link.Id).ToArrayAsync(ct);
        return current.Any(other => (other.Connection.BranchId == link.BranchId ||
                other.Connection.BranchId == null || link.BranchId == null) &&
            other.Types.Intersect(proposed.Types).Any() && other.Start <= (proposed.End ?? DateOnly.MaxValue) &&
            proposed.Start <= (other.End ?? DateOnly.MaxValue));
    }
}

