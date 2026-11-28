using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Reconciliation;

internal sealed class ReconciliationEngine(EfiscalDbContext db, DevelopmentAccountingRead accounting, IDataProtectionProvider protection)
{
    public IDataProtector Evidence(Guid subscriber, Guid company) => protection.CreateProtector("EMBM.Reconciliation.Evidence.v1",
        subscriber.ToString("N"), company.ToString("N"));

    public AccountingObservation Capture(ExternalConnection link, RetrievedAccounting row, int? normalizationVersion = null)
    {
        var values = Freeze(row.Values);
        var snapshot = new AccountingObservation
        {
            SubscriberId = link.SubscriberId, CompanyId = link.CompanyId, ConnectionId = link.Id, ConnectorVersionId = link.ConnectorVersionId,
            NormalizationVersion = normalizationVersion, Account = link.ExternalAccountId, ExternalCompany = link.ExternalResourceId, ExternalId = row.Id,
            Type = row.Type, Number = row.Number, BusinessDate = row.Date, Source = row.Source, ModifiedAt = row.ModifiedAt,
            Evidence = row.Evidence, Hash = DocumentRules.Digest(values),
            ProtectedValues = Evidence(link.SubscriberId, link.CompanyId).Protect(values.GetRawText())
        };
        db.AccountingObservations.Add(snapshot);
        return snapshot;
    }

    public AccountingValues ReadSnapshot(AccountingObservation snapshot) =>
        JsonSerializer.Deserialize<AccountingValues>(Evidence(snapshot.SubscriberId, snapshot.CompanyId).Unprotect(snapshot.ProtectedValues), SpecImport.Json)!;

    public ComparisonCell[] ReadCells(ReconciliationCheck row) => string.IsNullOrEmpty(row.ProtectedComparison) ? [] :
        JsonSerializer.Deserialize<ComparisonCell[]>(Evidence(row.SubscriberId, row.CompanyId).Unprotect(row.ProtectedComparison), SpecImport.Json) ?? [];

    public async Task<ReconciliationCheck> Document(ExternalConnection link, CoverageRevision? policy, Guid documentId, Guid? job, CancellationToken ct)
    {
        var subscriber = link.SubscriberId;
        var company = link.CompanyId;
        var doc = await db.FiscalDocuments.AsNoTracking().Include(x => x.Processing).Include(x => x.Snapshot).Include(x => x.Lines)
            .SingleAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == documentId, ct);
        var check = New(link, policy, job, "document:" + documentId.ToString("N"), doc.Type, doc.Date, doc.BranchId);
        check.DocumentId = doc.Id;
        check.Number = doc.Number;
        check.ExpectedTotal = doc.Total;
        check.Currency = doc.Currency;
        check.Synchronization = doc.Processing.Accounting;
        var pin = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber &&
            x.CompanyId == company && x.DocumentId == doc.Id, ct);
        var confirmed = await db.ExternalDocumentIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber &&
            x.CompanyId == company && x.DocumentId == doc.Id, ct);
        var manual = await db.AccountingMatches.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber &&
            x.CompanyId == company && x.DocumentId == doc.Id, ct);
        if (pin is not null && pin.ConnectionId != link.Id || manual is not null && manual.ConnectionId != link.Id ||
            confirmed is not null && (confirmed.ConnectionId != link.Id || confirmed.ExternalAccount != link.ExternalAccountId ||
                confirmed.ExternalCompany != link.ExternalResourceId || confirmed.ExternalType != doc.Type))
            return Finish(check, "AmbiguousMatch", "Complete", "Conclusive");
        if (confirmed is null && manual is null && (pin is null || doc.Processing.Accounting is
            "Pending" or "Queued" or "Processing" or "Submitted" or "AwaitingConfirmation" or "AwaitingMapping" or "RetryScheduled"))
            return Finish(check, "AwaitingSynchronization", "NotAttempted", "Pending");

        var read = await accounting.Read(link, manual is null ? pin?.Id : null, confirmed?.ExternalId ?? manual?.ExternalId, doc.Type, ct);
        if (read.Status != "Complete") return Finish(check, read.Status, read.Status, "Pending");
        if (read.Records.Length == 0)
            return confirmed is not null || manual is not null ?
                Finish(check, "MissingAccountingRecord", "Complete", "Conclusive") :
                Finish(check, "AwaitingSynchronization", "Complete", "Pending");

        var observations = read.Records.Select(row => Capture(link, row, pin?.ResponseMappingNumber)).ToArray();
        check.ObservationId = observations[0].Id;
        check.ExternalNumber = observations[0].Number;
        if (read.Records.Length > 1) return Finish(check, "DuplicateExternalRecords", "Complete", "Conclusive");
        var record = read.Records[0];
        if (confirmed is not null && confirmed.ExternalId != record.Id || manual is not null && manual.ExternalId != record.Id)
            return Finish(check, "AmbiguousMatch", "Complete", "Conclusive");

        var previous = await db.ReconciliationChecks.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.UnitKey == check.UnitKey && x.ObservationId != null).OrderByDescending(x => x.At).Select(x => x.ObservationId).FirstOrDefaultAsync(ct);
        if (previous is Guid last)
        {
            var hash = await db.AccountingObservations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == last)
                .Select(x => x.Hash).SingleAsync(ct);
            check.ExternalChanged = hash != observations[0].Hash;
        }
        if (record.Values.Header.TryGetValue("total", out var total) && total.ValueKind == JsonValueKind.Number && total.TryGetDecimal(out var actual))
            check.ActualTotal = actual;

        var expected = await Expected(link, doc, pin, ct);
        if (expected is null) return Finish(check, "PartialVerification", "Complete", "Partial");
        var comparison = ReconciliationComparison.Compare(expected, record.Values, policy?.AmountTolerance ?? 0);
        var cells = comparison.Cells;
        check.Status = comparison.Status;
        check.Retrieval = "Complete";
        check.Completeness = comparison.Completeness;
        check.ProtectedComparison = Evidence(subscriber, company).Protect(Freeze(cells).GetRawText());
        await AddFindings(check, cells.Where(x => x.State == "Mismatch").Select(x => (x.Category,
            x.Line is null ? x.Field : x.Line + "." + x.Field)), ct);
        if (check.Completeness == "Complete" && check.Status is "Matched" or "MatchedWithinTolerance")
            await ResolvePrior(check, ct, ExternalKey(link.Id, record.Type, record.Id));
        return check;
    }

    public async Task<string> Discover(ExternalConnection link, CoverageRevision policy, ReconciliationJob job, CancellationToken ct)
    {
        var from = job.From ?? policy.Start;
        var through = job.Through ?? policy.End ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        List<RetrievedAccounting> all = [];
        string? after = null;
        for (int page = 0; page < 10; page++)
        {
            var outcome = await accounting.Page(link, from, through, after, policy.BatchSize, ct);
            if (outcome.Status != "Complete") return outcome.Status;
            all.AddRange(outcome.Rows);
            if (outcome.HasMore)
            {
                after = outcome.Rows.Last().Id;
                continue;
            }
            foreach (var external in all)
            {
                var observed = Capture(link, external);
                var check = New(link, policy, job.Id, ExternalKey(link.Id, external.Type, external.Id), external.Type, external.Date, link.BranchId);
                check.ObservationId = observed.Id;
                check.ExternalNumber = external.Number;
                check.Coverage = Coverage.Classify(policy, external);
                if (external.Values.Header.TryGetValue("currency", out var currency)) check.Currency = currency.GetString();
                if (external.Values.Header.TryGetValue("total", out var tot) && tot.TryGetDecimal(out var amount)) check.ActualTotal = amount;
                if (check.Coverage != "Eligible")
                {
                    Finish(check, check.Coverage, "Complete", check.Coverage == "UnknownCoverage" ? "Pending" : "Excluded");
                    continue;
                }
                var match = await db.AccountingMatches.AsNoTracking().Where(x => x.SubscriberId == link.SubscriberId && x.CompanyId == link.CompanyId &&
                    x.ConnectionId == link.Id && x.ExternalId == external.Id && x.Type == external.Type).Select(x => (Guid?)x.DocumentId).SingleOrDefaultAsync(ct);
                var found = match ?? await db.ExternalDocumentIdentities.AsNoTracking().Where(x => x.SubscriberId == link.SubscriberId &&
                    x.CompanyId == link.CompanyId && x.ConnectionId == link.Id && x.ExternalId == external.Id && x.ExternalType == external.Type &&
                    x.ExternalAccount == link.ExternalAccountId && x.ExternalCompany == link.ExternalResourceId)
                    .Select(x => (Guid?)x.DocumentId).SingleOrDefaultAsync(ct);
                if (found is not null)
                {
                    Finish(check, "CounterpartLinked", "Complete", "Excluded");
                }
                else Finish(check, "MissingEfiscalRecord", "Complete", "Conclusive");
            }
            return "Complete";
        }
        return "IncompleteRetrieval";
    }

    public static string ExternalKey(Guid connection, string type, string id) => $"external:{connection:N}:{type}:{id}";

    private async Task<AccountingValues?> Expected(ExternalConnection link, FiscalDocument doc, SyncOperation? pin, CancellationToken ct)
    {
        if (pin is null) return null;
        string? relationship = null;
        if (doc.SourceDocumentId is Guid source)
            relationship = await db.ExternalDocumentIdentities.AsNoTracking().Where(x => x.SubscriberId == link.SubscriberId &&
                x.CompanyId == link.CompanyId && x.ConnectionId == link.Id && x.DocumentId == source).Select(x => x.ExternalId).SingleOrDefaultAsync(ct);
        else if (doc.ExternalOrderKey is string external)
            relationship = external.StartsWith("so-" + link.Id.ToString("N") + "-", StringComparison.Ordinal) ? external[36..] : doc.ExternalOrderNumber;
        var committed = FieldMapper.Document(doc, relationship);
        var rev = await db.MappingVersions.AsNoTracking().SingleAsync(x => x.SubscriberId == link.SubscriberId && x.CompanyId == link.CompanyId &&
            x.ConnectorVersionId == pin.ConnectorVersionId && x.ProfileId == pin.ProfileId && x.Number == pin.MappingNumber, ct);
        try
        {
            var mapped = FieldMapper.Map(FieldMapper.Outbound, rev.Rules.Deserialize<FieldRule[]>(SpecImport.Json) ?? [],
                committed, rev.ReferenceSnapshot.Deserialize<BoundReference[]>(SpecImport.Json) ?? []);
            var expected = DevelopmentAccountingRead.Posted(doc, mapped).Deserialize<AccountingValues>(SpecImport.Json)!;
            expected.Header["status"] = Freeze("Posted");
            return expected;
        }
        catch (ConnectorInputError) { return null; }
    }

    private async Task AddFindings(ReconciliationCheck check, IEnumerable<(string Category, string Field)> entries, CancellationToken ct)
    {
        var findings = entries.Distinct().ToArray();
        foreach (var entry in findings)
            db.ReconciliationFindings.Add(new()
            {
                SubscriberId = check.SubscriberId, CompanyId = check.CompanyId, CheckId = check.Id, UnitKey = check.UnitKey,
                Category = entry.Category, Field = entry.Field
            });
        check.FindingCount = findings.Length;
        check.Categories = findings.Select(x => x.Category).Distinct().ToArray();
        if (check.Status is not ("Matched" or "MatchedWithinTolerance") || check.Completeness != "Complete") return;
        await ResolvePrior(check, ct);
    }

    private async Task ResolvePrior(ReconciliationCheck check, CancellationToken ct, string? unitKey = null)
    {
        var key = unitKey ?? check.UnitKey;
        var old = await db.ReconciliationFindings.AsNoTracking().Where(x => x.SubscriberId == check.SubscriberId &&
            x.CompanyId == check.CompanyId && x.UnitKey == key &&
            !db.ReconciliationReviews.Any(r => r.FindingId == x.Id && r.Status == "ResolvedAfterVerification")).ToArrayAsync(ct);
        db.ReconciliationReviews.AddRange(old.Select(x => new ReconciliationReview
        {
            SubscriberId = check.SubscriberId, CompanyId = check.CompanyId, FindingId = x.Id,
            VerificationId = check.Id, Status = "ResolvedAfterVerification"
        }));
    }

    private ReconciliationCheck New(ExternalConnection link, CoverageRevision? coverage, Guid? job, string key, string type, DateOnly? date, Guid? branch)
    {
        var check = new ReconciliationCheck
        {
            SubscriberId = link.SubscriberId, CompanyId = link.CompanyId, ConnectionId = link.Id, CoverageId = coverage?.Id,
            JobId = job, Type = type, BusinessDate = date, UnitKey = key, BranchId = branch,
            FreshUntil = DateTimeOffset.UtcNow.AddHours(coverage?.FreshnessHours ?? 48)
        };
        db.ReconciliationChecks.Add(check);
        return check;
    }

    private ReconciliationCheck Finish(ReconciliationCheck row, string status, string retrieval, string completeness)
    {
        row.Status = status;
        row.Retrieval = retrieval;
        row.Completeness = completeness;
        if (status is "MissingAccountingRecord" or "MissingEfiscalRecord" or "DuplicateExternalRecords" or "AmbiguousMatch")
        {
            row.Categories = [status];
            row.FindingCount = 1;
            db.ReconciliationFindings.Add(new()
            {
                SubscriberId = row.SubscriberId, CompanyId = row.CompanyId, CheckId = row.Id, UnitKey = row.UnitKey,
                Category = status, Field = "document"
            });
        }
        return row;
    }
}

