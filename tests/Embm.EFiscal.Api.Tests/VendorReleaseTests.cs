using System.Net;
using Embm.EFiscal.Api.Modules.Connect;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task VendorReleaseGovernancePreservesPinsAndEnforcesEmergencyControls()
    {
        using var editor = await ConnectorVendor(ConnectorRules.View, ConnectorRules.Edit);
        using var reviewer = await ConnectorVendor(ConnectorRules.View, ConnectorRules.ApproveRelease);
        using var publisher = await ConnectorVendor(ConnectorRules.View, ConnectorRules.Publish);
        using var owner = await BillingBuyer();

        var input = Draft();
        var draft = input with { Configuration = input.Configuration with { Capabilities =
            [.. input.Configuration.Capabilities, new("SalesOrderRead", "Development Verified", null), new("SalesOrderLineRead", "Development Verified", null)] } };
        var first = await RequestJson(editor, VendorConnectPath, draft);
        var connectorId = first.GetProperty("connectorId").GetGuid();
        var connector = $"{VendorConnectPath}/{connectorId}";
        var firstId = first.GetProperty("id").GetGuid();
        var initial = connector + "/versions/" + firstId;
        var versionOne = await RequestJson(editor, initial + "/validate", new { expectedRevision = 1 });
        Assert.True(versionOne.GetProperty("validation").GetProperty("valid").GetBoolean());
        await Expect(Mutate(publisher, initial + "/approve", new { expectedRevision = 2 }), HttpStatusCode.Forbidden);
        await RequestJson(reviewer, initial + "/approve", new { expectedRevision = 2 });
        await Expect(Mutate(reviewer, initial + "/publish", new { expectedRevision = 3 }), HttpStatusCode.Forbidden);
        await RequestJson(publisher, initial + "/publish", new { expectedRevision = 3 });

        var company = await PaidCompany(owner, "RELEASE");
        var catalog = ConnectPath(company) + "/catalog/" + draft.Code;
        var oldBinding = await BindFixture(owner, company, draft.Code!, "sample-company-mnl");
        await PrepareFixture(owner, company, oldBinding);
        var oldOrders = ConnectPath(company) + "/connections/" + oldBinding + "/sales-orders";
        await Expect(owner.GetAsync(oldOrders), HttpStatusCode.OK);

        async Task<Guid> ApproveCopy()
        {
            var copy = await RequestJson(editor, initial + "/copy", new { });
            var id = copy.GetProperty("id").GetGuid();
            var versionPath = connector + "/versions/" + id;
            await RequestJson(editor, versionPath + "/validate", new { expectedRevision = 1 });
            await RequestJson(reviewer, versionPath + "/approve", new { expectedRevision = 2 });
            return id;
        }

        var olderCopy = await ApproveCopy();
        var candidate = await ApproveCopy();
        var candidatePath = connector + "/versions/" + candidate;
        await Expect(Mutate(publisher, connector + "/disable", new { }), HttpStatusCode.NoContent);
        await RequestJson(publisher, candidatePath + "/publish", new { expectedRevision = 3 });
        await Expect(Mutate(publisher, connector + "/versions/" + olderCopy + "/publish", new { expectedRevision = 3 }), HttpStatusCode.Conflict);
        Assert.False((await Json(owner, catalog)).GetProperty("canConnect").GetBoolean());
        await Expect(owner.GetAsync(oldOrders), HttpStatusCode.NotFound);
        await Expect(Mutate(publisher, connector + "/resume", new { }), HttpStatusCode.NoContent);
        await Expect(owner.GetAsync(oldOrders), HttpStatusCode.OK);
        var newBinding = await BindFixture(owner, company, draft.Code!, "sample-company-ceb");
        await PrepareFixture(owner, company, newBinding);
        var newOrders = ConnectPath(company) + "/connections/" + newBinding + "/sales-orders";
        await Expect(owner.GetAsync(newOrders), HttpStatusCode.OK);

        await Expect(Mutate(reviewer, candidatePath + "/revoke", new { reason = "Reviewed fixture incident" }), HttpStatusCode.Forbidden);
        await Expect(Mutate(publisher, candidatePath + "/revoke", new { reason = " " }), HttpStatusCode.BadRequest);
        await Expect(Mutate(publisher, candidatePath + "/revoke", new { reason = "Reviewed fixture incident" }), HttpStatusCode.NoContent);
        await Expect(Mutate(publisher, candidatePath + "/revoke", new { reason = "Repeat revocation" }), HttpStatusCode.Conflict);
        await Expect(owner.GetAsync(newOrders), HttpStatusCode.NotFound);
        await Expect(owner.GetAsync(oldOrders), HttpStatusCode.OK);
        await Expect(Mutate(owner, catalog + "/authorize", new { }), HttpStatusCode.NotFound);
        var latest = await Json(owner, catalog);
        Assert.Equal(candidate, latest.GetProperty("versionId").GetGuid());
        Assert.Equal("Revoked", latest.GetProperty("availability").GetString());

        var bindings = (await Json(owner, ConnectPath(company) + "/connections")).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(firstId, bindings.Single(x => x.GetProperty("id").GetGuid() == oldBinding).GetProperty("connectorVersionId").GetGuid());
        Assert.Equal("Suspended", bindings.Single(x => x.GetProperty("id").GetGuid() == newBinding).GetProperty("status").GetString());
        var impact = await Json(editor, candidatePath + "/impact");
        Assert.Equal("Suspended", Assert.Single(impact.GetProperty("connections").EnumerateArray()).GetProperty("status").GetString());
        Assert.Contains(impact.GetProperty("audit").EnumerateArray(), x => x.GetProperty("action").GetString() == "Connectors.VersionRevoked");
        Assert.DoesNotContain(company.Subscriber.ToString(), impact.GetRawText());
        Assert.Contains((await Json(editor, VendorConnectPath + "/runtime/monitor")).GetProperty("availability").EnumerateArray(),
            x => x.GetProperty("state").GetString() == "Revoked");
        Assert.DoesNotContain((await Json(editor, VendorConnectPath + "/runtime/options")).GetProperty("versions").EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == candidate);

        await Db(async db =>
        {
            var revoke = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Set<ConnectorVersionRevocation>().Where(x => x.VersionId == candidate).ExecuteDeleteAsync());
            Assert.Equal("P0001", revoke.SqlState);
            var released = await db.ConnectorVersions.AsNoTracking().SingleAsync(x => x.Id == candidate);
            Assert.Equal("Published", released.State);
            Assert.Equal("Reviewed fixture incident", released.Revocation!.Reason);
        });
    }
}
