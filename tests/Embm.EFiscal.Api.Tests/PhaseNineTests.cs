using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Connect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task CoverageVersionsPreserveHistoricalEvidenceAndApplyInclusiveBusinessDates()
    {
        using var owner = await BillingBuyer();
        var company = await PaidCompany(owner, "RECON9");
        var connection = await BindFixture(owner, company, "development-accounting", "sample-company-mnl");
        await PrepareFixture(owner, company, connection);
        var path = ConnectPath(company) + "/reconciliation";
        var balance = await Balance(owner, company.Subscriber);

        async Task<JsonElement> Configure(string mode, int version, string? end, string scope, string[] excluded)
        {
            var input = new
            {
                connectionId = connection, adoption = "2027-01-01", end, mode, issuanceScope = scope,
                types = new[] { "SalesInvoice", "CreditMemo", "SalesOrder" }, excludedSources = excluded,
                exclusionReason = "Approved fixture exclusion", reason = "Test accounting adoption",
                timeZone = "Asia/Manila", expectedVersion = version, batchSize = 2
            };
            var impact = await RequestJson(owner, path + "/coverage/preview", input);
            Assert.True(impact.GetProperty("valid").GetBoolean());
            Assert.False(impact.GetProperty("historyTotalKnown").GetBoolean());
            using var response = await Mutate(owner, path + "/coverage", input);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("2027-01-01", draft.GetProperty("start").GetString());
            Assert.Equal("Asia/Manila", draft.GetProperty("timeZone").GetString());
            return await RequestJson(owner, path + "/coverage/" + draft.GetProperty("id").GetGuid() + "/activate", new { confirm = true });
        }

        await Configure("FullCoverage", 0, "2027-01-10", "AllVerifiedSources", ["ApprovedExternal"]);
        await DiscoverCoverage(owner, path, connection);
        var rows = await Json(owner, path + "/register");
        var statuses = rows.GetProperty("items").EnumerateArray()
            .Where(x => x.GetProperty("externalNumber").GetString()!.StartsWith("MOCK-MNL-"))
            .ToDictionary(x => x.GetProperty("externalNumber").GetString()!, x => x.GetProperty("status").GetString());
        Assert.Equal("HistoricalExcluded", statuses["MOCK-MNL-HIST"]);
        Assert.Equal("MissingEfiscalRecord", statuses["MOCK-MNL-CUTOVER"]);
        Assert.Equal("NotExpected", statuses["MOCK-MNL-EXTERNAL"]);
        Assert.Equal("MissingEfiscalRecord", statuses["MOCK-MNL-CURRENT"]);
        Assert.Equal("OutsideCoveragePeriod", statuses["MOCK-MNL-AFTER"]);
        var overview = await Json(owner, path + "/overview");
        Assert.Equal(2, overview.GetProperty("eligible").GetInt32());
        Assert.Equal(2, overview.GetProperty("missingEfiscal").GetInt32());
        Assert.Equal(1, overview.GetProperty("observedHistorical").GetInt32());
        Assert.Equal(0m, overview.GetProperty("matchRate").GetDecimal());

        await Configure("EfiscalOnly", 1, null, "Adopted", []);
        await DiscoverCoverage(owner, path, connection);
        overview = await Json(owner, path + "/overview");
        Assert.Equal(0, overview.GetProperty("missingEfiscal").GetInt32());
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("matchRate").ValueKind);
        var history = await Json(owner, path + "/register?history=true&status=MissingEfiscalRecord");
        Assert.Equal(2, history.GetProperty("items").GetArrayLength());
        var coverage = await Json(owner, path + "/coverage");
        Assert.Equal(2, coverage.GetProperty("items").GetArrayLength());
        Assert.Single(coverage.GetProperty("items").EnumerateArray(), x => x.GetProperty("active").GetBoolean());
        await Db(async db =>
        {
            db.ReconciliationChecks.Add(new()
            {
                SubscriberId = company.Subscriber, CompanyId = company.Company, ConnectionId = connection,
                UnitKey = "export-text-fixture", Type = "SalesInvoice", Coverage = "NotExpected", Retrieval = "Complete",
                Status = "NotExpected", Completeness = "Excluded", ExternalNumber = "=1+1"
            });
            await db.SaveChangesAsync();
        });
        var csv = await owner.GetStringAsync(path + "/reports/csv");
        Assert.Contains("'=1+1", csv);
        await using var stream = await owner.GetStreamAsync(path + "/reports/xlsx");
        using var workbook = new ZipArchive(stream, ZipArchiveMode.Read);
        using var worksheet = new StreamReader(workbook.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = await worksheet.ReadToEndAsync();
        Assert.Contains("=1+1", xml);
        Assert.DoesNotContain("<f>", xml);
        Assert.Equal(balance, await Balance(owner, company.Subscriber));
    }

    [PostgresFact]
    public async Task IndependentAccountingEvidenceControlsFindingsFreshnessAndResolution()
    {
        using var owner = await BillingBuyer();
        var company = await PaidCompany(owner, "READBACK9");
        var connection = await BindFixture(owner, company, "development-accounting", "sample-company-mnl");
        await PrepareFixture(owner, company, connection);
        await ActivateDestination(owner, company, connection, "SalesInvoice", "Normal");
        var start = await Balance(owner, company.Subscriber);
        var invoice = Input(company, "SalesInvoice", "2");
        var saved = await CommitDocument(owner, company, "SalesInvoice", invoice, await DocPreview(owner, company, "SalesInvoice", invoice));
        var document = saved.GetProperty("id").GetGuid();
        var connect = ConnectPath(company);
        var path = connect + "/reconciliation";
        await WaitForDocument(owner, connect + "/accounting/jobs/" + document,
            x => x.GetProperty("status").GetString() == "Synchronized");

        async Task<JsonElement> Verify(string scenario, string expected)
        {
            await RequestJson(owner, path + "/development/" + document + "/external-revision", new { scenario });
            using var response = await Mutate(owner, path + "/jobs", new { documentId = document, key = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await WaitForDocument(owner, path + "/jobs", result => result.GetProperty("items").EnumerateArray().Any(job =>
                job.GetProperty("id").GetGuid() == id && job.GetProperty("state").GetString() is "Completed" or "Failed"));
            var checks = await Json(owner, path + "/documents/" + document);
            var latest = checks.GetProperty("items")[0];
            Assert.Equal(expected, latest.GetProperty("status").GetString());
            return latest;
        }

        await Verify("Exact", "Matched");
        await Db(async db =>
        {
            db.ReconciliationChecks.Add(new()
            {
                SubscriberId = company.Subscriber, CompanyId = company.Company, ConnectionId = connection,
                UnitKey = "expired-fixture", Type = "SalesInvoice", Coverage = "Eligible", Retrieval = "Complete",
                Status = "Matched", Completeness = "Complete", At = DateTimeOffset.UtcNow.AddDays(-3),
                FreshUntil = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync();
        });
        var overview = await Json(owner, path + "/overview?status=StaleVerification");
        Assert.Equal(1, overview.GetProperty("stale").GetInt32());
        Assert.Equal(0, overview.GetProperty("exact").GetInt32());
        Assert.Equal(0, overview.GetProperty("conclusive").GetInt32());
        Assert.Equal(0m, overview.GetProperty("verificationCoverage").GetDecimal());

        var vat = await Verify("Vat", "TaxMismatch");
        var checkPath = path + "/comparisons/" + vat.GetProperty("id").GetGuid();
        var evidence = await Json(owner, checkPath);
        var finding = evidence.GetProperty("findings")[0].GetProperty("id").GetGuid();
        Assert.NotEmpty(evidence.GetProperty("snapshot").GetProperty("hash").GetString()!);
        await Expect(Mutate(owner, path + "/discrepancies/" + finding + "/review",
            new { status = "ResolvedAfterVerification", note = "Manual resolution must fail" }), HttpStatusCode.BadRequest);
        await RequestJson(owner, path + "/discrepancies/" + finding + "/review",
            new { status = "Acknowledged", note = "Accounting correction pending" });
        await Verify("Discount", "DiscountMismatch");
        await Verify("Quantity", "LineMismatch");
        await Verify("Total", "FinancialMismatch");
        await Verify("Missing", "MissingAccountingRecord");
        await Verify("Duplicate", "DuplicateExternalRecords");
        await Verify("WrongIdentity", "AmbiguousMatch");
        await Verify("Unsupported", "PartialVerification");
        var incomplete = await Verify("IncompletePagination", "IncompleteRetrieval");
        Assert.Equal(0, incomplete.GetProperty("findingCount").GetInt32());
        var unavailable = await Verify("Unavailable", "ConnectionUnavailable");
        Assert.Equal(0, unavailable.GetProperty("findingCount").GetInt32());
        await Verify("Exact", "Matched");
        await Expect(Mutate(owner, path + "/discrepancies/" + finding + "/review",
            new { status = "UnderReview", note = "Already resolved by verification" }), HttpStatusCode.Conflict);
        Assert.Equal(start - 1, await Balance(owner, company.Subscriber));
        await Db(async db => Assert.Equal(1, await db.MockAccountingRecords.CountAsync(x => x.DocumentId == document)));
    }

    [PostgresFact]
    public async Task AccountingMigrationPinsInclusiveDestinationsAndPreservesHistoricalOrderReferences()
    {
        using var owner = await BillingBuyer();
        var company = await PaidCompany(owner, "MIGRATE9");
        var previous = await BindFixture(owner, company, "development-accounting", "sample-company-mnl");
        var current = await BindFixture(owner, company, "development-accounting", "sample-company-ceb");
        await PrepareFixture(owner, company, previous);
        await PrepareFixture(owner, company, current);
        await ActivateDestination(owner, company, previous, "SalesInvoice", "Normal");
        await ActivateDestination(owner, company, current, "SalesInvoice", "Normal");
        var path = ConnectPath(company);
        var mapping = (await Json(owner, path + "/mapping-profiles")).GetProperty("items").EnumerateArray()
            .ToDictionary(x => x.GetProperty("connectionId").GetGuid(), x => x.GetProperty("id").GetGuid());
        object Window(Guid connection, string start, string? end) => new
        {
            profileId = mapping[connection], branchId = company.Branch, documentType = "SalesInvoice", start, end,
            automatic = true, confirm = true, reason = "Approved synthetic accounting cutover"
        };
        var windows = path + "/accounting/destination-windows";
        await Expect(Mutate(owner, windows, Window(previous, "2026-01-01", "2026-11-01")), HttpStatusCode.Created);
        await Expect(Mutate(owner, windows, Window(current, "2026-11-01", null)), HttpStatusCode.Conflict);
        using var currentResponse = await Mutate(owner, windows, Window(current, "2026-11-02", null));
        Assert.Equal(HttpStatusCode.Created, currentResponse.StatusCode);
        var currentWindow = (await currentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var balance = await Balance(owner, company.Subscriber);
        var orders = await Json(owner, path + "/connections/" + previous + "/sales-orders?q=1003");
        var key = Assert.Single(orders.GetProperty("items").EnumerateArray()).GetProperty("key").GetString();
        var source = await Json(owner, company.Path + "/external-order/" + key);
        Assert.Equal("sample-company-mnl", source.GetProperty("externalCompany").GetString());
        Assert.Equal("0003", source.GetProperty("recordId").GetString());
        Assert.Equal("1", source.GetProperty("lines")[0].GetProperty("previouslyInvoiced").GetString());
        Assert.Equal("2", source.GetProperty("lines")[0].GetProperty("remainingQuantity").GetString());
        Assert.Equal(balance, await Balance(owner, company.Subscriber));

        var original = Input(company, "SalesInvoice") with
        {
            Date = new(2026, 11, 1), ExternalOrderKey = key,
            Lines = [new(company.Item, company.Unit, "1", null, null, null, null, ExternalLineKey: "001")]
        };
        var next = Input(company, "SalesInvoice") with { Date = new(2026, 11, 2) };
        var first = await CommitDocument(owner, company, "SalesInvoice", original, await DocPreview(owner, company, "SalesInvoice", original));
        var second = await CommitDocument(owner, company, "SalesInvoice", next, await DocPreview(owner, company, "SalesInvoice", next));
        var firstId = first.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();
        await Db(async db =>
        {
            Assert.Equal(previous, await db.SyncOperations.Where(x => x.DocumentId == firstId).Select(x => x.ConnectionId).SingleAsync());
            Assert.Equal(current, await db.SyncOperations.Where(x => x.DocumentId == secondId).Select(x => x.ConnectionId).SingleAsync());
        });
        var remaining = await Json(owner, company.Path + "/external-order/" + key);
        Assert.Equal("1", remaining.GetProperty("lines")[0].GetProperty("remainingQuantity").GetString());
        var futureStart = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(7);
        await Expect(Mutate(owner, windows, new
        {
            profileId = mapping[previous], branchId = company.Branch, documentType = "SalesInvoice", start = futureStart,
            closeWindowId = currentWindow, automatic = true, confirm = true, reason = "Approved future cutover"
        }), HttpStatusCode.Created);
        var history = await Json(owner, path + "/accounting/destinations");
        Assert.Equal(3, history.GetProperty("windows").GetArrayLength());
        var closed = history.GetProperty("windows").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == currentWindow);
        Assert.Equal(futureStart.AddDays(-1).ToString("yyyy-MM-dd"), closed.GetProperty("end").GetString());
        Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("closedAt").ValueKind);
        await Db(async db => Assert.Equal(current, await db.SyncOperations.Where(x => x.DocumentId == secondId)
            .Select(x => x.ConnectionId).SingleAsync()));
        Assert.Equal(balance - 2, await Balance(owner, company.Subscriber));
    }

    [PostgresFact]
    public async Task HistoricalCreditRechecksPriorExternalAdjustmentsAndSerializesCommitment()
    {
        using var owner = await BillingBuyer();
        var company = await PaidCompany(owner, "CREDIT9");
        var connection = await BindFixture(owner, company, "development-accounting", "sample-company-mnl");
        await PrepareFixture(owner, company, connection);
        await ActivateDestination(owner, company, connection, "CreditMemo", "Normal");
        var connect = ConnectPath(company);
        var path = connect + "/reconciliation";
        var balance = await Balance(owner, company.Subscriber);
        var evidence = await RequestJson(owner, path + "/references", new
            { connectionId = connection, type = "SalesInvoice", externalId = "fixture-MNL-HIST", purpose = "Verify historical return" });
        Assert.True(evidence.GetProperty("creditMemoEligible").GetBoolean());
        var reference = evidence.GetProperty("id").GetGuid();
        var incomplete = await RequestJson(owner, path + "/references", new
            { connectionId = connection, type = "SalesInvoice", externalId = "fixture-MNL-CUTOVER", purpose = "Check adjustment history" });
        Assert.False(incomplete.GetProperty("creditMemoEligible").GetBoolean());
        await Expect(owner.GetAsync(company.Path + "/historical-credit/" + incomplete.GetProperty("id").GetGuid()), HttpStatusCode.NotFound);
        var source = await Json(owner, company.Path + "/historical-credit/" + reference);
        Assert.Equal("1.5", source.GetProperty("lines")[0].GetProperty("remainingQuantity").GetString());
        Assert.Equal("168", source.GetProperty("lines")[0].GetProperty("remainingAmount").GetString());

        var input = Input(company, "CreditMemo") with
        {
            CustomerId = null, HistoricalInvoiceReferenceId = reference, AdjustmentKind = "Return", AdjustmentReason = "Verified historical return",
            Lines = [new(company.Item, company.Unit, "1", null, null, null, null, ExternalLineKey: "fixture-line-1")]
        };
        var tooMuch = input with { Lines = [input.Lines![0] with { Quantity = "2" }] };
        await Expect(Mutate(owner, company.Path + "/CreditMemo/preview", tooMuch), HttpStatusCode.BadRequest);
        var adjustment = input with { AdjustmentKind = "PriceAdjustment",
            Lines = [input.Lines![0] with { Quantity = null, AdjustmentAmount = "10" }] };
        await CommitDocument(owner, company, "CreditMemo", adjustment, await DocPreview(owner, company, "CreditMemo", adjustment));
        var preview = await DocPreview(owner, company, "CreditMemo", input);
        Assert.Equal("112", preview.GetProperty("totals").GetProperty("total").GetString());
        var commits = await Task.WhenAll(
            Mutate(owner, company.Path + "/CreditMemo/commit", Operation(input, preview)),
            Mutate(owner, company.Path + "/CreditMemo/commit", Operation(input, preview)));
        var winner = Assert.Single(commits, x => x.IsSuccessStatusCode);
        Assert.Single(commits, x => x.StatusCode == HttpStatusCode.BadRequest || x.StatusCode == HttpStatusCode.Conflict);
        var memo = (await winner.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach (var response in commits) response.Dispose();
        await WaitForDocument(owner, connect + "/accounting/jobs/" + memo,
            x => x.GetProperty("status").GetString() == "Synchronized");
        await Db(async db =>
        {
            Assert.Equal(0, await db.FiscalDocuments.CountAsync(x => x.CompanyId == company.Company && x.Type == "SalesInvoice"));
            Assert.Equal(2, await db.DocumentAllocations.CountAsync(x => x.CompanyId == company.Company && x.Kind == "CreditHistoricalInvoice"));
            Assert.Equal("fixture-MNL-HIST", await db.MockAccountingRecords.Where(x => x.DocumentId == memo).Select(x => x.Relationship).SingleAsync());
        });
        Assert.Equal(balance - 2, await Balance(owner, company.Subscriber));
    }

    [PostgresFact]
    public async Task MarketplaceMappingSeparatesTaxableShippingDiscountAndSettlement()
    {
        using var owner = await BillingBuyer();
        var company = await PaidCompany(owner, "MARKET9");
        var master = MasterPath(company.Subscriber, company.Company);
        var tax = await Add(owner, master, "tax-groups", "SHIP-VAT", Vat("Exclusive", "2026-01-01"));
        var shipping = await Add(owner, master, "items", "SHIPPING", new { itemType = "Services", unit = company.Unit, price = "25", taxGroup = tax });
        var discount = await Add(owner, master, "discount-groups", "SELLER5", Discount("Fixed", "5", "Line"));
        var store = await BindFixture(owner, company, "development-commerce", "sample-store-mnl");
        await PrepareFixture(owner, company, store);
        var path = ConnectPath(company);
        var link = path + "/connections/" + store;
        var destination = await BindFixture(owner, company, "development-accounting", "sample-company-mnl");
        await PrepareFixture(owner, company, destination);
        await Expect(Mutate(owner, path + "/connections/" + destination + "/references",
            new { kind = "Item", localKey = shipping, externalId = "DEMO-SHIPPING" }, HttpMethod.Put), HttpStatusCode.OK);
        var outbound = await CreateOutbound(owner, company, destination, "SalesInvoice");
        var rules = FieldMapper.Defaults(FieldMapper.Outbound)
            .Select(x => x.Target == "item" ? x with { Operation = "reference" } : x).ToArray();
        await RequestJson(owner, outbound + "/versions", new { expectedVersion = 0, rules });
        await RequestJson(owner, outbound + "/activate", new { version = 1 });
        await Expect(Mutate(owner, path + "/accounting/destinations", new
        {
            profileId = Guid.Parse(outbound.Split('/').Last()), branchId = company.Branch, documentType = "SalesInvoice",
            enabled = true, automatic = true
        }, HttpMethod.Put), HttpStatusCode.OK);
        await Expect(Mutate(owner, link + "/mapping", new
        {
            customerId = company.Customer, itemId = company.Item, unitId = company.Unit, expectedVersion = 1,
            shippingItemId = shipping, sellerDiscountGroupId = discount
        }, HttpMethod.Put), HttpStatusCode.OK);
        await RequestJson(owner, link + "/activate", new { });
        var before = await Balance(owner, company.Subscriber);
        var provider = _app.Services.GetRequiredService<DevelopmentAdapters>().Get("mock-commerce")!;
        var oldOrder = provider.ReadOrders("sample-store-mnl", null).Single(x => x.Id == "0001") with { Financials = null, Revision = 1 };
        await Db(async db =>
        {
            db.SourceOrders.Add(new()
            {
                SubscriberId = company.Subscriber, CompanyId = company.Company, ConnectionId = store,
                ExternalOrderId = oldOrder.Id, Reference = oldOrder.Reference, Status = oldOrder.Status,
                Date = oldOrder.OrderedOn, Currency = oldOrder.Currency, Data = ConnectorRules.Freeze(oldOrder)
            });
            await db.SaveChangesAsync();
        });
        var refresh = await RequestJson(owner, link + "/import-orders", new { });
        Assert.Equal(1, refresh.GetProperty("updated").GetInt32());
        var orders = (await Json(owner, path + "/source-orders")).GetProperty("items").EnumerateArray().ToArray();
        var imported = orders.Single(x => x.GetProperty("status").GetString() == "Paid");
        var key = imported.GetProperty("key").GetString();
        var source = await Json(owner, company.Path + "/external-order/" + key);
        var financials = source.GetProperty("marketplace");
        Assert.Equal("582.4", financials.GetProperty("invoiceTotal").GetString());
        Assert.Equal("562.4", financials.GetProperty("settlement").GetString());
        Assert.Equal(2, financials.GetProperty("mappingVersion").GetInt32());

        var input = Input(company, "SalesInvoice") with { ExternalOrderKey = key, Lines =
        [
            new(company.Item, company.Unit, "5", null, null, null, null, ExternalLineKey: "001"),
            new(shipping, company.Unit, "1", null, null, null, null, ExternalLineKey: "marketplace-shipping")
        ] };
        var tampered = input with { Lines = [input.Lines![0] with { UnitPrice = "80" }, input.Lines[1]] };
        await Expect(Mutate(owner, company.Path + "/SalesInvoice/preview", tampered), HttpStatusCode.BadRequest);
        var refund = orders.Single(x => x.GetProperty("status").GetString() == "Partially Refunded").GetProperty("key").GetString();
        await Expect(Mutate(owner, company.Path + "/SalesInvoice/preview", input with { ExternalOrderKey = refund }), HttpStatusCode.BadRequest);
        var quote = await DocPreview(owner, company, "SalesInvoice", input);
        Assert.Equal("582.4", quote.GetProperty("totals").GetProperty("total").GetString());
        Assert.Equal("5", quote.GetProperty("totals").GetProperty("commercialDiscount").GetString());
        Assert.Equal(before, await Balance(owner, company.Subscriber));
        var issued = await CommitDocument(owner, company, "SalesInvoice", input, quote);
        var documentId = issued.GetProperty("id").GetGuid();
        await WaitForDocument(owner, path + "/accounting/jobs/" + documentId,
            row => row.GetProperty("status").GetString() == "Synchronized");
        await Expect(Mutate(owner, company.Path + "/SalesInvoice/commit", Operation(input, quote)), HttpStatusCode.BadRequest);
        Assert.Equal(before - 1, await Balance(owner, company.Subscriber));
        await Db(async db =>
        {
            var id = issued.GetProperty("id").GetGuid();
            var snapshot = await db.FiscalDocumentSnapshots.Where(x => x.DocumentId == id).Select(x => x.Header).SingleAsync();
            var components = snapshot.GetProperty("externalSource").GetProperty("marketplace").GetProperty("components");
            Assert.Equal(562.40m, components.GetProperty("settlement").GetDecimal());
            Assert.Equal(10m, components.GetProperty("commission").GetDecimal());
            Assert.Equal(5m, components.GetProperty("sellerFundedDiscount").GetDecimal());
            Assert.Single(await db.FiscalDocuments.Where(x => x.CompanyId == company.Company).ToArrayAsync());
        });
    }

    [PostgresFact]
    public async Task BatchReconciliationResumesItsCursorAfterRateLimitAndExpiredLease()
    {
        using var owner = await BillingBuyer();
        var company = await PaidCompany(owner, "BATCH9");
        var connection = await BindFixture(owner, company, "development-accounting", "sample-company-mnl");
        await PrepareFixture(owner, company, connection);
        await ActivateDestination(owner, company, connection, "SalesInvoice", "Normal");
        var connect = ConnectPath(company);
        var path = connect + "/reconciliation";
        using var draftResponse = await Mutate(owner, path + "/coverage", new
        {
            connectionId = connection, adoption = "2027-01-01", mode = "EfiscalOnly", reason = "Automatic synchronization acceptance",
            afterSynchronization = true, batchSize = 2, intervalHours = 24
        });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draftId = (await draftResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await RequestJson(owner, path + "/coverage/" + draftId + "/activate", new { confirm = true });

        var before = await Balance(owner, company.Subscriber);
        var input = Input(company, "SalesInvoice");
        var ids = new Guid[2];
        for (var i = 0; i < ids.Length; i++)
        {
            var issued = await CommitDocument(owner, company, "SalesInvoice", input, await DocPreview(owner, company, "SalesInvoice", input));
            ids[i] = issued.GetProperty("id").GetGuid();
            await WaitForDocument(owner, connect + "/accounting/jobs/" + ids[i],
                x => x.GetProperty("status").GetString() == "Synchronized");
            await WaitForDocument(owner, path + "/documents/" + ids[i], x =>
                x.GetProperty("items").EnumerateArray().Any(check => check.GetProperty("status").GetString() == "Matched"));
        }
        Array.Sort(ids);
        await WaitForDocument(owner, path + "/jobs", x => x.GetProperty("items").EnumerateArray().Any(job =>
            job.GetProperty("kind").GetString() == "Batch" && job.GetProperty("state").GetString() == "Completed"));
        var overview = await Json(owner, path + "/overview");
        Assert.Equal(2, overview.GetProperty("exact").GetInt32());

        await RequestJson(owner, path + "/development/" + ids[1] + "/external-revision", new { scenario = "RateLimit" });
        using var queued = await Mutate(owner, path + "/jobs", new { connectionId = connection, kind = "Batch", key = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var jobId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        bool JobIn(JsonElement x, string state) => x.GetProperty("items").EnumerateArray().Any(job =>
            job.GetProperty("id").GetGuid() == jobId && job.GetProperty("state").GetString() == state);
        await WaitForDocument(owner, path + "/jobs", x => JobIn(x, "RetryScheduled"));
        await Db(async db =>
        {
            Assert.Equal(ids[0], await db.ReconciliationJobs.Where(x => x.Id == jobId).Select(x => x.DocumentCursor).SingleAsync());
            Assert.Single(await db.ReconciliationChecks.Where(x => x.JobId == jobId).ToArrayAsync());
        });
        await RequestJson(owner, path + "/development/" + ids[1] + "/external-revision", new { scenario = "Exact" });
        await Db(async db => await db.ReconciliationJobs.Where(x => x.Id == jobId && x.State == "RetryScheduled")
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.State, "Processing").SetProperty(t => t.Lease, Guid.NewGuid())
                .SetProperty(t => t.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await WaitForDocument(owner, path + "/jobs", x => JobIn(x, "Completed"));
        await Db(async db =>
        {
            var checks = await db.ReconciliationChecks.Where(x => x.JobId == jobId).ToArrayAsync();
            Assert.Equal(2, checks.Select(x => x.DocumentId).Distinct().Count());
            Assert.All(checks, x => Assert.Equal("Matched", x.Status));
        });
        Assert.Equal(before - 2, await Balance(owner, company.Subscriber));
    }

    private static async Task DiscoverCoverage(HttpClient owner, string path, Guid connection)
    {
        using var response = await Mutate(owner, path + "/jobs", new
        {
            connectionId = connection, kind = "Discovery", from = "2026-10-01", through = "2027-02-01", key = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var result = await WaitForDocument(owner, path + "/jobs", x => x.GetProperty("items").EnumerateArray().Any(job =>
            job.GetProperty("id").GetGuid() == id && job.GetProperty("state").GetString() is "Completed" or "Failed"));
        var completed = result.GetProperty("items").EnumerateArray().Single(job => job.GetProperty("id").GetGuid() == id);
        Assert.Equal("Completed", completed.GetProperty("state").GetString());
    }
}
