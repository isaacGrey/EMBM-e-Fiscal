using System.Net;
using System.Security.Cryptography;
using Embm.EFiscal.Api.Modules.Billing;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task CreditMemoUsesOriginalTermsAndCannotExceedOriginalQuantityOrValue()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "MEMO");
        var starting = await Balance(owner, f.Subscriber);
        var inv = Input(f, "SalesInvoice", "2");
        var issued = await CommitDocument(owner, f, "SalesInvoice", inv, await DocPreview(owner, f, "SalesInvoice", inv));
        var source = issued.GetProperty("id").GetGuid();
        var sourceLines = await Json(owner, f.Path + "/SalesInvoice/" + source + "/source-lines");
        var original = sourceLines.GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Expect(Mutate(owner, MasterPath(f.Subscriber, f.Company) + "/customers/" + f.Customer, new
        {
            code = "BUYER", name = "Changed buyer", revision = 1, active = true,
            data = new { classification = "Corporate", registeredName = "Changed buyer", email = "new-buyer@example.test" }
        }, HttpMethod.Put), HttpStatusCode.OK);
        var line = new DocumentLineInput(f.Item, f.Unit, "1", null, null, null, null, SourceLineId: original);
        var credit = Input(f, "CreditMemo") with { CustomerId = null, SourceDocumentId = source, AdjustmentReason = "Partial return",
            AdjustmentKind = "Return", Lines = [line] };
        var preview = await DocPreview(owner, f, "CreditMemo", credit);
        Assert.Equal("112", preview.GetProperty("totals").GetProperty("total").GetString());
        Assert.Equal("Original buyer", preview.GetProperty("parties").GetProperty("customer").GetProperty("name").GetString());
        var memo = await CommitDocument(owner, f, "CreditMemo", credit, preview);
        Assert.Equal("CM-000001", memo.GetProperty("number").GetString());
        var memoPath = f.Path + "/CreditMemo/" + memo.GetProperty("id").GetGuid();
        await WaitForDocument(owner, memoPath, row => row.GetProperty("electronicDelivery").GetString() == "AcceptedByProvider");
        await SaveVerificationPdf("phase6-credit-memo.pdf", await owner.GetByteArrayAsync(memoPath + "/pdf"));
        await Expect(Mutate(owner, f.Path + "/CreditMemo/preview", credit with { Lines = [line with { Quantity = "2" }] }), HttpStatusCode.BadRequest);
        await Expect(Mutate(owner, f.Path + "/CreditMemo/preview",
            credit with { AdjustmentKind = "PriceAdjustment", Lines = [line with { Quantity = null, AdjustmentAmount = "113" }] }), HttpStatusCode.BadRequest);
        var invoice = await Json(owner, f.Path + "/SalesInvoice/" + source);
        Assert.Equal("224", invoice.GetProperty("totals").GetProperty("total").GetString());
        Assert.Contains(invoice.GetProperty("related").EnumerateArray(), x => x.GetProperty("id").GetGuid() == memo.GetProperty("id").GetGuid());
        Assert.Equal(starting - 2, await Balance(owner, f.Subscriber));
    }

    [PostgresFact]
    public async Task FinalNumberRollbackAndLastCreditConcurrencyLeaveOneDebit()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "LIMITS", 999);
        var who = (await Json(owner, "/api/auth/me")).GetProperty("id").GetGuid();
        await Db(async db =>
        {
            await db.CreditLots.Where(x => x.SubscriberId == f.Subscriber)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await new CreditEngine(db).Adjust(f.Subscriber, who, 1, "support-approval", "Synthetic last-credit test", DateTimeOffset.UtcNow, default);
        });
        var input = Input(f, "SalesInvoice");
        var second = input with { Remarks = "Independent request" };
        var firstQuote = await DocPreview(owner, f, "SalesInvoice", input);
        var secondQuote = await DocPreview(owner, f, "SalesInvoice", second);
        var results = await Task.WhenAll(Mutate(owner, f.Path + "/SalesInvoice/commit", Operation(input, firstQuote)),
            Mutate(owner, f.Path + "/SalesInvoice/commit", Operation(second, secondQuote)));
        try
        {
            Assert.Single(results, row => row.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, row => row.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in results) response.Dispose(); }
        Assert.Equal(0, await Balance(owner, f.Subscriber));
        await Db(async db => await new CreditEngine(db).Adjust(f.Subscriber, who, 1, "support-rollback", "Synthetic rollback test", DateTimeOffset.UtcNow, default));
        using var rejected = await Mutate(owner, f.Path + "/SalesInvoice/commit", Operation(second, await DocPreview(owner, f, "SalesInvoice", second)));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(1, await Balance(owner, f.Subscriber));
        await Db(async db =>
        {
            Assert.Equal("SI-999", await db.FiscalDocuments.Where(x => x.SubscriberId == f.Subscriber).Select(x => x.Number).SingleAsync());
            Assert.Equal(1, await db.CreditLedgerEntries.CountAsync(x => x.SubscriberId == f.Subscriber && x.EventType == "Consumption"));
        });
    }

    [PostgresFact]
    public async Task OriginalMultipagePdfUsesPinnedTemplateAndSurvivesDeliveryFailure()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "RECOVER");
        var config = $"/api/workspace/{f.Subscriber}/companies/{f.Company}/configuration";
        var layout = TemplateRules.Defaults("invoice").Layout with { Title = "Synthetic Sales Invoice" };
        var template = await RequestJson(owner, config + "/templates/pdf/invoice", new TemplateInput("Office layout", null, layout));
        var templateId = template.GetProperty("id").GetGuid();
        var doc = Input(f, "SalesInvoice") with { PdfTemplateId = templateId, Lines = Enumerable.Range(1, 39).Select(index =>
            new DocumentLineInput(f.Item, f.Unit, "1", null, null, null, null,
                Description: $"Consulting service {index:00} with a longer narrative to verify wrapping and repeated page headers.")).ToArray() };
        var start = await Balance(owner, f.Subscriber);
        _deliveryFailureCompany = f.Company.ToString();
        _renderFailureCompany = f.Company.ToString();
        var issued = await CommitDocument(owner, f, "SalesInvoice", doc, await DocPreview(owner, f, "SalesInvoice", doc));
        var path = f.Path + "/SalesInvoice/" + issued.GetProperty("id").GetGuid();
        var failedRender = await WaitForDocument(owner, path, row => row.GetProperty("rendering").GetString() == "RetryScheduled");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, failedRender.GetProperty("file").ValueKind);
        Assert.Equal(start - 1, await Balance(owner, f.Subscriber));
        _renderFailureCompany = null;
        await Db(async db =>
        {
            var id = issued.GetProperty("id").GetGuid();
            Assert.Equal(1, await db.DocumentOutboxMessages.Where(job => job.DocumentId == id && job.Kind == "Render")
                .ExecuteUpdateAsync(changes => changes.SetProperty(job => job.AvailableAt, DateTimeOffset.UtcNow)));
        });
        var failed = await WaitForDocument(owner, path, row => row.GetProperty("electronicDelivery").GetString() == "RetryScheduled");
        var attempt = failed.GetProperty("delivery")[0];
        var hash = failed.GetProperty("file").GetProperty("sha256").GetString();
        var pdfBytes = await owner.GetByteArrayAsync(path + "/pdf");
        using (var pdf = PdfReader.Open(new MemoryStream(pdfBytes))) Assert.True(pdf.PageCount > 1);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(pdfBytes)));
        await SaveVerificationPdf("phase6-invoice.pdf", pdfBytes);
        await Expect(Mutate(owner, config + "/templates/" + templateId,
            new TemplateInput("Updated layout", null, layout with { Title = "Changed title" }, 1), HttpMethod.Put), HttpStatusCode.OK);
        _deliveryFailureCompany = null;
        await Expect(Mutate(owner, path + "/retry-delivery/" + attempt.GetProperty("jobId").GetGuid(),
            new { key = Guid.NewGuid().ToString("N") }), HttpStatusCode.Accepted);
        var recovered = await WaitForDocument(owner, path, row => row.GetProperty("electronicDelivery").GetString() == "AcceptedByProvider");
        Assert.Equal(1, recovered.GetProperty("templates").GetProperty("pdf").GetProperty("version").GetInt32());
        Assert.Equal(hash, recovered.GetProperty("delivery")[0].GetProperty("attachmentSha256").GetString());
        Assert.Equal(pdfBytes, await owner.GetByteArrayAsync(path + "/pdf"));
        Assert.Equal(start - 1, await Balance(owner, f.Subscriber));
    }

    [PostgresFact]
    public async Task ConcurrentDirectInvoicesAssignDistinctNumbersAndReportingRoleCannotCommit()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "PARALLEL");
        var before = await Balance(owner, f.Subscriber);
        var input = Input(f, "SalesInvoice");
        var other = input with { Remarks = "Second simultaneous invoice" };
        var firstQuote = await DocPreview(owner, f, "SalesInvoice", input);
        var secondQuote = await DocPreview(owner, f, "SalesInvoice", other);
        var email = Email("document-reporting");
        await Db(async db =>
        {
            var user = NewIdentity(email);
            db.AddRange(user, new SubscriberMembership { SubscriberId = f.Subscriber, UserId = user.Id,
                RoleId = "auditor", CreatedAt = DateTimeOffset.UtcNow },
                new CompanyMembership { SubscriberId = f.Subscriber, CompanyId = f.Company, UserId = user.Id });
            await db.SaveChangesAsync();
        });
        using var viewer = Browser();
        await Login(viewer, email, _password);
        await Expect(Mutate(viewer, f.Path + "/SalesInvoice/commit", Operation(input, firstQuote)), HttpStatusCode.NotFound);
        Assert.Equal(before, await Balance(owner, f.Subscriber));
        var invoices = await Task.WhenAll(CommitDocument(owner, f, "SalesInvoice", input, firstQuote),
            CommitDocument(owner, f, "SalesInvoice", other, secondQuote));
        var numbers = invoices.Select(x => x.GetProperty("number").GetString()).Order().ToArray();
        Assert.Equal(new[] { "SI-000001", "SI-000002" }, numbers);
        Assert.Equal(before - 2, await Balance(owner, f.Subscriber));
        foreach (var invoice in invoices)
            await WaitForDocument(owner, f.Path + "/SalesInvoice/" + invoice.GetProperty("id").GetGuid(),
                row => row.GetProperty("electronicDelivery").GetString() == "AcceptedByProvider");
    }

    [PostgresFact]
    public async Task ExternalOrderLookupIsReadOnlyAndUsesAuthorizedSourceLimits()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "EXTERNAL");
        var master = MasterPath(f.Subscriber, f.Company);
        var tax = await Add(owner, master, "tax-groups", "EXT-VAT", Vat("Exclusive", "2026-01-01"));
        var item = await Add(owner, master, "items", "DEMO-EXT-ITEM", new
        {
            itemType = "Services", unit = f.Unit, price = "100", taxGroup = tax
        });
        var customer = await Add(owner, master, "customers", "DEMO-EXT-CUSTOMER", new
        {
            classification = "Corporate", registeredName = "Fixture buyer", email = "fixture-buyer@example.test"
        });
        var before = await Balance(owner, f.Subscriber);
        var lookup = f.Path + "/external-order/sample-order";
        var source = await Json(owner, lookup);
        Assert.Equal(customer, source.GetProperty("customerId").GetGuid());
        Assert.Equal("5", source.GetProperty("lines")[0].GetProperty("remainingQuantity").GetString());
        var lineKey = source.GetProperty("lines")[0].GetProperty("key").GetString();
        var path = f.Path + "/SalesInvoice";
        var input = Input(f, "SalesInvoice") with
        {
            CustomerId = customer, ExternalOrderKey = "sample-order",
            Lines = [new(item, f.Unit, "4", null, null, null, null, ExternalLineKey: lineKey)]
        };
        var deniedTax = await Add(owner, master, "tax-groups", "UNAPPROVED-VAT", new
        {
            classification = "VATable", treatment = "Exclusive", rounding = "HalfAway", effectiveFrom = "2026-01-01",
            components = new[] { new { kind = "VAT", basis = "NetSales", rate = "12" } },
            authority = Embm.EFiscal.Api.Modules.MasterData.RegulatoryCatalog.Vat, companyAuthorized = false
        });
        await Expect(Mutate(owner, path + "/preview", input with { Lines = [input.Lines[0] with { TaxGroupId = deniedTax }] }),
            HttpStatusCode.BadRequest);
        var preview = await DocPreview(owner, f, "SalesInvoice", input);
        Assert.Equal(before, await Balance(owner, f.Subscriber));
        Assert.Empty((await Json(owner, path + "/register")).GetProperty("items").EnumerateArray());
        var invoice = await CommitDocument(owner, f, "SalesInvoice", input, preview);
        Assert.Equal("TEST-SO-101", invoice.GetProperty("externalOrderNumber").GetString());
        Assert.Equal("1", (await Json(owner, lookup)).GetProperty("lines")[0].GetProperty("remainingQuantity").GetString());
        await Expect(Mutate(owner, path + "/commit", Operation(input, preview)), HttpStatusCode.BadRequest);
        Assert.Equal(before - 1, await Balance(owner, f.Subscriber));
        await WaitForDocument(owner, path + "/" + invoice.GetProperty("id").GetGuid(),
            row => row.GetProperty("electronicDelivery").GetString() == "AcceptedByProvider");
    }

    private volatile string? _deliveryFailureCompany;
    private volatile string? _renderFailureCompany;

    private sealed class FaultInjectingRenderer(DocumentPdf inner, IdentityTenantTests tests) : IDocumentPdfRenderer
    {
        public byte[] Render(FiscalDocument doc, bool preview)
        {
            if (tests._renderFailureCompany == doc.CompanyId.ToString()) throw new IOException("Simulated rendering failure.");
            return inner.Render(doc, preview);
        }
    }

    private sealed class FaultInjectingDelivery(IWebHostEnvironment environment, IConfiguration configuration, IdentityTenantTests tests) : IDocumentDeliveryAdapter
    {
        private readonly DevelopmentDocumentMailbox _inner = new(environment, configuration);
        public bool Available => _inner.Available;

        public Task<string> Deliver(FiscalDocument doc, DocumentOutboxMessage job, IssuedDocumentFile file, CancellationToken ct)
        {
            if (tests._deliveryFailureCompany == doc.CompanyId.ToString()) throw new IOException("Simulated local delivery failure.");
            return _inner.Deliver(doc, job, file, ct);
        }
    }

    private static async Task SaveVerificationPdf(string filename, byte[] bytes)
    {
        var output = Environment.GetEnvironmentVariable("EFISCAL_PHASE6_PDF_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            await File.WriteAllBytesAsync(Path.Combine(output, filename), bytes);
        }
    }
}
