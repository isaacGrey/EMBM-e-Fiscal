using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record ReconciliationFilters(Guid? Connection, Guid? Branch, string? Type, string? Status,
    string? Category, string? CoverageMode, string? Search, DateOnly? From, DateOnly? Through, bool? History, string? Sort, int? Page);

internal static partial class ClientConnectEndpoints
{
    private static IQueryable<ReconciliationCheck> Checks(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.ReconciliationChecks.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    private static IQueryable<ReconciliationCheck> FilterChecks(EfiscalDbContext db, Guid subscriber, Guid company, ReconciliationFilters filter, DateTimeOffset now)
    {
        var scope = Checks(db, subscriber, company);
        var rows = filter.History == true ? scope : scope.Where(x => !scope.Any(y => y.UnitKey == x.UnitKey &&
            (y.At > x.At || y.At == x.At && y.Id.CompareTo(x.Id) > 0)));
        if (filter.Connection is Guid connection) rows = rows.Where(x => x.ConnectionId == connection);
        if (filter.Branch is Guid branch) rows = rows.Where(x => x.BranchId == branch);
        if (filter.Type is string type) rows = rows.Where(x => x.Type == type);
        if (filter.Status is "StaleVerification") rows = rows.Where(x => x.FreshUntil < now && x.Retrieval == "Complete" && x.Coverage == "Eligible");
        else if (filter.Status is string status) rows = rows.Where(x => x.Status == status);
        if (filter.Category is string category) rows = rows.Where(x => x.Categories.Contains(category));
        if (filter.CoverageMode is string mode) rows = rows.Where(x => db.CoverageRevisions.Any(v => v.Id == x.CoverageId &&
            v.SubscriberId == subscriber && v.CompanyId == company && v.Mode == mode));
        if (filter.From is DateOnly from) rows = rows.Where(x => x.BusinessDate >= from);
        if (filter.Through is DateOnly through) rows = rows.Where(x => x.BusinessDate <= through);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = SpecImport.Text(filter.Search, 80).ToLowerInvariant();
            rows = rows.Where(x => x.Number != null && x.Number.ToLower().Contains(search) ||
                x.ExternalNumber != null && x.ExternalNumber.ToLower().Contains(search));
        }
        return rows;
    }

    private static object CheckView(ReconciliationCheck x, DateTimeOffset now) => new { x.Id, x.DocumentId, x.ConnectionId, x.CoverageId, x.Type,
        x.Number, x.ExternalNumber, x.BusinessDate, x.Currency, x.ExpectedTotal, x.ActualTotal, difference = x.ActualTotal - x.ExpectedTotal,
        x.Coverage, x.Retrieval, x.Completeness, x.Status, x.Categories, x.FindingCount, x.ExternalChanged, x.Synchronization, x.At,
        stale = x.FreshUntil < now && x.Retrieval == "Complete" && x.Coverage == "Eligible" };

    private static IQueryable<ReconciliationCheck> Sorted(IQueryable<ReconciliationCheck> rows, string? sort) => sort switch
    {
        "oldest" => rows.OrderBy(x => x.At).ThenBy(x => x.Id),
        "date" => rows.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.At).ThenBy(x => x.Id),
        _ => rows.OrderByDescending(x => x.At).ThenBy(x => x.Id)
    };

    private static async Task<IResult> ReconciliationRegister(Guid subscriber, Guid company, [AsParameters] ReconciliationFilters filter,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        var page = Math.Clamp(filter.Page ?? 1, 1, 1000);
        var rows = await Sorted(FilterChecks(db, subscriber, company, filter, now), filter.Sort).Skip((page - 1) * 30).Take(31).ToArrayAsync(ct);
        return Results.Ok(new { items = rows.Take(30).Select(x => CheckView(x, now)), page, hasMore = rows.Length > 30 });
    }

    private static async Task<IResult> ReconciliationOverview(Guid subscriber, Guid company, [AsParameters] ReconciliationFilters filter,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        var rows = FilterChecks(db, subscriber, company, filter with { History = false }, now);
        var groups = await rows.GroupBy(x => new { x.Status, x.Completeness, x.Coverage, Stale = x.FreshUntil < now && x.Retrieval == "Complete" })
            .Select(g => new { g.Key, Count = g.Count(), Findings = g.Sum(x => x.FindingCount) }).ToArrayAsync(ct);
        var pending = from pin in db.SyncOperations.AsNoTracking()
                      join doc in db.FiscalDocuments.AsNoTracking() on pin.DocumentId equals doc.Id
                      where pin.SubscriberId == subscriber && pin.CompanyId == company && (filter.Connection == null || pin.ConnectionId == filter.Connection) &&
                          (filter.Branch == null || doc.BranchId == filter.Branch) && (filter.Type == null || doc.Type == filter.Type) &&
                          (filter.From == null || doc.Date >= filter.From) && (filter.Through == null || doc.Date <= filter.Through) &&
                          !db.ReconciliationChecks.Any(r => r.SubscriberId == subscriber && r.CompanyId == company && r.DocumentId == pin.DocumentId)
                      select pin;
        var uncheckedCount = filter.Status is null && filter.Category is null && filter.CoverageMode is null &&
            string.IsNullOrWhiteSpace(filter.Search) ? await pending.CountAsync(ct) : 0;
        var eligible = groups.Where(g => g.Key.Coverage == "Eligible" && g.Key.Completeness != "Excluded").ToArray();
        var conclusive = eligible.Where(g => !g.Key.Stale && g.Key.Completeness is "Complete" or "Conclusive").Sum(x => x.Count);
        var total = eligible.Sum(x => x.Count) + uncheckedCount;
        var exact = eligible.Where(g => !g.Key.Stale && g.Key.Status == "Matched").Sum(x => x.Count);
        var stale = await rows.CountAsync(x => x.FreshUntil < now && x.Coverage == "Eligible" && x.Retrieval == "Complete", ct);
        var latest = await rows.Where(x => x.Status == "Matched" || x.Status == "MatchedWithinTolerance").MaxAsync(x => (DateTimeOffset?)x.At, ct);
        int Count(string status) => groups.Where(x => x.Key.Status == status).Sum(x => x.Count);
        return Results.Ok(new { eligible = total, conclusive, exact,
            withinTolerance = eligible.Where(g => !g.Key.Stale && g.Key.Status == "MatchedWithinTolerance").Sum(x => x.Count),
            partial = Count("PartialVerification"), findings = eligible.Sum(x => x.Findings), discrepancyUnits = eligible.Where(x => x.Findings > 0).Sum(x => x.Count),
            missingAccounting = Count("MissingAccountingRecord"), missingEfiscal = Count("MissingEfiscalRecord"), duplicates = Count("DuplicateExternalRecords"),
            awaiting = Count("AwaitingSynchronization"), notYetVerified = uncheckedCount, stale, latest,
            observedHistorical = Count("HistoricalExcluded") is int known && known > 0 ? known : (int?)null, observedOutside = Count("OutsideCoveragePeriod"),
            historyTotalKnown = false, matchRate = conclusive == 0 ? (decimal?)null : (decimal)exact / conclusive,
            verificationCoverage = total == 0 ? (decimal?)null : (decimal)conclusive / total });
    }

    private static async Task<IResult> ReconciliationDetail(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db,
        ReconciliationEngine engine, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var row = await Checks(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return TenantAccess.Missing();
        var snapshot = row.ObservationId is Guid snapshotId ? await db.AccountingObservations.AsNoTracking().SingleOrDefaultAsync(
            x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == snapshotId && x.ConnectionId == row.ConnectionId, ct) : null;
        var coverage = row.CoverageId is Guid coverageId ? await Coverage.For(db, subscriber, company).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == coverageId, ct) : null;
        var history = await Checks(db, subscriber, company).Where(x => x.UnitKey == row.UnitKey).OrderByDescending(x => x.At).Take(30).ToArrayAsync(ct);
        var findings = await db.ReconciliationFindings.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.UnitKey == row.UnitKey).OrderByDescending(x => x.DetectedAt).Take(100).ToArrayAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var canAssign = Grant(http).Grants.Contains(Coverage.Assign);
        var reviewerQuery = from member in db.SubscriberMemberships.AsNoTracking()
                            join role in db.RolePermissions on member.RoleId equals role.RoleId
                            join user in db.Users on member.UserId equals user.Id
                            where member.SubscriberId == subscriber && role.PermissionId == Coverage.Review &&
                                (member.RoleId == Permissions.Owner || db.CompanyMemberships.Any(m => m.SubscriberId == subscriber &&
                                    m.CompanyId == company && m.UserId == member.UserId))
                            orderby user.FullName
                            select new { user.Id, user.FullName };
        var reviewers = canAssign ? await reviewerQuery.Take(150).ToArrayAsync(ct) : [];
        return Results.Ok(new { check = CheckView(row, now), cells = engine.ReadCells(row), history = history.Select(x => CheckView(x, now)), reviewers,
            coverage = coverage is null ? null : CoverageView(coverage, false), snapshot = snapshot is null ? null : SnapshotView(snapshot),
            findings = await FindingViews(db, engine, subscriber, company, findings, ct),
            canRun = Grant(http).Grants.Contains(Coverage.Run), canReview = Grant(http).Grants.Contains(Coverage.Review),
            canAssign, canSimulate = Grant(http).Grants.Contains(Coverage.Manage) });
    }

    private static object SnapshotView(AccountingObservation x) => new { x.Id, x.ConnectionId, x.ConnectorVersionId, x.NormalizationVersion,
        x.Account, x.ExternalCompany, x.ExternalId, x.Number, x.Type, x.BusinessDate, x.ModifiedAt, x.RetrievedAt, x.Hash, x.Evidence };

    private static async Task<IResult> DocumentReconciliation(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var doc = await db.FiscalDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == id, ct);
        if (doc is null || !Grant(http).Grants.Contains(DocumentRules.Permission(doc.Type, false))) return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        var rows = await Checks(db, subscriber, company).Where(x => x.DocumentId == id).OrderByDescending(x => x.At).Take(20).ToArrayAsync(ct);
        var connection = await db.SyncOperations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == id)
            .Select(x => (Guid?)x.ConnectionId).SingleOrDefaultAsync(ct);
        return Results.Ok(new { items = rows.Select(x => CheckView(x, now)), connection, canRun = connection != null && Grant(http).Grants.Contains(Coverage.Run) });
    }

    private static async Task<IResult> ReconciliationFindings(Guid subscriber, Guid company, string? category, int? page, HttpContext http,
        EfiscalDbContext db, ReconciliationEngine engine, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var scope = db.ReconciliationFindings.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
        if (category is not null) scope = scope.Where(x => x.Category == category);
        var list = await scope.OrderByDescending(x => x.DetectedAt).ThenBy(x => x.Id).Skip((Math.Clamp(page ?? 1, 1, 1000) - 1) * 30)
            .Take(31).ToArrayAsync(ct);
        return Results.Ok(new { items = await FindingViews(db, engine, subscriber, company, list.Take(30).ToArray(), ct), hasMore = list.Length > 30 });
    }

    private static async Task<object[]> FindingViews(EfiscalDbContext db, ReconciliationEngine engine, Guid subscriber, Guid company,
        ReconciliationFinding[] findings, CancellationToken ct)
    {
        var ids = findings.Select(x => x.Id).ToArray();
        var events = await db.ReconciliationReviews.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            ids.Contains(x.FindingId)).OrderByDescending(x => x.At).ToArrayAsync(ct);
        var perFinding = events.ToLookup(x => x.FindingId);
        var protector = engine.Evidence(subscriber, company);
        return findings.Select(x => (object)new { x.Id, x.CheckId, x.Category, x.Field, x.Severity, x.DetectedAt,
            status = perFinding[x.Id].FirstOrDefault()?.Status ?? "Open", reviewer = perFinding[x.Id].FirstOrDefault(y => y.Reviewer != null)?.Reviewer,
            history = perFinding[x.Id].Take(30).Select(y => new { y.Id, y.Actor, y.Reviewer, y.VerificationId, y.Status, y.At,
                note = y.ProtectedNote is null ? null : protector.Unprotect(y.ProtectedNote) }) }).ToArray();
    }

    private static async Task<IResult> ReviewDiscrepancy(Guid subscriber, Guid company, Guid id, ReviewFindingInput input, HttpContext http,
        EfiscalDbContext db, ReconciliationEngine engine, CancellationToken ct)
    {
        if (!MockAccess(http) || !await db.ReconciliationFindings.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == id, ct))
            return TenantAccess.Missing();
        if (input.Status is not ("Open" or "UnderReview" or "AwaitingExternalCorrection" or "Acknowledged"))
            return ApiSupport.Invalid("status", "Successful re-verification is required to resolve a finding.");
        var note = SpecImport.Text(input.Note ?? "", 1200);
        if (note.Length == 0) return ApiSupport.Invalid("note", "Record an investigation note.");
        if (await db.ReconciliationReviews.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.FindingId == id &&
            x.Status == "ResolvedAfterVerification", ct)) return Conflict("This finding has resolved verification evidence.");
        if (await db.ReconciliationReviews.CountAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.FindingId == id, ct) >= 100)
            return Conflict("The investigation note limit has been reached.");
        if (input.Reviewer is Guid reviewer && (!Grant(http).Grants.Contains(Coverage.Assign) || !await (
            from member in db.SubscriberMemberships
            join role in db.RolePermissions on member.RoleId equals role.RoleId
            where member.SubscriberId == subscriber && member.UserId == reviewer && role.PermissionId == Coverage.Review
                && (member.RoleId == Permissions.Owner || db.CompanyMemberships.Any(m => m.SubscriberId == subscriber && m.CompanyId == company && m.UserId == reviewer))
            select member).AnyAsync(ct))) return TenantAccess.Missing();
        var entry = new ReconciliationReview
        {
            SubscriberId = subscriber, CompanyId = company, FindingId = id, Actor = http.UserId(), Reviewer = input.Reviewer,
            Status = input.Status, ProtectedNote = engine.Evidence(subscriber, company).Protect(note)
        };
        db.ReconciliationReviews.Add(entry);
        db.Audit("Reconciliation.FindingReviewed", http.UserId(), subscriber, id);
        return Results.Ok(new { entry.Id, entry.Status, entry.At });
    }

    private static async Task<IResult> ListAccountingReferences(Guid subscriber, Guid company, Guid connection, string type, string? search, HttpContext http,
        EfiscalDbContext db, DevelopmentAccountingRead accounting, CancellationToken ct)
    {
        if (!MockAccess(http) || !DocumentRules.Types.Contains(type) || await AccountingLinks(db, subscriber, company)
            .SingleOrDefaultAsync(x => x.Id == connection, ct) is not { } link || await accounting.Authorize(link, ct) is null) return TenantAccess.Missing();
        var query = string.IsNullOrWhiteSpace(search) ? null : SpecImport.Text(search, 80);
        var rows = await accounting.References(link, type, query, ct);
        var saved = await (from r in db.AccountingReferences.AsNoTracking()
           join snap in db.AccountingObservations.AsNoTracking() on r.ObservationId equals snap.Id
           where r.SubscriberId == subscriber && r.CompanyId == company && snap.ConnectionId == connection && snap.Type == type
           orderby r.At descending
           select new { r.Id, r.Purpose, r.At, snap.Number, snap.BusinessDate, snap.ExternalId }).Take(40).ToArrayAsync(ct);
        return Results.Ok(new { items = rows.Select(x => new { x.Id, x.Number, x.Date, x.Type, x.Source,
            status = x.Values.Header.TryGetValue("status", out var status) ? status.GetString() : null }), saved,
            creditMemoEligibility = "Credit Memos require complete, verified original invoice and prior adjustment evidence." });
    }

    private static async Task<IResult> SaveAccountingReference(Guid subscriber, Guid company, ReferenceRequest input, HttpContext http, EfiscalDbContext db,
        DevelopmentAccountingRead accounting, ReconciliationEngine engine, CancellationToken ct)
    {
        if (!MockAccess(http) || !DocumentRules.Types.Contains(input.Type) ||
            await AccountingLinks(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == input.ConnectionId, ct) is not { } link)
            return TenantAccess.Missing();
        var purpose = SpecImport.Text(input.Purpose ?? "", 300);
        var externalId = SpecImport.Text(input.ExternalId ?? "", 120);
        if (purpose.Length == 0 || externalId.Length == 0) return ApiSupport.Invalid("reference", "Select a source and record a purpose.");
        var read = await accounting.Read(link, null, externalId, input.Type, ct);
        if (read.Status != "Complete" || read.Records.Length != 1) return TenantAccess.Missing();
        var source = read.Records[0];
        var observed = engine.Capture(link, source);
        var reference = new AccountingReference { SubscriberId = subscriber, CompanyId = company, ObservationId = observed.Id,
            ActorId = http.UserId(), Purpose = purpose };
        db.AccountingReferences.Add(reference);
        db.Audit("Reconciliation.ReferenceRetrieved", http.UserId(), subscriber, reference.Id);
        return Results.Ok(new { reference.Id, observation = SnapshotView(observed),
            invoiceSourceKey = input.Type == "SalesOrder" && source.Values.Lines?.Any(x => x.Values["remainingQuantity"].GetDecimal() > 0) == true ?
                ConnectionRuntime.SalesKey(link.Id, source.Id) : null,
            creditMemoEligible = DevelopmentAccountingRead.HasCompleteCreditHistory(source) && Grant(http).Grants.Contains(DocumentRules.Permission("CreditMemo", true)) });
    }

    private static async Task<IResult> SaveAccountingMatch(Guid subscriber, Guid company, MatchRequest input, HttpContext http, EfiscalDbContext db,
        DevelopmentAccountingRead accounting, ReconciliationEngine engine, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var snapshot = await db.AccountingObservations.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.Id == input.ObservationId, ct);
        var doc = await db.FiscalDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.Id == input.DocumentId, ct);
        if (snapshot is null || doc is null || await AccountingLinks(db, subscriber, company)
            .SingleOrDefaultAsync(x => x.Id == snapshot.ConnectionId, ct) is not { } link || await accounting.Authorize(link, ct) is null) return TenantAccess.Missing();
        var reason = SpecImport.Text(input.Reason ?? "", 800);
        if (reason.Length < 12 || doc.Type != snapshot.Type || doc.Date != snapshot.BusinessDate || link.BranchId is Guid branch && branch != doc.BranchId ||
            snapshot.Account != link.ExternalAccountId || snapshot.ExternalCompany != link.ExternalResourceId)
            return ApiSupport.Invalid("evidence", "Provide independent supporting evidence for the same business date, document type and company file.");
        var provider = await accounting.Read(link, null, snapshot.ExternalId, doc.Type, ct);
        if (provider.Status != "Complete" || provider.Records.Length != 1 || !provider.Records[0].Values.Header.TryGetValue("currency", out var currency) ||
            currency.ValueKind != JsonValueKind.String || currency.GetString() != doc.Currency)
            return Conflict("Current provider identity or currency could not be confirmed.");
        if (await db.SyncOperations.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == doc.Id, ct) ||
            await db.ExternalDocumentIdentities.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company &&
                (x.DocumentId == doc.Id || x.ConnectionId == link.Id && x.ExternalType == doc.Type && x.ExternalId == snapshot.ExternalId), ct) ||
            await db.AccountingMatches.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company &&
                (x.DocumentId == doc.Id || x.ConnectionId == link.Id && x.Type == doc.Type && x.ExternalId == snapshot.ExternalId), ct))
            return Conflict("The document or external identity has an existing accounting assignment.");
        var linkRecord = new AccountingMatch { SubscriberId = subscriber, CompanyId = company, DocumentId = doc.Id, ConnectionId = link.Id, Type = doc.Type,
            ObservationId = snapshot.Id, ExternalId = snapshot.ExternalId, ActorId = http.UserId(),
            ProtectedReason = engine.Evidence(subscriber, company).Protect(reason) };
        db.AccountingMatches.Add(linkRecord);
        db.Audit("Reconciliation.ManualMatchRecorded", http.UserId(), subscriber, doc.Id);
        return Results.Created("matches/" + linkRecord.Id, new { linkRecord.Id, linkRecord.DocumentId, linkRecord.ExternalId });
    }
}

