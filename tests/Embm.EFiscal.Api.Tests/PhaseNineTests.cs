using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

    private static async Task DiscoverCoverage(HttpClient owner, string path, Guid connection)
    {
        using var response = await Mutate(owner, path + "/jobs", new
        {
            connectionId = connection, kind = "Discovery", from = "2026-12-01", through = "2027-02-01", key = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var result = await WaitForDocument(owner, path + "/jobs", x => x.GetProperty("items").EnumerateArray().Any(job =>
            job.GetProperty("id").GetGuid() == id && job.GetProperty("state").GetString() is "Completed" or "Failed"));
        var completed = result.GetProperty("items").EnumerateArray().Single(job => job.GetProperty("id").GetGuid() == id);
        Assert.Equal("Completed", completed.GetProperty("state").GetString());
    }
}
