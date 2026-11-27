using System.Text.Json;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Documents;

internal record LocalDeliveryPayload(string ProviderReference, Guid DocumentId, string Sender, string SenderName, string Recipient,
    string? ReplyTo, string[] Cc, string[] Bcc, RenderedEmail Message, string AttachmentFileName, string AttachmentSha256, byte[] Attachment);

internal interface IDocumentDeliveryAdapter
{
    bool Available { get; }
    Task<string> Deliver(FiscalDocument doc, DocumentOutboxMessage job, IssuedDocumentFile file, CancellationToken ct);
}

internal sealed class DevelopmentDocumentMailbox(IWebHostEnvironment environment, IConfiguration configuration) : IDocumentDeliveryAdapter
{
    public bool Available => environment.IsDevelopment() && !string.IsNullOrWhiteSpace(configuration["Identity:DevelopmentMailbox"]);

    // The atomic rename and stable job ID make an accepted result recoverable after an interrupted database update.
    public async Task<string> Deliver(FiscalDocument doc, DocumentOutboxMessage job, IssuedDocumentFile file, CancellationToken ct)
    {
        if (!Available || !doc.DevelopmentFixture || job.Recipient is null || !DocumentCommitService.TestAddress(job.Recipient))
            throw new InvalidOperationException("The development delivery adapter is unavailable for this document.");
        var delivery = Read<DeliveryStamp>(doc.Snapshot.Delivery);
        var sender = delivery.Sender;
        if (sender is not { Provider: "platform" } || !DocumentCommitService.TestAddress(sender.SenderEmail))
            throw new InvalidOperationException("An authorized development sender is required.");
        var template = Read<DocumentTemplates>(doc.Snapshot.Templates).Email?.Content ?? throw new InvalidOperationException();
        var reference = "local-" + job.Id.ToString("N");
        var stamped = new LocalDeliveryPayload(reference, doc.Id, sender.SenderEmail, sender.SenderName, job.Recipient,
            sender.ReplyTo is not null && DocumentCommitService.TestAddress(sender.ReplyTo) ? sender.ReplyTo : null,
            template.IncludeConfiguredCopies ? sender.Cc.Where(DocumentCommitService.TestAddress).ToArray() : [],
            template.IncludeConfiguredCopies ? sender.Bcc.Where(DocumentCommitService.TestAddress).ToArray() : [],
            RenderEmail(doc, template, job.Recipient, false), doc.Number.Replace('/', '-') + ".pdf", file.Sha256, file.Content);
        var directory = Path.Combine(Path.GetFullPath(configuration["Identity:DevelopmentMailbox"]!), "issued-documents");
        Directory.CreateDirectory(directory);
        var final = Path.Combine(directory, reference + ".json");
        if (File.Exists(final)) return reference;
        var temp = Path.Combine(directory, reference + "-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(stamped, Json), ct);
            try { File.Move(temp, final, overwrite: false); }
            catch (IOException) when (File.Exists(final)) { }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return reference;
    }

    public static RenderedEmail RenderEmail(FiscalDocument doc, EmailTemplate template, string? recipient, bool preview)
    {
        var parties = Read<DocumentParties>(doc.Snapshot.Parties);
        var header = Read<DocumentHeader>(doc.Snapshot.Header);
        var number = preview ? "PREVIEW" : doc.Number;
        var credit = doc.Type == "CreditMemo";
        return TemplateRules.Render(template, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CompanyName"] = parties.Seller.Name, ["CustomerName"] = parties.Customer.Name, ["CustomerEmail"] = recipient ?? "",
            ["DocumentType"] = Name(doc.Type), ["DocumentNumber"] = number, ["DocumentDate"] = doc.Date.ToString("yyyy-MM-dd"),
            ["DocumentTotal"] = PrintMoney(doc, doc.Total), ["AmountDue"] = PrintMoney(doc, doc.AmountDue),
            ["DueDate"] = doc.DueDate.ToString("yyyy-MM-dd"), ["OriginalInvoiceNumber"] = credit ? header.SourceNumber ?? "" : "",
            ["SalesOrderReference"] = credit ? "" : header.SourceNumber ?? "",
            ["CompanyContactInformation"] = string.Join(" · ", new[] { parties.Seller.Email, parties.Seller.Contact }.Where(x => !string.IsNullOrWhiteSpace(x))),
            ["CreditMemoNumber"] = credit ? number : "", ["CreditMemoDate"] = credit ? doc.Date.ToString("yyyy-MM-dd") : "",
            ["CreditAmount"] = credit ? PrintMoney(doc, doc.Total) : "", ["AdjustmentReason"] = credit ? header.AdjustmentReason ?? "" : ""
        });
    }
}

