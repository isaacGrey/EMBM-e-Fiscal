using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task TaxpayerExportAndOriginalRecordsSurviveCommercialTermination()
    {
        using var owner = await BillingBuyer();
        var sample = await PaidCompany(owner, "PORTABLE");
        var other = await CreateCompany(owner, sample.Subscriber, "NOT-EXPORTED");
        var input = Input(sample, "SalesInvoice");
        var created = await CommitDocument(owner, sample, "SalesInvoice", input, await DocPreview(owner, sample, "SalesInvoice", input));
        var id = created.GetProperty("id").GetGuid();
        var originals = sample.Path + "/SalesInvoice/" + id;
        await WaitForDocument(owner, originals, x => x.GetProperty("rendering").GetString() == "Ready");
        var company = sample.Path.Replace("/documents", "", StringComparison.Ordinal);
        var export = company + "/portability";
        var report = company + "/reporting/" + id;
        await Db(async db => await db.Subscribers.Where(x => x.Id == sample.Subscriber)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, CommercialStatus.Cancelled)));

        await Expect(owner.GetAsync(originals), HttpStatusCode.OK);
        await Expect(owner.GetAsync(originals + "/pdf"), HttpStatusCode.OK);
        await Expect(Mutate(owner, report + "/prepare"), HttpStatusCode.OK);
        await Expect(Mutate(owner, sample.Path + "/SalesInvoice/preview", input), HttpStatusCode.NotFound);

        var period = new { from = input.Date, through = input.Date };
        var job = await RequestJson(owner, export, period);
        Assert.Equal(1, job.GetProperty("documentCount").GetInt32());
        var archive = export + "/" + job.GetProperty("id").GetGuid() + "/archive";
        var bytes = await owner.GetByteArrayAsync(archive);
        Assert.Equal(job.GetProperty("sha256").GetString(), Hash(bytes));
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        Assert.Equal(sample.Company, manifest.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal(1, manifest.RootElement.GetProperty("documentCount").GetInt32());
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            using var data = zip.GetEntry(file.GetProperty("path").GetString()!)!.Open();
            using var copy = new MemoryStream();
            await data.CopyToAsync(copy);
            Assert.Equal(file.GetProperty("sha256").GetString(), Hash(copy.ToArray()));
        }
        Assert.Contains(zip.Entries, entry => entry.FullName == $"pdf/{id:N}.pdf");
        Assert.Contains(zip.Entries, entry => entry.FullName.StartsWith($"reporting/{id:N}/", StringComparison.Ordinal));
        Assert.Single((await Json(owner, export)).GetProperty("items").EnumerateArray());
        await Expect(owner.GetAsync($"/api/workspace/{sample.Subscriber}/companies/{other}/portability/{job.GetProperty("id").GetGuid()}/archive"),
            HttpStatusCode.NotFound);
        var (_, _, email) = await SeedSubscriber();
        using var outsider = Browser();
        await Login(outsider, email, _password);
        await Expect(outsider.GetAsync(archive), HttpStatusCode.NotFound);

        await Db(async db => Assert.Equal(1, await db.CreditLedgerEntries.CountAsync(x =>
            x.SubscriberId == sample.Subscriber && x.EventType == "Consumption")));

        static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    [PostgresFact]
    public async Task LegalHoldReleaseIsOwnerOnlyAndKeepsOriginalHold()
    {
        using var owner = await BillingBuyer();
        var sample = await PaidCompany(owner, "PRESERVATION");
        var path = sample.Path.Replace("/documents", "/preservation", StringComparison.Ordinal);
        var hold = await RequestJson(owner, path + "/holds", new
        {
            branchId = sample.Branch, category = "Documents", from = "2026-01-01", through = "2026-12-31",
            reason = "Preserve documents during a synthetic investigation"
        });
        var releasePath = path + "/holds/" + hold.GetProperty("id").GetGuid() + "/release";
        var policy = await RequestJson(owner, path + "/policies", new
        {
            category = "Documents", effectiveFrom = "2026-01-01", preservation = "Preserve original evidence",
            migrationEvidence = "Synthetic archive restore check", expectedVersion = 0
        });
        Assert.Equal(1, policy.GetProperty("version").GetInt32());
        await Expect(Mutate(owner, path + "/policies", new
        {
            category = "Documents", effectiveFrom = "2026-01-01", preservation = "Retain record", expectedVersion = 0
        }), HttpStatusCode.Conflict);
        var member = await DbMember();
        await Db(async db =>
        {
            db.CompanyMemberships.Add(new CompanyMembership { SubscriberId = sample.Subscriber, CompanyId = sample.Company, UserId = member });
            await db.SaveChangesAsync();
            await db.SubscriberMemberships.Where(x => x.SubscriberId == sample.Subscriber)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.RoleId, Permissions.Administrator));
        });
        var request = new { reason = "Synthetic investigation closed with release authority" };
        await Expect(Mutate(owner, releasePath, request), HttpStatusCode.NotFound);
        await Db(async db => await db.SubscriberMemberships.Where(x => x.SubscriberId == sample.Subscriber)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RoleId, Permissions.Owner)));
        await RequestJson(owner, releasePath, request);
        await Expect(Mutate(owner, releasePath, request), HttpStatusCode.Conflict);
        var register = await Json(owner, path);
        var entry = Assert.Single(register.GetProperty("holds").EnumerateArray());
        Assert.Equal(hold.GetProperty("reason").GetString(), entry.GetProperty("hold").GetProperty("reason").GetString());
        Assert.Equal(request.reason, entry.GetProperty("release").GetProperty("reason").GetString());
        Assert.Equal("PreserveAll", register.GetProperty("disposalMode").GetString());

        async Task<Guid> DbMember()
        {
            Guid id = default;
            await Db(async db => id = await db.SubscriberMemberships.Where(x => x.SubscriberId == sample.Subscriber).Select(x => x.UserId).SingleAsync());
            return id;
        }
    }
}
