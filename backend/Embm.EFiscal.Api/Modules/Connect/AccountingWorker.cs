using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record SyncResult(string State, string? Category = null, int? Delay = null, MockAccountingRecord? Record = null);

internal sealed class AccountingWorker(IServiceScopeFactory services, ILogger<AccountingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { for (var i = 0; i < 8 && await ProcessOne(ct); i++) { } }
            catch (Exception e) when (!ct.IsCancellationRequested)
            { log.LogWarning("Accounting worker will recover its lease after {ErrorType}", e.GetType().Name); }
        }
    }

    internal async Task<bool> ProcessOne(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfiscalDbContext>();
        var now = DateTimeOffset.UtcNow;
        DocumentOutboxMessage job;
        SyncOperation operation;
        Guid attemptId;
        bool recovery;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var available = await db.DocumentOutboxMessages.FromSqlInterpolated($"""
                SELECT j.* FROM "DocumentOutboxMessages" j
                JOIN "SyncOperations" o ON o."Id" = j."Id" AND o."SubscriberId" = j."SubscriberId" AND o."CompanyId" = j."CompanyId"
                WHERE j."Kind" = 'Accounting' AND
                    ((j."State" IN ('Pending','RetryScheduled','VerifyPending') AND j."AvailableAt" <= {now})
                    OR (j."State" = 'Processing' AND j."LeaseUntil" <= {now}))
                ORDER BY j."AvailableAt",j."CreatedAt" LIMIT 1 FOR UPDATE OF j SKIP LOCKED
                """).ToListAsync(ct);
            if (available.Count == 0) return false;
            job = available[0];
            recovery = job.State == "VerifyPending" || job.State == "Processing" && job.Attempts >= 5;
            operation = await db.SyncOperations.AsNoTracking().SingleAsync(x => x.Id == job.Id, ct);
            job.State = "Processing";
            job.LeaseToken = Guid.NewGuid();
            job.LeaseUntil = now.AddMinutes(1);
            job.Attempts++;
            var attempt = new SyncAttempt { SubscriberId = job.SubscriberId, CompanyId = job.CompanyId, OperationId = job.Id,
                Number = job.Attempts, MappingNumber = operation.MappingNumber, StartedAt = now };
            attemptId = attempt.Id;
            db.SyncAttempts.Add(attempt);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        db.ChangeTracker.Clear();
        await db.DocumentProcessing.Where(x => x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId && x.DocumentId == job.DocumentId)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.Accounting, "Processing"), ct);
        var doc = await db.FiscalDocuments.AsNoTracking().Include(x => x.Snapshot).Include(x => x.Lines).SingleAsync(x =>
            x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId && x.Id == job.DocumentId, ct);
        var runtime = scope.ServiceProvider.GetRequiredService<ConnectionRuntime>();
        SyncResult result;
        ExternalConnection? link = null;
        try
        {
            if (!doc.DevelopmentFixture) result = new("RequiresReview", "DevelopmentOnly");
            else if (await runtime.Active(job.SubscriberId, job.CompanyId, operation.ConnectionId, [doc.Type + "Create"], false, ct) is not { } provider
                || provider.Connection.ConnectorVersionId != operation.ConnectorVersionId || provider.Adapter.Key != "mock-accounting")
                result = new(operation.SubmittedAt.HasValue || recovery ? "UncertainExternalOutcome" : "AwaitingConnection", "ConnectionUnavailable");
            else
            {
                link = provider.Connection;
                result = await Submit(db, job, operation, doc, provider, recovery, ct);
            }
        }
        catch (ConnectorInputError)
        { result = new(operation.SubmittedAt.HasValue ? "UncertainExternalOutcome" : "RequiresReview", "MappingOrSchemaMismatch"); }

        await Finish(db, job, operation, attemptId, doc, link, result, ct);
        return true;
    }

    private async Task<SyncResult> Submit(EfiscalDbContext db, DocumentOutboxMessage job, SyncOperation op, FiscalDocument doc,
        AuthorizedProvider provider, bool verifyOnly, CancellationToken ct)
    {
        var existing = await ReadExternal(db, op, ct);
        if (existing is not null) return await Verify(db, op, doc, existing, ct);
        if (verifyOnly || op.SubmittedAt.HasValue) return new("UncertainExternalOutcome", "ReadbackUnavailable");
        var mapping = await db.MappingVersions.AsNoTracking().SingleAsync(x => x.SubscriberId == op.SubscriberId && x.CompanyId == op.CompanyId &&
            x.ProfileId == op.ProfileId && x.Number == op.MappingNumber && x.ConnectorVersionId == op.ConnectorVersionId, ct);
        var connection = provider.Connection;
        var dependency = await SourceIdentity(db, doc, provider, ct);
        if (dependency.Error is not null)
            return dependency.Error == "SourceStillPending" && job.Attempts < 5 ? new("RetryScheduled", dependency.Error, Backoff(job.Attempts)) :
                new("RequiresReview", dependency.Error);
        var references = mapping.ReferenceSnapshot.Deserialize<BoundReference[]>(SpecImport.Json) ?? [];
        var rules = mapping.Rules.Deserialize<FieldRule[]>(SpecImport.Json) ?? [];
        var data = FieldMapper.Map(FieldMapper.Outbound, rules, FieldMapper.Document(doc, dependency.Identity), references);
        if (dependency.Identity is not null && !Equals(data.Header.GetValueOrDefault("relationship"), dependency.Identity))
            return new("RequiresReview", "ProtectedRelationshipMismatch");
        if (!AuthorizedReferences(doc, data, references))
            return new("RequiresReview", "UnmappedCompanyReference");

        var policy = await db.ExecutionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ConnectorVersionId == connection.ConnectorVersionId
            && x.Capability == doc.Type + "Create" && x.Method == "POST" && x.Path == "/v1/accounting/documents", ct);
        if (policy is null) return new("RequiresReview", "EndpointApprovalMissing");
        if (op.Scenario == "Transient" && job.Attempts == 1)
            return new("RetryScheduled", "RateLimited", Backoff(job.Attempts));

        var payload = Freeze(data);
        await db.SyncOperations.Where(x => x.Id == op.Id && x.SubmittedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.SubmittedAt, DateTimeOffset.UtcNow), ct);
        await Position(db, job, "Submitted", ct);
        var submitted = await ControlledExecution.Execute(policy, payload, op.Id, async (request, token) =>
        {
            if (request.Headers.GetValues("Idempotency-Key").SingleOrDefault() != op.Id.ToString("N"))
                throw new InvalidOperationException("The sandbox idempotency key is missing.");
            var record = await PersistExternal(op, doc, data, payload, token);
            if (op.Scenario is "TimeoutAfterWrite" or "UncertainAfterWrite") await Task.Delay(7000, token);
            return new(201, Freeze(new { record.ExternalId, record.ExternalNumber, record.Status }));
        }, ct);
        if (op.Scenario == "UncertainAfterWrite" && submitted.Status == "Timeout")
            return new("UncertainExternalOutcome", "ReadbackTemporarilyUnavailable");
        await Position(db, job, "AwaitingConfirmation", ct);
        var readback = await ReadExternal(db, op, ct);
        if (readback is not null) return await Verify(db, op, doc, readback, ct);
        return new("UncertainExternalOutcome", submitted.Status);
    }

    private static bool AuthorizedReferences(FiscalDocument doc, MappingValues data, BoundReference[] references)
    {
        bool Matches(string kind, string local, object? mapped) =>
            references.Any(v => v.Kind == kind && v.LocalKey == local && Equals(v.ExternalId, mapped));

        if (!Matches("Customer", doc.CustomerId.ToString(), data.Header.GetValueOrDefault("customer"))) return false;
        return doc.Lines.OrderBy(x => x.Position).Zip(data.Lines).All(pair =>
            Matches("Item", pair.First.ItemId.ToString(), pair.Second.GetValueOrDefault("item")) &&
            Matches("Unit", pair.First.UnitId.ToString(), pair.Second.GetValueOrDefault("unit")));
    }

    private async Task<MockAccountingRecord> PersistExternal(SyncOperation op, FiscalDocument doc, MappingValues projected, JsonElement payload, CancellationToken ct)
    {
        await using var external = services.CreateAsyncScope();
        var store = external.ServiceProvider.GetRequiredService<EfiscalDbContext>();
        await using var tx = await store.Database.BeginTransactionAsync(ct);
        await store.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({op.Id.ToString()}, 83341))", ct);
        var row = await ReadExternal(store, op, ct);
        if (row is not null) return row;
        row = new MockAccountingRecord { OperationId = op.Id, SubscriberId = op.SubscriberId, CompanyId = op.CompanyId,
            ConnectionId = op.ConnectionId, DocumentId = op.DocumentId, Number = doc.Number, ExternalType = doc.Type,
            ExternalId = "dev-" + op.Id.ToString("N"), ExternalNumber = "ACCT-" + doc.Number,
            Total = (decimal)projected.Header["total"]! + (op.Scenario == "Variance" ? .01m : 0m), Relationship = projected.Header.GetValueOrDefault("relationship") as string,
            PayloadHash = DocumentRules.Digest(payload), PostedState = DevelopmentAccountingRead.Posted(doc, projected) };
        store.MockAccountingRecords.Add(row);
        await store.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return row;
    }

    private static Task<MockAccountingRecord?> ReadExternal(EfiscalDbContext db, SyncOperation op, CancellationToken ct) =>
        db.MockAccountingRecords.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == op.Id && x.SubscriberId == op.SubscriberId &&
            x.CompanyId == op.CompanyId && x.ConnectionId == op.ConnectionId && x.DocumentId == op.DocumentId, ct);
    private static async Task Position(EfiscalDbContext db, DocumentOutboxMessage job, string status, CancellationToken ct)
    {
        await db.DocumentProcessing.Where(x => x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId &&
            x.DocumentId == job.DocumentId && db.DocumentOutboxMessages.Any(j => j.Id == job.Id && j.LeaseToken == job.LeaseToken))
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.Accounting, status), ct);
    }

    private static async Task<SyncResult> Verify(EfiscalDbContext db, SyncOperation operation, FiscalDocument doc, MockAccountingRecord record, CancellationToken ct)
    {
        if (doc.Number != record.Number || doc.Type != record.ExternalType || doc.Total != record.Total || record.Status != "Posted")
            return new("RequiresReview", "ExternalFinancialVariance", Record: record);
        if (operation.ResponseProfileId is Guid profile && operation.ResponseMappingNumber is int number)
        {
            var mapped = await db.MappingVersions.AsNoTracking().SingleAsync(x => x.SubscriberId == operation.SubscriberId &&
                x.CompanyId == operation.CompanyId && x.ProfileId == profile && x.Number == number && x.ConnectorVersionId == operation.ConnectorVersionId, ct);
            try
            {
                var values = FieldMapper.Map(FieldMapper.Response, mapped.Rules.Deserialize<FieldRule[]>(SpecImport.Json) ?? [],
                    new(FieldMapper.ReadFields(Freeze(new { record.ExternalId, record.ExternalNumber, record.Status })), []), []);
                if (!Equals(values.Header.GetValueOrDefault("externalId"), record.ExternalId) ||
                    !Equals(values.Header.GetValueOrDefault("externalNumber"), record.ExternalNumber) ||
                    !Equals(values.Header.GetValueOrDefault("status"), record.Status))
                    return new("RequiresReview", "ResponseIdentityMismatch", Record: record);
            }
            catch (ConnectorInputError) { return new("RequiresReview", "ResponseMappingInvalid", Record: record); }
        }
        return new("Synchronized", Record: record);
    }
    private static int Backoff(int attempt) => Math.Min(120, 3 * (1 << Math.Min(5, attempt)));

    private static async Task<(string? Identity, string? Error)> SourceIdentity(EfiscalDbContext db, FiscalDocument doc, AuthorizedProvider provider, CancellationToken ct)
    {
        if (doc.SourceDocumentId is Guid id)
        {
            if (doc.Type == "SalesInvoice" && !ConnectionRuntime.Capable(provider.Connection.Version, provider.Adapter, "InvoiceOrderLink"))
                return (null, "RelationshipCapabilityMissing");
            var identity = await db.ExternalDocumentIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == doc.SubscriberId &&
                x.CompanyId == doc.CompanyId && x.DocumentId == id, ct);
            if (identity is null) return (null, "SourceStillPending");
            return identity.ConnectionId == provider.Connection.Id ? (identity.ExternalId, null) : (null, "SourceDestinationMismatch");
        }
        if (HistoricalCreditLink.Of(doc) is { } historical)
            return historical.Matches(provider.Connection) ? (historical.RecordId, null) : (null, "SourceDestinationMismatch");
        if (doc.ExternalOrderKey is not { } key) return (null, null);
        if (key.StartsWith("shop-", StringComparison.Ordinal))
            return (doc.ExternalOrderNumber, null);
        if (!ConnectionRuntime.Capable(provider.Connection.Version, provider.Adapter, "InvoiceOrderLink"))
            return (null, "RelationshipCapabilityMissing");
        if (!key.StartsWith("so-" + provider.Connection.Id.ToString("N") + "-", StringComparison.Ordinal))
            return (null, "SourceDestinationMismatch");
        var source = provider.Adapter.ReadOrders(provider.Connection.ExternalResourceId, null).SingleOrDefault(x => x.Id == key[36..]);
        return source is null ? (null, "SourceReferenceUnavailable") : (source.Id, null);
    }

    private static async Task Finish(EfiscalDbContext db, DocumentOutboxMessage job, SyncOperation op, Guid attemptId, FiscalDocument document,
        ExternalConnection? link, SyncResult result, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(job.SubscriberId, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "DocumentOutboxMessages" WHERE "Id" = {job.Id} FOR UPDATE""", ct);
        var current = await db.DocumentOutboxMessages.SingleAsync(x => x.Id == job.Id && x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId, ct);
        if (current.LeaseToken != job.LeaseToken) return;
        var state = await db.DocumentProcessing.SingleAsync(x => x.SubscriberId == job.SubscriberId &&
            x.CompanyId == job.CompanyId && x.DocumentId == job.DocumentId, ct);
        var currentOp = await db.SyncOperations.SingleAsync(x => x.Id == op.Id && x.SubscriberId == op.SubscriberId && x.CompanyId == op.CompanyId, ct);
        var attempt = await db.SyncAttempts.SingleAsync(x => x.Id == attemptId, ct);
        var now = DateTimeOffset.UtcNow;
        current.State = result.State;
        current.LeaseToken = null;
        current.LeaseUntil = null;
        if (result.Delay.HasValue) current.AvailableAt = now.AddSeconds(result.Delay.Value);
        state.Accounting = result.State;
        attempt.Status = result.State;
        attempt.Category = result.Category;
        attempt.Evidence = result.Record?.ExternalNumber;
        attempt.CompletedAt = now;
        if (result.State == "Synchronized" && result.Record is { } record && link is not null)
        {
            currentOp.ConfirmedAt = now;
            if (!await db.ExternalDocumentIdentities.AnyAsync(x => x.SubscriberId == op.SubscriberId && x.CompanyId == op.CompanyId && x.DocumentId == op.DocumentId, ct))
                db.ExternalDocumentIdentities.Add(new ExternalDocumentIdentity { SubscriberId = op.SubscriberId, CompanyId = op.CompanyId,
                    DocumentId = op.DocumentId, OperationId = op.Id, ConnectionId = op.ConnectionId, InternalNumber = document.Number,
                    ExternalAccount = link.ExternalAccountId, ExternalCompany = link.ExternalResourceId, ExternalType = record.ExternalType,
                    ExternalId = record.ExternalId, ExternalNumber = record.ExternalNumber, Status = record.Status, ConnectorVersionId = op.ConnectorVersionId,
                    MappingNumber = op.MappingNumber, EvidenceHash = record.PayloadHash, SubmittedAt = currentOp.SubmittedAt ?? record.CreatedAt, ConfirmedAt = now });
        }
        db.Audit("Accounting." + result.State, subscriber: op.SubscriberId, target: document.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

