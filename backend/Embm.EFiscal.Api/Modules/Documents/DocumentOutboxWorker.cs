using System.Security.Cryptography;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Documents;

internal sealed class DocumentOutboxWorker(IServiceScopeFactory scopes, ILogger<DocumentOutboxWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                for (var index = 0; index < 12 && await ProcessOne(ct); index++) { }
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                log.LogWarning("Document outbox will retry after {ErrorType}", error.GetType().Name);
            }
        }
    }

    internal async Task<bool> ProcessOne(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfiscalDbContext>();
        var now = DateTimeOffset.UtcNow;
        DocumentOutboxMessage job;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var picked = await db.DocumentOutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM "DocumentOutboxMessages"
                WHERE "Kind" IN ('Render', 'Email') AND
                (("State" IN ('Pending', 'RetryScheduled') AND "AvailableAt" <= {now})
                 OR ("State" = 'Processing' AND "LeaseUntil" <= {now}))
                ORDER BY "AvailableAt", "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (picked.Count == 0) return false;
            job = picked[0];
            job.LeaseToken = Guid.NewGuid();
            job.LeaseUntil = now.AddMinutes(3);
            job.State = "Processing";
            job.Attempts++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var token = job.LeaseToken;
        db.ChangeTracker.Clear();
        var doc = await db.FiscalDocuments.AsNoTracking().Include(x => x.Snapshot).Include(x => x.Lines).SingleAsync(x =>
            x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId && x.Id == job.DocumentId, ct);
        var file = await db.IssuedDocumentFiles.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == job.SubscriberId
            && x.CompanyId == job.CompanyId && x.DocumentId == job.DocumentId, ct);
        if (job.Kind == "Email") await MarkSending(db, job, token, ct);
        byte[]? pdf = null;
        string? accepted = null;
        string? errorCategory = null;
        var permanentFailure = false;
        try
        {
            if (!doc.DevelopmentFixture || SalesOrder(doc.Type)) throw new InvalidOperationException("Unsupported document delivery boundary.");
            if (job.Kind == "Render")
            {
                if (file is null) pdf = scope.ServiceProvider.GetRequiredService<IDocumentPdfRenderer>().Render(doc, false);
            }
            else
            {
                var mailbox = scope.ServiceProvider.GetRequiredService<IDocumentDeliveryAdapter>();
                if (!mailbox.Available || file is null) { errorCategory = "ProviderOrAttachmentUnavailable"; permanentFailure = true; }
                else accepted = await mailbox.Deliver(doc, job, file, ct);
            }
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            errorCategory = job.Kind == "Render" ? "RenderingFailed" : "LocalDeliveryFailed";
            log.LogWarning("Document job {JobId} failed during {Operation} with {ErrorType}", job.Id, job.Kind, error.GetType().Name);
        }

        await using var completed = await db.LockSubscriberAsync(job.SubscriberId, ct);
        var current = await db.DocumentOutboxMessages.SingleAsync(x => x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId
            && x.DocumentId == job.DocumentId && x.Id == job.Id, ct);
        if (current.LeaseToken != token) return true;
        var state = await db.DocumentProcessing.SingleAsync(x => x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId
            && x.DocumentId == job.DocumentId, ct);
        now = DateTimeOffset.UtcNow;
        current.LeaseToken = null;
        current.LeaseUntil = null;
        if (errorCategory is null)
        {
            current.State = "Completed";
            if (job.Kind == "Render")
            {
                if (file is null && pdf is not null)
                    db.IssuedDocumentFiles.Add(new IssuedDocumentFile { SubscriberId = job.SubscriberId, CompanyId = job.CompanyId, DocumentId = job.DocumentId,
                        Content = pdf, Sha256 = Convert.ToHexString(SHA256.HashData(pdf)), RenderedAt = now });
                var pinned = Read<DeliveryStamp>(doc.Snapshot.Delivery);
                if (!await db.DocumentOutboxMessages.AnyAsync(x => x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId
                    && x.DocumentId == job.DocumentId && x.Kind == "Email" && x.Operation == "initial", ct))
                    db.DocumentOutboxMessages.Add(DocumentCommitService.Job(doc, "Email", "initial", now, pinned.Recipient));
                state.Rendering = "Ready";
                state.ElectronicDelivery = "Queued";
                db.Audit("Documents.PdfPreserved", subscriber: doc.SubscriberId, target: doc.Id);
            }
            else
            {
                state.ElectronicDelivery = "AcceptedByProvider";
                db.DocumentDeliveryAttempts.Add(Attempt(job, now, "AcceptedByProvider", accepted, file?.Sha256, null, null));
            }
        }
        else
        {
            var retry = !permanentFailure && job.Attempts < 4;
            var status = retry ? "RetryScheduled" : permanentFailure ? "RequiresReview" : "Failed";
            DateTimeOffset? retryAt = retry ? now.AddSeconds(5 * (1 << job.Attempts)) : null;
            current.State = status;
            if (retryAt.HasValue) current.AvailableAt = retryAt.Value;
            if (job.Kind == "Render")
            {
                state.Rendering = status;
                state.ElectronicDelivery = retry ? "PendingPdf" : "RequiresReview";
                db.Audit("Documents.Pdf" + status, subscriber: doc.SubscriberId, target: doc.Id);
            }
            else
            {
                state.ElectronicDelivery = status;
                db.DocumentDeliveryAttempts.Add(Attempt(job, now, status, null, file?.Sha256, errorCategory, retryAt));
            }
        }
        await db.SaveChangesAsync(ct);
        await completed.CommitAsync(ct);
        return true;
    }

    private static async Task MarkSending(EfiscalDbContext db, DocumentOutboxMessage job, Guid? token, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(job.SubscriberId, ct);
        var current = await db.DocumentOutboxMessages.AsNoTracking().SingleAsync(x => x.Id == job.Id, ct);
        if (current.LeaseToken != token) return;
        await db.DocumentProcessing.Where(x => x.SubscriberId == job.SubscriberId && x.CompanyId == job.CompanyId && x.DocumentId == job.DocumentId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.ElectronicDelivery, "Sending"), ct);
        await tx.CommitAsync(ct);
    }

    private static DocumentDeliveryAttempt Attempt(DocumentOutboxMessage job, DateTimeOffset at, string status, string? reference,
        string? sha, string? error, DateTimeOffset? retryAt) => new()
    {
        SubscriberId = job.SubscriberId, CompanyId = job.CompanyId, DocumentId = job.DocumentId, JobId = job.Id,
        Attempt = job.Attempts, Status = status, Recipient = job.Recipient ?? "", ProviderReference = reference, AttachmentSha256 = sha,
        ErrorCategory = error, Error = error is null ? null : "The development adapter could not complete this attempt.", At = at, RetryAt = retryAt
    };
}

