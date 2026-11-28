using System.Globalization;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Billing;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.Reporting;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Documents;

internal sealed class CommitmentConflict(string message) : Exception(message);

internal sealed class DocumentCommitService(EfiscalDbContext db, DocumentAssembly assembly, DocumentSettings settings,
    CreditEngine credits, DocumentRelationships relationships, IWebHostEnvironment environment, AccountingRouting routing)
{
    public bool LocalFixture(TenantGrant access) => environment.IsDevelopment() && access.Subscriber.DevelopmentFixture;
    public bool CanCommit(string type, TenantGrant access) => SalesOrder(type) || LocalFixture(access);
    public async Task<long> Balance(Guid subscriber, CancellationToken ct)
    {
        var wallet = await db.CreditWallets.AsNoTracking().Where(x => x.SubscriberId == subscriber).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        return wallet.HasValue ? await credits.Available(wallet.Value, DateTimeOffset.UtcNow, ct) : 0;
    }

    public async Task<FiscalDocument?> Commit(Guid subscriber, Guid companyId, string type, CommitInput request, HttpContext http, CancellationToken ct)
    {
        var key = Key(request.Key);
        Need(request.Document is not null, "document", "Supply the document inputs.");
        var input = request.Document!;
        Need(input.Action == Action(type), "action", "Use the final action for this document type.");
        var actor = http.UserId();
        var digest = Digest(new { type, input });
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        var scope = await DocumentEndpoints.Access(subscriber, companyId, Permission(type, true), http, db, ct);
        if (scope is null || !DocumentEndpoints.MayUseHistoricalInvoice(input, scope.Grant)) return null;
        var prior = await Prior(subscriber, companyId, actor, key, ct);
        if (prior is not null)
        {
            if (prior.Digest != digest) throw new CommitmentConflict("This operation key already identifies a different document request.");
            return await relationships.Scope(subscriber, companyId).AsNoTracking().SingleAsync(x => x.Id == prior.DocumentId, ct);
        }

        if (!CanCommit(type, scope.Grant))
            throw new CommitmentConflict("Regulated issuance requires verified production authorizations. This environment supports development fixtures only.");
        Need(scope.Company.ConfigurationStatus == ConfigurationStatus.Configured, "company", "Complete company configuration before committing a document.");
        var now = DateTimeOffset.UtcNow;
        if (!await credits.AllowsOperations(subscriber, now, ct))
            throw new CommitmentConflict("The subscription does not allow new document commitment.");
        var prepared = await assembly.Prepare(scope.Company, actor, type, input, LocalFixture(scope.Grant), ct);
        if (!SalesOrder(type) || request.Confirmation is not null)
            if (request.Confirmation != prepared.Confirmation)
                throw new CommitmentConflict("The current calculation or company settings differ from the confirmation. Preview again before committing.");

        var doc = prepared.Document;
        if (!SalesOrder(type))
        {
            var delivery = Read<DeliveryStamp>(doc.Snapshot.Delivery);
            Need(delivery.Sender is { Provider: "platform" } && TemplateRules.ValidateSender(delivery.Sender) is null && TestAddress(delivery.Sender.SenderEmail),
                "sender", "Save a company sender profile with the platform option for local delivery.");
            Need(delivery.Recipient is not null && TestAddress(delivery.Recipient), "recipient", "Use an authorized .test customer address for the development fixture.");
        }
        var series = prepared.Series ?? throw new CommitmentConflict("Publish an effective numbering series for this company or branch.");
        var reservation = await credits.Reserve(subscriber, companyId, actor, doc.Id, type, Action(type),
            "document-" + Digest(new { companyId, actor, key }), now, ct);
        var issuedNext = await settings.Series(subscriber, companyId).Where(x => x.Type == type && x.Prefix == series.Prefix
            && x.NextNumber > x.InitialNumber).MaxAsync(x => (long?)x.NextNumber, ct);
        var sequence = Math.Max(series.NextNumber, issuedNext ?? 0);
        if (sequence > DocumentSettings.Maximum(series.Digits))
            throw new CommitmentConflict("This numbering series is exhausted.");
        doc.Number = series.Prefix + sequence.ToString("D" + series.Digits, CultureInfo.InvariantCulture);
        doc.SeriesId = series.Id;
        doc.CommittedAt = now;
        series.NextNumber = sequence + 1;
        doc.Snapshot.Numbering = Freeze(new { series.Id, series.Revision, series.Prefix, series.Digits, series.EffectiveFrom, series.BranchId, sequence });
        doc.Processing = new DocumentProcessing { SubscriberId = subscriber, CompanyId = companyId, DocumentId = doc.Id,
            Rendering = SalesOrder(type) ? "NotApplicable" : "Queued", ElectronicDelivery = SalesOrder(type) ? "NotApplicable" : "PendingPdf",
            Accounting = "AwaitingConnection", Regulator = SalesOrder(type) ? "NotApplicable" : "AuthorizationRequired" };
        db.FiscalDocuments.Add(doc);
        db.DocumentAllocations.AddRange(prepared.Allocations);
        db.DocumentCommitments.Add(new DocumentCommitment { SubscriberId = subscriber, CompanyId = companyId, ActorId = actor,
            Key = key, Digest = digest, DocumentId = doc.Id });
        var accounting = Job(doc, "Accounting", "initial", now, state: "AwaitingConnection");
        db.DocumentOutboxMessages.Add(accounting);
        await routing.Pin(doc, accounting, doc.Processing, true, ct);
        if (!SalesOrder(type))
        {
            await CanonicalInvoices.Capture(db, doc, ct);
            db.DocumentOutboxMessages.Add(Job(doc, "Render", "initial", now));
        }
        if (reservation is not null)
            await credits.Complete(reservation, true, now, ct);
        db.Audit("Documents." + type + ".Committed", actor, subscriber, doc.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return doc;
    }

    public Task<DocumentCommitment?> Prior(Guid subscriber, Guid company, Guid actor, string key, CancellationToken ct) =>
        db.DocumentCommitments.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ActorId == actor && x.Key == key, ct);

    public static DocumentOutboxMessage Job(FiscalDocument doc, string kind, string operation, DateTimeOffset now, string? recipient = null, string state = "Pending") =>
        new() { SubscriberId = doc.SubscriberId, CompanyId = doc.CompanyId, DocumentId = doc.Id, Kind = kind, Operation = operation,
            State = state, AvailableAt = now, CreatedAt = now, Recipient = recipient };

    public static bool TestAddress(string email) => TemplateRules.Mailbox(email) && email.EndsWith(".test", StringComparison.OrdinalIgnoreCase);
}

