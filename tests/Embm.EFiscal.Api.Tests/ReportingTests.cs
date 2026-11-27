using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task ReportingPreparationIsScopedImmutableIdempotentAndNeverSubmitsOrCharges()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "REPORT");
        var input = Input(f, "SalesInvoice", "2");
        var invoice = await CommitDocument(owner, f, "SalesInvoice", input, await DocPreview(owner, f, "SalesInvoice", input));
        var id = invoice.GetProperty("id").GetGuid();
        var path = f.Path.Replace("/documents", "/reporting");
        var target = path + "/" + id;
        var readiness = await Json(owner, path + "/readiness");
        Assert.False(readiness.GetProperty("productionIssuanceEnabled").GetBoolean());
        Assert.False(readiness.GetProperty("liveTransmissionEnabled").GetBoolean());
        Assert.Equal("001", readiness.GetProperty("companyRdo").GetProperty("code").GetString());
        Assert.Single(readiness.GetProperty("branches").EnumerateArray());
        var balance = await Balance(owner, f.Subscriber);
        await Expect(Mutate(owner, target + "/simulate", new { }), HttpStatusCode.Conflict);
        var first = await RequestJson(owner, target + "/prepare", new { });
        var replay = await RequestJson(owner, target + "/prepare", new { });
        Assert.Equal(first.GetProperty("id").GetGuid(), replay.GetProperty("id").GetGuid());
        var simulation = await RequestJson(owner, target + "/simulate", new { });
        Assert.Equal("SimulationValidated", simulation.GetProperty("outcome").GetString());
        Assert.Equal("NotSubmitted", simulation.GetProperty("governmentStatus").GetString());
        var repeat = await RequestJson(owner, target + "/simulate", new { });
        Assert.Equal(simulation.GetProperty("id").GetGuid(), repeat.GetProperty("id").GetGuid());
        var detail = await Json(owner, target);
        Assert.Equal("embm-preparation-v1", detail.GetProperty("schemaVersion").GetString());
        Assert.False(detail.GetProperty("liveTransmissionEnabled").GetBoolean());
        var register = await Json(owner, path + "/");
        Assert.Equal("PreparationOnly", register.GetProperty("mode").GetString());
        Assert.True(Assert.Single(register.GetProperty("items").EnumerateArray()).GetProperty("simulated").GetBoolean());
        await Expect(owner.GetAsync(path + "/?page=0"), HttpStatusCode.BadRequest);
        var orderInput = Input(f, "SalesOrder", "1");
        var order = await CommitDocument(owner, f, "SalesOrder", orderInput, await DocPreview(owner, f, "SalesOrder", orderInput));
        var orderTarget = path + "/" + order.GetProperty("id").GetGuid();
        await Expect(Mutate(owner, orderTarget + "/prepare", new { }), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, orderTarget + "/simulate", new { }), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, target + "/transmit", new { }), HttpStatusCode.NotFound);
        using var production = Host("Production");
        using var productionClient = production.CreateClient();
        await Expect(productionClient.PostAsJsonAsync(target + "/simulate", new { }), HttpStatusCode.NotFound);
        var (_, _, otherEmail) = await SeedSubscriber();
        using var outsider = Browser();
        await Login(outsider, otherEmail, _password);
        await Expect(outsider.GetAsync(target), HttpStatusCode.NotFound);
        await Expect(outsider.GetAsync(path + "/readiness"), HttpStatusCode.NotFound);
        await Expect(Mutate(outsider, target + "/prepare", new { }), HttpStatusCode.NotFound);
        await Expect(Mutate(outsider, target + "/simulate", new { }), HttpStatusCode.NotFound);
        Assert.Equal(balance, await Balance(owner, f.Subscriber));
        await Db(async db =>
        {
            var member = await db.SubscriberMemberships.SingleAsync(x => x.SubscriberId == f.Subscriber);
            db.CompanyMemberships.Add(new Embm.EFiscal.Api.Modules.Tenants.CompanyMembership { SubscriberId = f.Subscriber, CompanyId = f.Company, UserId = member.UserId });
            await db.SaveChangesAsync();
            await db.SubscriberMemberships.Where(x => x.SubscriberId == f.Subscriber).ExecuteUpdateAsync(s => s.SetProperty(x => x.RoleId, "auditor"));
        });
        await Expect(owner.GetAsync(target), HttpStatusCode.OK);
        await Expect(Mutate(owner, target + "/prepare", new { }), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, target + "/simulate", new { }), HttpStatusCode.NotFound);
        await Db(async db =>
        {
            var row = await db.ReportingPreparations.SingleAsync(x => x.DocumentId == id);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Content))), row.Sha256);
            Assert.Equal("AuthorizationRequired", (await db.DocumentProcessing.SingleAsync(x => x.DocumentId == id)).Regulator);
            Assert.Equal(1, await db.ReportingSimulations.CountAsync(x => x.PreparationId == row.Id));
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.ReportingPreparations.Where(x => x.Id == row.Id).ExecuteDeleteAsync());
            Assert.Equal("P0001", rejected.SqlState);
        });
    }
}
