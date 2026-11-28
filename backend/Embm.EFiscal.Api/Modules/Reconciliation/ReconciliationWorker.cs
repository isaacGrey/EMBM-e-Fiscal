using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reconciliation;

internal sealed class ReconciliationWorker(IServiceScopeFactory services, ILogger<ReconciliationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Schedule(ct);
                if (!await ProcessOne(ct)) await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                log.LogWarning("Reconciliation processing could not complete ({Category})", e.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    internal async Task<bool> ProcessOne(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfiscalDbContext>();
        var lease = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Guid id;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var rows = await db.ReconciliationJobs.FromSqlInterpolated($"""
                SELECT * FROM "ReconciliationJobs"
                WHERE (("State" IN ('Queued', 'RetryScheduled') AND "AvailableAt" <= {now})
                    OR ("State" = 'Processing' AND "LeaseUntil" <= {now}))
                ORDER BY "AvailableAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToArrayAsync(ct);
            if (rows.Length == 0) return false;
            var job = rows[0];
            id = job.Id;
            job.State = "Processing";
            job.Lease = lease;
            job.LeaseUntil = now.AddMinutes(2);
            job.Attempts++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.ReconciliationJobs.FromSqlInterpolated($"""
                SELECT * FROM "ReconciliationJobs" WHERE "Id" = {id} AND "Lease" = {lease} FOR UPDATE
                """).ToArrayAsync(ct);
            if (rows.Length == 0) return true;
            var job = rows[0];
            var link = await db.ExternalConnections.AsNoTracking().SingleAsync(x => x.Id == job.ConnectionId &&
                x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId, ct);
            var policy = job.CoverageId is Guid policyId ? await Coverage.For(db, job.SubscriberId, job.CompanyId)
                .AsNoTracking().SingleAsync(x => x.Id == policyId, ct) : null;
            var engine = scope.ServiceProvider.GetRequiredService<ReconciliationEngine>();
            string outcome;
            if (job.DocumentId is Guid document)
            {
                var check = await engine.Document(link, policy, document, job.Id, ct);
                outcome = check.Retrieval;
                if (outcome == "RateLimited" && job.Attempts < 4)
                {
                    db.ReconciliationChecks.Remove(check);
                    JobResult(job, outcome, true);
                }
                else JobResult(job, outcome == "NotAttempted" ? "Complete" : outcome);
            }
            else if (job.Kind == "Discovery" && policy is not null)
            {
                outcome = await engine.Discover(link, policy, job, ct);
                JobResult(job, outcome, outcome == "RateLimited");
            }
            else
            {
                outcome = await Batch(db, engine, link.Id, policy, job, ct);
                JobResult(job, outcome, outcome == "RateLimited");
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Reconciliation job could not complete ({Category})", e.GetType().Name);
            db.ChangeTracker.Clear();
            var retry = await db.ReconciliationJobs.SingleOrDefaultAsync(x => x.Id == id && x.Lease == lease, ct);
            if (retry is not null) { JobResult(retry, "WorkerFailure", true); await db.SaveChangesAsync(ct); }
        }
        return true;
    }

    private static void JobResult(ReconciliationJob job, string result, bool retry = false)
    {
        var now = DateTimeOffset.UtcNow;
        job.Lease = null;
        job.LeaseUntil = null;
        job.Failure = result is "Complete" or "Continue" ? null : result;
        if (result == "Continue")
        {
            job.State = "Queued";
            job.AvailableAt = now;
            job.Attempts = 0;
        }
        else if (retry && job.Attempts < 4)
        {
            job.State = "RetryScheduled";
            job.AvailableAt = now.AddSeconds(3 * (1 << job.Attempts));
        }
        else
        {
            job.State = result == "Complete" ? "Completed" : "Failed";
            job.CompletedAt = now;
        }
    }

    private static async Task<string> Batch(EfiscalDbContext db, ReconciliationEngine engine, Guid connection, CoverageRevision? policy,
        ReconciliationJob job, CancellationToken ct)
    {
        var cursor = job.DocumentCursor ?? Guid.Empty;
        var link = await db.ExternalConnections.AsNoTracking().SingleAsync(x => x.Id == connection && x.SubscriberId == job.SubscriberId
            && x.CompanyId == job.CompanyId, ct);
        var ids = await (from pin in db.SyncOperations.AsNoTracking()
            join document in db.FiscalDocuments.AsNoTracking() on pin.DocumentId equals document.Id
            where pin.SubscriberId == job.SubscriberId && pin.CompanyId == job.CompanyId && pin.ConnectionId == connection &&
                pin.DocumentId.CompareTo(cursor) > 0 && (job.From == null || document.Date >= job.From) &&
                (job.Through == null || document.Date <= job.Through)
            orderby pin.DocumentId
            select pin.DocumentId).Take(policy?.BatchSize ?? 25).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            var check = await engine.Document(link, policy, id, job.Id, ct);
            if (check.Retrieval == "RateLimited")
            {
                if (job.Attempts < 4) db.ReconciliationChecks.Remove(check);
                return check.Retrieval;
            }
            job.DocumentCursor = id;
            job.Attempts = 1;
        }
        if (ids.Length < (policy?.BatchSize ?? 25)) return "Complete";
        return "Continue";
    }

    private async Task Schedule(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfiscalDbContext>();
        var now = DateTimeOffset.UtcNow;
        var active = Coverage.Active(db.CoverageRevisions.AsNoTracking(), now);
        var periodic = await active.Where(x => x.IntervalHours != null).OrderBy(x => x.Id).Take(100).ToArrayAsync(ct);
        var automatic = await (from policy in active
            join pin in db.SyncOperations.AsNoTracking() on policy.ConnectionId equals pin.ConnectionId
            join document in db.FiscalDocuments.AsNoTracking() on pin.DocumentId equals document.Id
            where policy.AfterSynchronization && pin.ConfirmedAt != null && pin.ConfirmedAt >= policy.EffectiveAt &&
                policy.Types.Contains(document.Type) &&
                !db.ReconciliationJobs.Any(j => j.SubscriberId == pin.SubscriberId && j.CompanyId == pin.CompanyId &&
                    j.DocumentId == pin.DocumentId && j.Kind == "AfterSynchronization")
            select new { Policy = policy, pin.DocumentId }).Take(25).ToArrayAsync(ct);
        foreach (var item in automatic)
            await EnqueueScheduled(db, item.Policy, item.DocumentId, "AfterSynchronization",
                "auto:" + item.DocumentId.ToString("N"), now, ct);
        foreach (var policy in periodic)
        {
            var interval = policy.IntervalHours!.Value;
            var bucket = (long)now.AddHours(8).Subtract(DateTimeOffset.UnixEpoch).TotalHours / interval;
            var key = policy.Id.ToString("N") + ":" + bucket;
            await EnqueueScheduled(db, policy, null, "Batch", "schedule-batch:" + key, now, ct);
            if (policy.Mode == "FullCoverage") await EnqueueScheduled(db, policy, null, "Discovery", "schedule-discovery:" + key, now, ct);
        }
    }

    internal static Task<int> EnqueueScheduled(EfiscalDbContext db, CoverageRevision policy, Guid? document, string kind,
        string key, DateTimeOffset now, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "ReconciliationJobs" ("Id", "SubscriberId", "CompanyId", "ConnectionId", "CoverageId", "DocumentId",
            "Key", "Kind", "State", "Attempts", "AvailableAt", "CreatedAt")
        VALUES ({Guid.NewGuid()}, {policy.SubscriberId}, {policy.CompanyId}, {policy.ConnectionId}, {policy.Id}, {document},
            {key}, {kind}, 'Queued', 0, {now}, {now})
        ON CONFLICT ("SubscriberId", "CompanyId", "Key") DO NOTHING
        """, ct);
}

