using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Billing;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    private sealed record DocumentFixture(Guid Subscriber, Guid Company, Guid Branch, Guid Customer, Guid Item, Guid Unit, string Path);

    [PostgresFact]
    public async Task InvoiceCommitIsIdempotentScopedAndImmutableWithOriginalPdfAndEmail()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "INVOICE");
        var terms = await Add(owner, MasterPath(f.Subscriber, f.Company), "payment-terms", "NET15", new { days = "15", method = "CalendarDays" });
        var doc = Input(f, "SalesInvoice", "2") with { PaymentTermId = terms };
        var balanceBefore = await Balance(owner, f.Subscriber);
        var preview = await DocPreview(owner, f, "SalesInvoice", doc);
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("documentNumber").ValueKind);
        Assert.Equal("224", preview.GetProperty("totals").GetProperty("total").GetString());
        Assert.Equal("2026-11-16", preview.GetProperty("dueDate").GetString());
        using (var pdf = await Mutate(owner, f.Path + "/SalesInvoice/pdf-preview", doc))
        {
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync()));
        }
        var email = await RequestJson(owner, f.Path + "/SalesInvoice/email-preview", doc);
        Assert.Contains("PREVIEW", email.GetProperty("subject").GetString());
        Assert.Equal(balanceBefore, await Balance(owner, f.Subscriber));
        Assert.Empty((await Json(owner, f.Path + "/SalesInvoice/register")).GetProperty("items").EnumerateArray());
        var key = Guid.NewGuid().ToString("N");
        var first = await CommitDocument(owner, f, "SalesInvoice", doc, preview, key);
        var id = first.GetProperty("id").GetGuid();
        Assert.Equal("SI-000001", first.GetProperty("number").GetString());
        var replay = await CommitDocument(owner, f, "SalesInvoice", doc, preview, key);
        Assert.Equal(id, replay.GetProperty("id").GetGuid());
        Assert.Equal(id, (await Json(owner, f.Path + "/commit-result/" + key)).GetProperty("id").GetGuid());
        Assert.Equal(balanceBefore - 1, await Balance(owner, f.Subscriber));
        await Expect(Mutate(owner, f.Path + "/SalesInvoice/commit", new { key, confirmation = preview.GetProperty("confirmation").GetString(),
            document = doc with { Remarks = "Changed request" } }), HttpStatusCode.Conflict);
        var recordPath = f.Path + "/SalesInvoice/" + id;
        var detail = await WaitForDocument(owner, recordPath, row => row.GetProperty("electronicDelivery").GetString() == "AcceptedByProvider");
        Assert.Equal("AwaitingConnection", detail.GetProperty("accounting").GetString());
        Assert.Equal("AuthorizationRequired", detail.GetProperty("regulator").GetString());
        var sha = detail.GetProperty("file").GetProperty("sha256").GetString();
        var bytes = await owner.GetByteArrayAsync(recordPath + "/pdf");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), sha);
        using (var pdf = PdfReader.Open(new MemoryStream(bytes))) Assert.True(pdf.PageCount >= 1);
        var envelope = Assert.Single(detail.GetProperty("delivery").EnumerateArray());
        Assert.Equal(sha, envelope.GetProperty("attachmentSha256").GetString());
        var provider = envelope.GetProperty("providerReference").GetString();
        using (var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_mail, "issued-documents", provider + ".json"))))
        {
            Assert.Equal("buyer@example.test", parsed.RootElement.GetProperty("recipient").GetString());
            Assert.Equal(bytes, parsed.RootElement.GetProperty("attachment").GetBytesFromBase64());
        }
        await Expect(Mutate(owner, recordPath, new { }, HttpMethod.Put), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, recordPath, null, HttpMethod.Delete), HttpStatusCode.NotFound);
        await Db(async db =>
        {
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.FiscalDocuments.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Number, "ALTERED")));
            Assert.Equal("P0001", rejected.SqlState);
            rejected = await Assert.ThrowsAsync<PostgresException>(() => db.FiscalDocumentLines.Where(x => x.DocumentId == id).ExecuteDeleteAsync());
            Assert.Equal("P0001", rejected.SqlState);
            Assert.Equal(1, await db.CreditLedgerEntries.CountAsync(x => x.SubscriberId == f.Subscriber && x.DocumentId == id && x.EventType == "Consumption"));
        });
        await Expect(Mutate(owner, recordPath + "/resend", new { key = Guid.NewGuid().ToString("N"), recipient = "unrelated@example.test" }), HttpStatusCode.BadRequest);
        var resend = new { key = Guid.NewGuid().ToString("N"), recipient = "authorized-copy@example.test" };
        await Expect(Mutate(owner, recordPath + "/resend", resend), HttpStatusCode.Accepted);
        await Expect(Mutate(owner, recordPath + "/resend", resend), HttpStatusCode.Accepted);
        var after = await WaitForDocument(owner, recordPath, row => row.GetProperty("delivery").GetArrayLength() == 2);
        Assert.All(after.GetProperty("delivery").EnumerateArray(), attempt => Assert.Equal(sha, attempt.GetProperty("attachmentSha256").GetString()));
        Assert.Equal(balanceBefore - 1, await Balance(owner, f.Subscriber));

        var (outsiderTenant, _, outsiderEmail) = await SeedSubscriber();
        using var outsider = Browser();
        await Login(outsider, outsiderEmail, _password);
        await Expect(outsider.GetAsync(recordPath), HttpStatusCode.NotFound);
        await Expect(outsider.GetAsync(recordPath + "/pdf"), HttpStatusCode.NotFound);
        var outsiderCompany = await CreateCompany(outsider, outsiderTenant, "FOREIGN");
        await Expect(outsider.GetAsync(DocPath(outsiderTenant, outsiderCompany) + "/SalesInvoice/" + id), HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task ZeroBalanceOrderCanSaveAndConcurrentInvoicesCannotOverallocateItsLines()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "ORDER");
        await Db(async db =>
        {
            await db.CreditLots.Where(x => x.SubscriberId == f.Subscriber)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        });
        Assert.Equal(0, await Balance(owner, f.Subscriber));
        var order = Input(f, "SalesOrder", "3") with { AllowPartial = true };
        var plan = await DocPreview(owner, f, "SalesOrder", order);
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("availableCredits").ValueKind);
        var saved = await CommitDocument(owner, f, "SalesOrder", order, plan);
        var orderId = saved.GetProperty("id").GetGuid();
        Assert.Equal(0, saved.GetProperty("credits").GetInt32());
        var earlyInvoice = Input(f, "SalesInvoice", "1");
        using (var blocked = await Mutate(owner, f.Path + "/SalesInvoice/commit", Operation(earlyInvoice,
            await DocPreview(owner, f, "SalesInvoice", earlyInvoice))))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("No Save & Send Credits", await blocked.Content.ReadAsStringAsync());
        }
        var who = (await Json(owner, "/api/auth/me")).GetProperty("id").GetGuid();
        await Db(async db => await new CreditEngine(db).Adjust(f.Subscriber, who, 2, "support-approval", "Synthetic credit grant for order test", DateTimeOffset.UtcNow, default));
        var source = await Json(owner, f.Path + "/SalesOrder/" + orderId + "/source-lines");
        var lineId = source.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var partial = Input(f, "SalesInvoice", "2") with { SourceDocumentId = orderId,
            Lines = [new DocumentLineInput(f.Item, f.Unit, "2", null, null, null, null, SourceLineId: lineId)] };
        var quote = await DocPreview(owner, f, "SalesInvoice", partial);
        var outstanding = await Task.WhenAll(Mutate(owner, f.Path + "/SalesInvoice/commit", Operation(partial, quote)),
            Mutate(owner, f.Path + "/SalesInvoice/commit", Operation(partial, quote)));
        try
        {
            Assert.Single(outstanding, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(outstanding, x => x.StatusCode == HttpStatusCode.BadRequest);
        }
        finally { foreach (var response in outstanding) response.Dispose(); }
        source = await Json(owner, f.Path + "/SalesOrder/" + orderId + "/source-lines");
        Assert.Equal("1", source.GetProperty("lines")[0].GetProperty("remainingQuantity").GetString());
        Assert.Equal(1, await Balance(owner, f.Subscriber));
        await Db(async db =>
        {
            Assert.False(await db.DocumentOutboxMessages.AnyAsync(x => x.DocumentId == orderId && x.Kind != "Accounting"));
            Assert.False(await db.CreditLedgerEntries.AnyAsync(x => x.DocumentId == orderId));
        });
        var detail = await Json(owner, f.Path + "/SalesOrder/" + orderId);
        Assert.Equal("NotApplicable", detail.GetProperty("electronicDelivery").GetString());
        Assert.Equal("NotApplicable", detail.GetProperty("regulator").GetString());
    }

    private async Task<DocumentFixture> PaidCompany(HttpClient owner, string code, long invoiceInitial = 1)
    {
        var checkout = await StartOrder(owner, "/api/billing", Order("InitialSubscription"));
        var paid = await Simulate(owner, checkout.GetProperty("id").GetGuid(), "paid");
        var subscriber = paid.GetProperty("subscriberId").GetGuid();
        var company = await CreateCompany(owner, subscriber, code);
        var active = (await Json(owner, "/api/workspace")).GetProperty("active");
        var branch = active.GetProperty("branchId").GetGuid();
        var master = MasterPath(subscriber, company);
        var unit = await Add(owner, master, "units", "EA", new { symbol = "ea" });
        var tax = await Add(owner, master, "tax-groups", "VAT", Vat("Exclusive", "2026-01-01"));
        var item = await Add(owner, master, "items", "SERVICE", new { itemType = "Services", unit, price = "100",
            description = "Original committed service", taxGroup = tax });
        var customer = await Add(owner, master, "customers", "BUYER", new { classification = "Corporate", email = "buyer@example.test",
            registeredName = "Original buyer", invoiceRecipients = new[] { "authorized-copy@example.test" } });
        var path = DocPath(subscriber, company);
        foreach (var kind in new[] { "SalesInvoice", "CreditMemo", "SalesOrder" })
        {
            var prefix = kind == "SalesInvoice" ? "SI-" : kind == "CreditMemo" ? "CM-" : "SO-";
            await Expect(Mutate(owner, path + "/numbering/" + kind, new { effectiveFrom = "2026-01-01", prefix,
                digits = kind == "SalesInvoice" && invoiceInitial == 999 ? 3 : 6, initialNumber = kind == "SalesInvoice" ? invoiceInitial : 1,
                expectedRevision = 0 }), HttpStatusCode.OK);
        }
        var sender = new SenderProfile("Synthetic sender", "sender@example.test", null, "platform", null, null, null, null, [], []);
        await Expect(Mutate(owner, $"/api/workspace/{subscriber}/companies/{company}/configuration/sender", new SenderUpdate(0, sender), HttpMethod.Put),
            HttpStatusCode.OK);
        return new(subscriber, company, branch, customer, item, unit, path);
    }

    private static string DocPath(Guid subscriber, Guid company) => $"/api/workspace/{subscriber}/companies/{company}/documents";
    private static DocumentInput Input(DocumentFixture f, string type, string quantity = "1") =>
        new(DocumentRules.Action(type), new(2026, 11, 1), f.Branch, f.Customer, null, null, [new(f.Item, f.Unit, quantity, null, null, null, null)]);
    private static Task<JsonElement> DocPreview(HttpClient browser, DocumentFixture f, string type, DocumentInput doc) =>
        RequestJson(browser, f.Path + "/" + type + "/preview", doc);
    private static object Operation(DocumentInput document, JsonElement quote, string? key = null) =>
        new { key = key ?? Guid.NewGuid().ToString("N"), confirmation = quote.GetProperty("confirmation").GetString(), document };
    private static Task<JsonElement> CommitDocument(HttpClient browser, DocumentFixture f, string type, DocumentInput doc, JsonElement quote, string? key = null) =>
        RequestJson(browser, f.Path + "/" + type + "/commit", Operation(doc, quote, key));
    private static async Task<long> Balance(HttpClient browser, Guid subscriber) =>
        (await Json(browser, $"/api/billing/subscribers/{subscriber}/summary")).GetProperty("wallet").GetProperty("available").GetInt64();

    private static async Task<JsonElement> WaitForDocument(HttpClient browser, string path, Func<JsonElement, bool> condition)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var data = await Json(browser, path);
            if (condition(data)) return data;
            await Task.Delay(250);
        }
        throw new TimeoutException("The expected document worker result was not recorded.");
    }
}

