using System.Net;
using Embm.EFiscal.Api.Modules.Reporting;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task ContinuityReferencesAreScopedAndDoNotIssueInvoicesOrChargeCredits()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "CONTINUITY");
        var path = f.Path.Replace("/documents", "/continuity", StringComparison.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var issue = new { branchId = f.Branch, startedAt = now.AddMinutes(-30), restoredAt = (DateTimeOffset?)null,
            components = new[] { "Reporting" }, reportingOperationIds = Array.Empty<Guid>(), notes = "Synthetic outage", state = "Investigating", expectedVersion = 0 };
        var recorded = await RequestJson(owner, path + "/incidents", issue);
        var incidentId = recorded.GetProperty("details").GetProperty("id").GetGuid();
        var before = await Balance(owner, f.Subscriber);
        var manual = new { incidentId, branchId = f.Branch, series = "SYNTHETIC", number = "01", date = RegulatoryEligibility.ManilaDate(issue.startedAt),
            originalEvidenceSha256 = new string('a', 64), reviewNotes = "Requires review before any electronic treatment" };
        var reference = await RequestJson(owner, path + "/manual-invoices", manual);
        Assert.Equal("RequiresVerifiedReview", reference.GetProperty("details").GetProperty("eligibility").GetString());
        await Expect(Mutate(owner, path + "/manual-invoices", manual with { series = "synthetic" }), HttpStatusCode.Conflict);
        Assert.Empty((await Json(owner, f.Path + "/SalesInvoice/register")).GetProperty("items").EnumerateArray());
        await RequestJson(owner, path + "/incidents", new { id = incidentId, issue.branchId, issue.startedAt, restoredAt = now,
            issue.components, issue.reportingOperationIds, issue.notes, state = "Closed", expectedVersion = 1 });
        Assert.Equal(3, (await Json(owner, path)).GetProperty("entries").GetArrayLength());
        Assert.Equal(before, await Balance(owner, f.Subscriber));

        var invoice = Input(f, "SalesInvoice");
        var issued = await CommitDocument(owner, f, "SalesInvoice", invoice, await DocPreview(owner, f, "SalesInvoice", invoice));
        var document = issued.GetProperty("id").GetGuid();
        await RequestJson(owner, path + "/deadline-policy", new
        {
            obligation = "Fictional demonstration deadline", commencement = invoice.Date, calendarDays = 15,
            warningDays = 2, recoveryDays = 3, expectedVersion = 0
        });
        var preview = await Json(owner, path + "/deadline-preview/" + document + "?incident=" + incidentId);
        Assert.Equal("DevelopmentSimulation", preview.GetProperty("mode").GetString());
        Assert.Equal(invoice.Date.AddDays(15).ToString("yyyy-MM-dd"), preview.GetProperty("deadline").GetString());
        Assert.False(preview.GetProperty("legalOverdue").GetBoolean());
        Assert.Null(preview.GetProperty("governmentSubmissionAt").GetString());

        using var observer = await ConnectorVendor(RegulatoryConnector.View);
        var monitor = (await Json(observer, "/api/vendor/bir-connector")).GetProperty("governance");
        Assert.Contains(monitor.GetProperty("incidents").EnumerateArray(), x =>
            x.GetProperty("state").GetString() == "Closed" && x.GetProperty("count").GetInt32() > 0);
        Assert.DoesNotContain(incidentId.ToString(), monitor.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task ProviderBillingShowsCommercialChargesSeparatelyFromClientFiscalDocuments()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "PROVIDER-BILLING");
        var path = $"/api/billing/subscribers/{f.Subscriber}/provider-records";
        var before = await Balance(owner, f.Subscriber);
        var register = await Json(owner, path);
        Assert.False(register.GetProperty("productionIssuanceEnabled").GetBoolean());
        var payment = Assert.Single(register.GetProperty("items").EnumerateArray());
        Assert.Equal("InitialSubscription", payment.GetProperty("purpose").GetString());
        Assert.Null(payment.GetProperty("invoiceReference").GetString());
        Assert.Null(payment.GetProperty("tax").GetString());
        Assert.Equal(before, await Balance(owner, f.Subscriber));
        var (_, _, email) = await SeedSubscriber();
        using var outsider = Browser();
        await Login(outsider, email, _password);
        await Expect(outsider.GetAsync(path), HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task InfrastructureEvidenceCannotClaimProductionApproval()
    {
        using var editor = await ConnectorVendor(VendorAccreditation.Grants);
        using var denied = await ConnectorVendor();
        var path = "/api/vendor/accreditation/Infrastructure/fixture-" + Guid.NewGuid().ToString("N");
        var data = new { title = "Synthetic infrastructure", status = "UnderReview", hostingProvider = "Fictional hosting",
            primaryRegion = "Development", databaseLocation = "Isolated PostgreSQL", dataClassification = "Synthetic", expectedVersion = 0 };
        await Expect(SaveAccreditation(editor, path, data with { status = "ProductionApproved" }), HttpStatusCode.BadRequest);
        await Expect(SaveAccreditation(editor, path, data), HttpStatusCode.OK);
        Assert.Single((await Json(editor, path)).EnumerateArray());
        await Expect(denied.GetAsync(path), HttpStatusCode.Forbidden);
    }
}
