using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class ReportingOperationWorker(IServiceScopeFactory scopes, IWebHostEnvironment environment,
    ILogger<ReportingOperationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessOne(ct)) await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                log.LogWarning("Reporting operation worker will recover its lease after {Category}", error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    internal async Task<bool> ProcessOne(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfiscalDbContext>();
        var lease = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Guid id;
        Guid subscriber;
        await using (var claim = await db.Database.BeginTransactionAsync(ct))
        {
            var rows = await db.Set<ReportingOperation>().FromSqlInterpolated($"""
                SELECT * FROM "ReportingOperation"
                WHERE ("State" IN ('Queued','RetryScheduled','AwaitingAcknowledgment') AND "AvailableAt" <= {now})
                   OR ("State" = 'Processing' AND "LeaseUntil" <= {now})
                ORDER BY "AvailableAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToArrayAsync(ct);
            if (rows is not [var row]) return false;
            id = row.Id;
            subscriber = row.SubscriberId;
            row.State = "Processing";
            row.Lease = lease;
            row.LeaseUntil = now.AddMinutes(1);
            row.Attempts++;
            await db.SaveChangesAsync(ct);
            await claim.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        var claimed = await db.Set<ReportingOperation>().FromSqlInterpolated($"""
            SELECT * FROM "ReportingOperation" WHERE "Id" = {id} AND "Lease" = {lease} FOR UPDATE
            """).ToArrayAsync(ct);
        if (claimed is not [var operation]) return true;
        if (operation.Attempts > 8)
        {
            Complete(operation, "RequiresReview", "LeaseRecoveryLimit");
        }
        else if (operation.Mode == "Preparation")
        {
            await Prepare(db, operation, ct);
        }
        else if (environment.IsDevelopment() && operation.Mode == "DevelopmentSimulation")
        {
            await RunSimulation(db, operation, ct);
        }
        else
        {
            Complete(operation, "RequiresReview", "ModeUnavailable");
        }
        operation.Lease = null;
        operation.LeaseUntil = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private static async Task Prepare(EfiscalDbContext db, ReportingOperation row, CancellationToken ct)
    {
        var prep = await db.ReportingPreparations.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == row.SubscriberId &&
            x.CompanyId == row.CompanyId && x.DocumentId == row.DocumentId && x.SchemaVersion == row.SchemaVersion, ct);
        if (prep is null)
        {
            var doc = await db.FiscalDocuments.AsNoTracking().Include(x => x.Snapshot).Include(x => x.Lines).SingleAsync(x =>
                x.SubscriberId == row.SubscriberId && x.CompanyId == row.CompanyId && x.BranchId == row.BranchId &&
                x.Id == row.DocumentId && (x.Type == "SalesInvoice" || x.Type == "CreditMemo"), ct);
            prep = ReportingGateway.BuildPreparation(doc, row.Actor);
            db.Add(prep);
        }
        if (!ReportingOperations.Integrity(prep))
        {
            Complete(row, "ValidationFailed", "PreparationIntegrity");
            return;
        }
        row.PayloadHash = prep.Sha256;
        Complete(row, "Prepared");
        Record(db, row, 1, "Prepare", null);
        db.Audit("Reporting.PreparationJobCompleted", row.Actor, row.SubscriberId, row.Id);
    }

    private static async Task RunSimulation(EfiscalDbContext db, ReportingOperation row, CancellationToken ct)
    {
        var connector = await db.Set<RegulatoryConnectorVersion>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.Number == row.ConnectorVersion && x.Environment == "DevelopmentSimulation", ct);
        var canonical = await db.ReportingPreparations.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == row.SubscriberId &&
            x.CompanyId == row.CompanyId && x.DocumentId == row.DocumentId && x.SchemaVersion == row.SchemaVersion, ct);
        if (connector is null || canonical is null || row.MappingVersion != RegulatoryConnector.MappingVersion ||
            row.SourceHash != canonical.Sha256 || !ReportingOperations.Integrity(canonical) ||
            row.PayloadHash != DocumentRules.Digest(RegulatoryConnector.Payload(JsonSerializer.Deserialize<JsonElement>(canonical.Content))) ||
            !await db.FiscalDocuments.AnyAsync(x => x.SubscriberId == row.SubscriberId && x.CompanyId == row.CompanyId &&
                x.Id == row.DocumentId && x.BranchId == row.BranchId && x.DevelopmentFixture, ct))
        {
            Complete(row, "ValidationFailed", "PinnedPayloadIntegrity");
            return;
        }
        var history = await db.Set<ReportingOperationAttempt>().AsNoTracking().Where(x => x.OperationId == row.Id)
            .OrderBy(x => x.Sequence).ToArrayAsync(ct);
        var sequence = history.Length + 1;
        var action = row.VerifyOnly ? "Verify" : "Submit";
        var spec = connector.Specification.Deserialize<SpecReview>(SpecImport.Json);
        if (row.VerifyOnly)
        {
            if (spec?.Endpoints.Any(x => x.Path is "/sim/status" or "/sim/acknowledgments") != true ||
                row.Scenario is "StatusUnavailable" or "InvalidResponse")
                Complete(row, "RequiresReview", "SimulatedStatusUnavailable");
            else
                Complete(row, "SimulatedAcknowledged");
        }
        else if (!await RegulatoryConnector.LatestEvents(db).AnyAsync(x => x.Version == connector.Number && x.State == "Active", ct))
        {
            Complete(row, "RequiresReview", "PinnedSimulatorInactive");
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({connector.Number}, 591103)", ct);
            var threshold = DateTimeOffset.UtcNow.AddMinutes(-1);
            var recent = await (from attempt in db.Set<ReportingOperationAttempt>()
                                join operation in db.Set<ReportingOperation>() on attempt.OperationId equals operation.Id
                                where operation.ConnectorVersion == connector.Number && attempt.Action == "Submit" &&
                                    attempt.At >= threshold && attempt.Outcome != "LocalRateLimit"
                                select attempt).CountAsync(ct);
            if (recent >= connector.RateLimit)
            {
                if (row.Attempts >= 8) Complete(row, "RequiresReview", "LocalRateLimit");
                else { Complete(row, "RetryScheduled", "LocalRateLimit"); row.AvailableAt = DateTimeOffset.UtcNow.AddMinutes(1); }
                action = "Throttle";
            }
            else ApplyOutcome(row, connector, history.Count(x => x.Action == "Submit") + 1);
        }
        Record(db, row, sequence, action, row.State == "SimulatedAcknowledged" ? "SIM-BIR-ACK-" + row.Id.ToString("N") : null);
        db.Audit("Reporting.Simulation." + row.State, row.Actor, row.SubscriberId, row.Id);
    }

    private static void ApplyOutcome(ReportingOperation row, RegulatoryConnectorVersion connector, int attempt)
    {
        switch (row.Scenario)
        {
            case "Processing":
            case "DelayedAcknowledgment":
                row.VerifyOnly = true;
                Complete(row, "AwaitingAcknowledgment");
                row.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(3);
                break;
            case "ValidationRejected":
                Complete(row, "SimulatedRejected", "SyntheticValidationRejection");
                break;
            case "AuthenticationFailed":
                Complete(row, "RequiresReview", "SyntheticAuthenticationFailure");
                break;
            case "TimeoutAfterAcceptance":
            case "InvalidResponse":
            case "StatusUnavailable":
            case "Duplicate":
                Complete(row, "UncertainOutcome", row.Scenario);
                break;
            case "TemporaryOutage":
            case "RateLimited" or "TimeoutBeforeSubmission" when attempt == 1:
                if (attempt >= connector.Attempts) Complete(row, "RequiresReview", row.Scenario);
                else
                {
                    Complete(row, "RetryScheduled", row.Scenario);
                    row.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(3 * attempt);
                }
                break;
            case "Accepted":
            case "RateLimited":
            case "TimeoutBeforeSubmission":
                Complete(row, "SimulatedAcknowledged");
                break;
            default:
                Complete(row, "RequiresReview", "UnsupportedScenario");
                break;
        }
    }

    private static void Complete(ReportingOperation row, string state, string? failure = null)
    {
        row.State = state;
        row.Failure = failure;
    }

    private static void Record(EfiscalDbContext db, ReportingOperation row, int sequence, string action, string? reference)
    {
        var hash = DocumentRules.Digest(new { row.Id, sequence, action, outcome = row.State, row.Failure, reference,
            row.Mode, row.ConnectorVersion, row.PayloadHash, governmentStatus = "NotSubmitted" });
        db.Add(new ReportingOperationAttempt { OperationId = row.Id, Sequence = sequence, Action = action,
            Outcome = row.State, Reference = reference, ResponseHash = hash });
    }
}
