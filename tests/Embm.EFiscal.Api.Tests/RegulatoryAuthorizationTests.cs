using System.Net;
using System.Net.Http.Json;
using Embm.EFiscal.Api.Modules.Reporting;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task RegulatoryEvidenceIsScopedVersionedIndependentlyReviewedAndCannotEnableIssuance()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "AUTHORIZATION");
        var path = f.Path.Replace("/documents", "/regulatory-authorizations");
        var otherCompany = await CreateCompany(owner, f.Subscriber, "OTHER-AUTH");
        var foreignBranch = (await Json(owner, $"/api/workspace/{f.Subscriber}/companies/{otherCompany}/branches"))[0].GetProperty("id").GetGuid();
        var today = RegulatoryEligibility.ManilaDate();
        var input = new RegulatoryAuthorizations.Submission(f.Branch, "PermitToIssue", "SalesInvoice", "000123",
            "Synthetic issuing authority", "EMBM eFiscal", "development", "Synthetic evidence workflow", today, today.AddDays(1), 0);
        var balance = await Balance(owner, f.Subscriber);
        await Expect(UploadAuthorization(owner, path, input with { BranchId = foreignBranch }), HttpStatusCode.NotFound);
        await Expect(UploadAuthorization(owner, path, input with { DocumentType = "SalesOrder" }), HttpStatusCode.BadRequest);
        await Expect(UploadAuthorization(owner, path, input with { Kind = "EspAccreditation" }), HttpStatusCode.BadRequest);
        await Expect(UploadAuthorization(owner, path, input with { ExpiresOn = today.AddDays(-1) }), HttpStatusCode.BadRequest);
        await Expect(UploadAuthorization(owner, path, input, "bad.pdf"), HttpStatusCode.BadRequest);
        using var submitted = await UploadAuthorization(owner, path, input);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var register = await Json(owner, path);
        var row = Assert.Single(register.GetProperty("records").EnumerateArray());
        Assert.Equal("000123", row.GetProperty("latest").GetProperty("reference").GetString());
        var id = row.GetProperty("id").GetGuid();
        var target = path + "/" + id;
        var review = target + "/versions/1/reviews";
        var download = target + "/versions/1/evidence";
        await Expect(UploadAuthorization(owner, path, input), HttpStatusCode.Conflict);
        await Expect(Mutate(owner, review, new { decision = "ReviewAccepted", reason = "Self review", expectedSequence = 0 }), HttpStatusCode.NotFound);
        using var reviewer = Browser();
        var reviewerEmail = Email("regulatory-reviewer");
        await Db(async db =>
        {
            var identity = NewIdentity(reviewerEmail);
            db.AddRange(identity, new SubscriberMembership { SubscriberId = f.Subscriber, UserId = identity.Id,
                RoleId = "regulatory-reviewer", CreatedAt = DateTimeOffset.UtcNow },
                new CompanyMembership { SubscriberId = f.Subscriber, CompanyId = f.Company, UserId = identity.Id });
            await db.SaveChangesAsync();
        });
        await Login(reviewer, reviewerEmail, _password);
        using var file = await reviewer.GetAsync(download);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("attachment", file.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("""{"synthetic":true}""", await file.Content.ReadAsStringAsync());
        await Expect(reviewer.GetAsync(path.Replace(f.Company.ToString(), otherCompany.ToString())), HttpStatusCode.NotFound);
        var (_, _, outsiderEmail) = await SeedSubscriber();
        using var outsider = Browser();
        await Login(outsider, outsiderEmail, _password);
        await Expect(outsider.GetAsync(download), HttpStatusCode.NotFound);

        Task Act(string decision, int sequence, HttpStatusCode result) =>
            Expect(Mutate(reviewer, review, new { decision, reason = "Synthetic review evidence", expectedSequence = sequence }), result);
        await Act("ExternalAuthorizationDocumented", 0, HttpStatusCode.Conflict);
        await Act("ReviewAccepted", 0, HttpStatusCode.OK);
        var eligibility = path + $"/eligibility?branch={f.Branch}&documentType=SalesInvoice";
        var internalOnly = await Json(owner, eligibility);
        Assert.Empty(internalOnly.GetProperty("applicableAuthorizations").EnumerateArray());
        await Act("ExternalAuthorizationDocumented", 1, HttpStatusCode.OK);
        var documented = await Json(owner, eligibility);
        Assert.Single(documented.GetProperty("applicableAuthorizations").EnumerateArray());
        Assert.Equal("Disabled", documented.GetProperty("issuance").GetString());
        Assert.Equal("Disabled", documented.GetProperty("transmission").GetString());
        Assert.Equal("InternalPreparationOnly", documented.GetProperty("preparation").GetString());

        await Act("Revoked", 2, HttpStatusCode.OK);
        Assert.Empty((await Json(owner, eligibility)).GetProperty("applicableAuthorizations").EnumerateArray());
        await Expect(UploadAuthorization(owner, target + "/versions", input with { ExpectedVersion = 0 }), HttpStatusCode.Conflict);
        await Expect(UploadAuthorization(owner, target + "/versions", input with { ExpectedVersion = 1, Reference = "000124" }), HttpStatusCode.Created);
        await Act("ReviewAccepted", 3, HttpStatusCode.Conflict);
        var history = await Json(owner, target + "/history");
        Assert.Equal(2, history.GetProperty("versions").GetArrayLength());
        Assert.Equal(3, history.GetProperty("reviews").GetArrayLength());
        await Expect(UploadAuthorization(owner, path, input with { Kind = "PermitToTransmit", EffectiveFrom = today.AddDays(-3),
            ExpiresOn = today.AddDays(-1) }), HttpStatusCode.Created);
        var expired = await Json(owner, eligibility);
        Assert.Equal("PermitToTransmit", Assert.Single(expired.GetProperty("expiredAuthorizations").EnumerateArray()).GetProperty("kind").GetString());
        Assert.Equal(balance, await Balance(owner, f.Subscriber));
        await Db(async db =>
        {
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Set<RegulatoryAuthorizationVersion>()
                .Where(x => x.AuthorizationId == id && x.Number == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Reference, "Changed")));
            Assert.Equal("P0001", rejected.SqlState);
        });
    }

    private static async Task<HttpResponseMessage> UploadAuthorization(HttpClient browser, string path,
        RegulatoryAuthorizations.Submission input, string name = "synthetic.json")
    {
        var token = (await Json(browser, "/api/auth/csrf")).GetProperty("token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-EFiscal-CSRF", token);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(System.Text.Json.JsonSerializer.Serialize(input)), "metadata");
        content.Add(new StringContent("""{"synthetic":true}"""), "evidence", name);
        request.Content = content;
        return await browser.SendAsync(request);
    }
}
