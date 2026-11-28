using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task ReportingJobsPreservePinnedPayloadsAcrossRetriesAndUncertainOutcomes()
    {
        using var creator = await ConnectorVendor(RegulatoryConnector.View, RegulatoryConnector.ConfigureGrant, RegulatoryConnector.Review);
        using var reviewer = await ConnectorVendor(RegulatoryConnector.Review);
        using var activator = await ConnectorVendor(RegulatoryConnector.Activate);
        var register = await Json(creator, BirConnectorPath);
        var latest = register.GetProperty("versions").EnumerateArray().FirstOrDefault();
        using var config = await UploadBirConnector(creator, latest.ValueKind == JsonValueKind.Undefined ? 0 : latest.GetProperty("number").GetInt32(),
            SyntheticSpecification, batch: true);
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
        var version = (await config.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetInt32();
        var transitions = BirConnectorPath + "/" + version + "/transitions";
        await RequestJson(creator, transitions, new { state = "UnderReview", expectedSequence = 1, reason = "Synthetic review" });
        await RequestJson(reviewer, transitions, new { state = "ConfigurationValidated", expectedSequence = 2, reason = "Bounded fixture" });
        await RequestJson(reviewer, transitions, new { state = "ApprovedForActivation", expectedSequence = 3, reason = "Approved simulation" });
        await RequestJson(activator, transitions, new { state = "Active", expectedSequence = 4, reason = "Simulate only" });

        using var owner = await BillingBuyer();
        var company = await PaidCompany(owner, "DURABLE-REG");
        var wallet = await Balance(owner, company.Subscriber);
        var input = Input(company, "SalesInvoice");
        var docs = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var doc = await CommitDocument(owner, company, "SalesInvoice", input, await DocPreview(owner, company, "SalesInvoice", input));
            docs.Add(doc.GetProperty("id").GetGuid());
        }
        var operations = company.Path.Replace("/documents", "/reporting/operations");
        Assert.Empty((await Json(owner, operations)).GetProperty("items").EnumerateArray());
        async Task<Guid[]> Queue(string mode, string? scenario, params Guid[] ids)
        {
            var data = await RequestJson(owner, operations, new { documents = ids, mode, scenario, connectorVersion = version });
            Assert.Equal("NotSubmitted", data.GetProperty("governmentStatus").GetString());
            return data.GetProperty("operations").EnumerateArray().Select(x => x.GetGuid()).ToArray();
        }
        async Task<JsonElement> Wait(Guid id, string state)
        {
            var result = await WaitForDocument(owner, operations, x => x.GetProperty("items").EnumerateArray().Any(j =>
                j.GetProperty("id").GetGuid() == id && j.GetProperty("state").GetString() == state));
            return result.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        }

        var prep = Assert.Single(await Queue("Preparation", null, docs[0]));
        await Wait(prep, "Prepared");
        Assert.Equal(prep, Assert.Single(await Queue("Preparation", null, docs[0])));
        var uncertain = Assert.Single(await Queue("DevelopmentSimulation", "TimeoutAfterAcceptance", docs[0]));
        var beforeVerification = await Wait(uncertain, "UncertainOutcome");
        Assert.Equal(64, beforeVerification.GetProperty("payloadHash").GetString()!.Length);
        Assert.Equal(version, beforeVerification.GetProperty("connectorVersion").GetInt32());
        Assert.Equal(uncertain, Assert.Single(await Queue("DevelopmentSimulation", "TimeoutAfterAcceptance", docs[0])));
        await Expect(Mutate(owner, operations + "/" + uncertain + "/verify", new { reason = "Verify without resending" }), HttpStatusCode.NoContent);
        await Wait(uncertain, "SimulatedAcknowledged");
        var history = await Json(owner, operations + "/" + uncertain + "/attempts");
        Assert.Equal("NotSubmitted", history.GetProperty("governmentStatus").GetString());
        var attempts = history.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(["Submit", "Verify"], attempts.Select(x => x.GetProperty("action").GetString()));
        Assert.StartsWith("SIM-BIR-ACK-", attempts[1].GetProperty("reference").GetString());
        await Expect(Mutate(owner, operations + "/" + uncertain + "/verify", new { reason = "Already resolved" }), HttpStatusCode.Conflict);

        var delayed = Assert.Single(await Queue("DevelopmentSimulation", "DelayedAcknowledgment", docs[1]));
        await Wait(delayed, "SimulatedAcknowledged");
        var limited = Assert.Single(await Queue("DevelopmentSimulation", "RateLimited", docs[2]));
        await Wait(limited, "RetryScheduled");
        await Db(async db => await db.Set<ReportingOperation>().Where(x => x.Id == limited && x.State == "RetryScheduled")
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.State, "Processing").SetProperty(t => t.Lease, Guid.NewGuid())
                .SetProperty(t => t.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await Wait(limited, "SimulatedAcknowledged");
        var batch = await Queue("DevelopmentSimulation", "PartialBatch", docs[3], docs[4]);
        Assert.Equal(2, batch.Length);
        await Wait(batch[0], "SimulatedAcknowledged");
        await Wait(batch[1], "SimulatedRejected");
        Assert.Equal(wallet - docs.Count, await Balance(owner, company.Subscriber));
        await RequestJson(activator, transitions, new { state = "Disabled", expectedSequence = 5, reason = "End development fixture" });
    }
}
