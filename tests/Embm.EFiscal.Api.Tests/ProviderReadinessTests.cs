using System.Net;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Reporting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task ProviderReadinessRequiresCurrentIndependentEvidenceAndCannotActivateProduction()
    {
        using var editor = await ConnectorVendor(ConnectorRules.VendorGrants);
        using var reviewer = await ConnectorVendor(ConnectorRules.View, ConnectorRules.ApproveRelease);
        using var outsider = await ConnectorVendor();
        var sample = Draft();
        const string officialSource = "https://example.com/fixture-provider/specification";
        var draft = sample with
        {
            Adapter = "review-only",
            Configuration = sample.Configuration with
            {
                Authentication = [],
                Documentation = [officialSource],
                Capabilities = [new("DiscoverCompanyFiles", "Proposed", null)],
                Origins = ["https://example.com/"],
                ManualOperations = [new("GET", "/fixture")]
            }
        };
        var candidate = await RequestJson(editor, VendorConnectPath, draft);
        var id = candidate.GetProperty("connectorId").GetGuid();
        var version = candidate.GetProperty("id").GetGuid();
        var release = $"{VendorConnectPath}/{id}/versions/{version}";
        var path = release + "/production-readiness";
        var register = await Json(editor, VendorConnectPath + "/production-readiness");
        var items = register.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, x => x.GetProperty("code").GetString() == "qbo");
        Assert.Contains(items, x => x.GetProperty("id").GetGuid() == version);
        Assert.DoesNotContain(items, x => x.GetProperty("code").GetString() == "development-accounting");
        Assert.False(register.GetProperty("productionAccessEnabled").GetBoolean());
        await Expect(outsider.GetAsync(path), HttpStatusCode.Forbidden);
        await Expect(editor.GetAsync(path.Replace(id.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal)), HttpStatusCode.NotFound);

        var evidence = new ProviderEvidence("Synthetic review edition", "PH", officialSource, "FIXTURE-ACCESS",
            "FIXTURE-AUTH", RegulatoryEligibility.ManilaDate().AddDays(5), "FIXTURE-REPORT", ["DiscoverCompanyFiles"]);
        Task<HttpResponseMessage> Save(HttpClient actor, int sequence, int revision, ProviderEvidence packet) =>
            Mutate(actor, path + "/evidence", new RecordProviderEvidence(sequence, revision, packet));
        Task<HttpResponseMessage> Review(HttpClient actor, int sequence, int revision, string decision) =>
            Mutate(actor, path + "/review", new ReviewProviderEvidence(sequence, revision, decision, "Synthetic review record"));

        await Expect(Save(reviewer, 0, 1, evidence), HttpStatusCode.Forbidden);
        await Expect(Save(editor, 0, 1, evidence with { DocumentationUrl = "https://example.org/unregistered" }), HttpStatusCode.BadRequest);
        await Expect(Save(editor, 0, 1, evidence with { Capabilities = ["CreditMemoCreate"] }), HttpStatusCode.BadRequest);
        await Expect(Save(editor, 0, 1, evidence with { AuthorizationThrough = RegulatoryEligibility.ManilaDate().AddDays(-1), SandboxReportReference = null }),
            HttpStatusCode.OK);
        await Expect(Review(editor, 1, 1, "EvidenceReviewed"), HttpStatusCode.Conflict);
        await Expect(Review(reviewer, 1, 1, "EvidenceReviewed"), HttpStatusCode.Conflict);
        await Expect(Review(reviewer, 1, 1, "ChangesRequested"), HttpStatusCode.OK);
        await Expect(Save(editor, 1, 1, evidence), HttpStatusCode.Conflict);
        await Expect(Save(editor, 2, 1, evidence), HttpStatusCode.OK);
        await Expect(Review(reviewer, 3, 1, "EvidenceReviewed"), HttpStatusCode.OK);

        var reviewed = await Json(reviewer, path);
        Assert.Equal("EvidenceReviewed", reviewed.GetProperty("status").GetString());
        Assert.Empty(reviewed.GetProperty("missing").EnumerateArray());
        Assert.False(reviewed.GetProperty("productionAccessEnabled").GetBoolean());
        await Expect(Review(reviewer, 4, 1, "Withdrawn"), HttpStatusCode.OK);
        Assert.Equal("Withdrawn", (await Json(reviewer, path)).GetProperty("status").GetString());

        await Expect(Save(editor, 5, 1, evidence), HttpStatusCode.OK);
        await Expect(Mutate(editor, release, draft with { ExpectedRevision = 1 }, HttpMethod.Put), HttpStatusCode.OK);
        Assert.Equal("EvidenceStale", (await Json(reviewer, path)).GetProperty("status").GetString());
        await Expect(Review(reviewer, 6, 2, "EvidenceReviewed"), HttpStatusCode.Conflict);

        await Expect(Mutate(editor, release + "/validate", new { expectedRevision = 2 }), HttpStatusCode.OK);
        await Expect(Mutate(reviewer, release + "/approve", new { expectedRevision = 3 }), HttpStatusCode.Conflict);
        await Expect(Mutate(editor, release + "/publish", new { expectedRevision = 3 }), HttpStatusCode.Conflict);
        using var customer = await BillingBuyer();
        var company = await PaidCompany(customer, "PROVIDER-REVIEW");
        var catalog = ConnectPath(company) + "/catalog/qbo";
        Assert.False((await Json(customer, catalog)).GetProperty("canConnect").GetBoolean());
        await Expect(Mutate(customer, catalog + "/authorize", new { }), HttpStatusCode.NotFound);
        await Expect(customer.GetAsync(path), HttpStatusCode.Forbidden);
        using var production = Host("Production");
        using var productionClient = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await Expect(productionClient.GetAsync(VendorConnectPath + "/production-readiness"), HttpStatusCode.NotFound);
        await Db(async db =>
        {
            var mutation = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Set<ProviderReadinessEvent>().Where(x => x.VersionId == version).ExecuteDeleteAsync());
            Assert.Equal("P0001", mutation.SqlState);
        });
    }
}
