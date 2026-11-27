using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;

namespace Embm.EFiscal.Api.Modules.Documents;

internal sealed class DocumentSettings(EfiscalDbContext db)
{
    public IQueryable<DocumentNumberSeries> Series(Guid subscriber, Guid company) =>
        db.DocumentNumberSeries.Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    public async Task<DocumentNumberSeries?> EffectiveSeries(Guid subscriber, Guid company, string type, Guid branch, DateOnly date, CancellationToken ct) =>
        await Series(subscriber, company).Where(x => x.Type == type && (x.BranchId == branch || x.BranchId == null) && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.BranchId.HasValue).ThenByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Revision).FirstOrDefaultAsync(ct);

    public async Task<DocumentNumberSeries> AddSeries(Guid subscriber, Guid company, Guid actor, string type, SeriesInput input, CancellationToken ct)
    {
        var prefix = input.Prefix?.Trim().ToUpperInvariant();
        Need(prefix is { Length: >= 1 and <= 24 } && prefix.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '/'),
            "prefix", "Enter 1 to 24 letters, numbers, hyphens or slashes.");
        Need(input.Digits is >= 3 and <= 12 && input.InitialNumber >= 1 && input.InitialNumber <= Maximum(input.Digits),
            "initialNumber", "Enter a positive sequence within the selected length of 3 to 12 digits.");
        Need(input.EffectiveFrom.Year is >= 2000 and <= 2199, "effectiveFrom", "Choose a valid effective date.");
        if (input.BranchId is Guid branch)
            Need(await db.Branches.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == branch, ct),
                "branchId", "Select a branch from this company.");
        var previous = await Series(subscriber, company).Where(x => x.Type == type && x.BranchId == input.BranchId)
            .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
        Need(input.ExpectedRevision == (previous?.Revision ?? 0), "revision", "The numbering settings changed. Reload before saving.");
        Need(previous is null || previous.EffectiveFrom <= input.EffectiveFrom, "effectiveFrom", "A new revision cannot precede the last effective date.");
        // A prefix owns its numeric range within the company and document type.
        var prefixes = await Series(subscriber, company).Where(x => x.Type == type).ToArrayAsync(ct);
        Need(!prefixes.Any(x => x.Prefix != prefix && (x.Prefix.StartsWith(prefix!, StringComparison.Ordinal)
            || prefix!.StartsWith(x.Prefix, StringComparison.Ordinal))), "prefix", "Choose a prefix that does not overlap another numbering series.");
        foreach (var other in prefixes.Where(x => x.Prefix == prefix))
        {
            Need(other.BranchId == input.BranchId, "prefix", "Use a distinct prefix for another branch scope.");
            Need(other.Digits == input.Digits && input.InitialNumber >= other.NextNumber,
                "initialNumber", "Continue after the existing sequence when reusing a prefix with the same length.");
        }
        var row = new DocumentNumberSeries { SubscriberId = subscriber, CompanyId = company, BranchId = input.BranchId,
            Type = type, Prefix = prefix!, Digits = input.Digits, EffectiveFrom = input.EffectiveFrom, InitialNumber = input.InitialNumber,
            NextNumber = input.InitialNumber, Revision = (previous?.Revision ?? 0) + 1, ActorId = actor, CreatedAt = DateTimeOffset.UtcNow };
        db.DocumentNumberSeries.Add(row);
        db.Audit("Documents.NumberSeriesPublished", actor, subscriber, row.Id);
        return row;
    }

    public static long Maximum(int digits)
    {
        long value = 1;
        for (var index = 0; index < digits; index++) value *= 10;
        return value - 1;
    }

    public async Task<DocumentTemplates> Templates(Guid subscriber, Guid company, string type, DocumentInput input, CancellationToken ct)
    {
        if (DocumentRules.SalesOrder(type))
        {
            Need(input.PdfTemplateId is null && input.EmailTemplateId is null && input.Recipient is null,
                "recipient", "Sales Order Save does not select electronic delivery settings.");
            return new(null, null);
        }
        var label = DocumentRules.TemplateType(type);
        var defaults = TemplateRules.Defaults(label);
        return new(await Pick(subscriber, company, label, "pdf", input.PdfTemplateId, defaults.Layout, ct),
            await Pick(subscriber, company, label, "email", input.EmailTemplateId, defaults.Email, ct));
    }

    private async Task<TemplateStamp<T>> Pick<T>(Guid subscriber, Guid company, string type, string kind, Guid? id, T defaults, CancellationToken ct)
    {
        var choice = await db.CompanyTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company
            && x.DocumentType == type && x.Kind == kind && (id == null ? x.IsDefault : x.Id == id), ct);
        if (choice is null)
        {
            Need(id is null, kind + "TemplateId", "The selected template is unavailable for this document.");
            return new(null, 1, "Built-in " + (kind == "pdf" ? "PDF" : "email"), defaults);
        }
        var version = await db.CompanyTemplateVersions.AsNoTracking().SingleAsync(x => x.SubscriberId == subscriber && x.CompanyId == company
            && x.TemplateId == choice.Id && x.Version == choice.CurrentVersion, ct);
        return new(choice.Id, version.Version, version.Name,
            System.Text.Json.JsonSerializer.Deserialize<T>(version.ContentJson, TemplateRules.Json) ?? throw new InvalidOperationException("Template content unavailable."));
    }

    public async Task<DeliveryStamp> Delivery(Guid subscriber, Guid company, string type, string? requested, string[] recipients, CancellationToken ct)
    {
        if (DocumentRules.SalesOrder(type)) return new(null, null, null, []);
        var choice = requested?.Trim();
        var recipient = choice is null ? recipients.FirstOrDefault() : recipients.FirstOrDefault(x => x.Equals(choice, StringComparison.OrdinalIgnoreCase));
        Need(choice is null || recipient is not null, "recipient", "Select an authorized email address from this customer record.");
        var row = await db.CompanyEmailSettings.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company, ct);
        var sender = row is null ? null : System.Text.Json.JsonSerializer.Deserialize<SenderProfile>(row.ProfileJson, TemplateRules.Json);
        return new(recipient, row?.Revision, sender, recipients);
    }
}

