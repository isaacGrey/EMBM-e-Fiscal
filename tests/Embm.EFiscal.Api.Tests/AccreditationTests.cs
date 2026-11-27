using System.Net;
using Embm.EFiscal.Api.Modules.Reporting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task SchemaRegistrySeparatesInternalSimulationAndUnverifiedOfficialProfiles()
    {
        const string path = "/api/vendor/regulatory-schemas";
        using var reader = await ConnectorVendor(VendorAccreditation.View);
        using var denied = await ConnectorVendor();
        await Expect(denied.GetAsync(path), HttpStatusCode.Forbidden);
        var result = await Json(reader, path);
        Assert.False(result.GetProperty("officialValidationAvailable").GetBoolean());
        Assert.Equal(2, result.GetProperty("reserved").GetArrayLength());
        var versions = result.GetProperty("versions").EnumerateArray().ToArray();
        Assert.Contains(versions, x => x.GetProperty("version").GetString() == CanonicalInvoices.Version &&
            x.GetProperty("classification").GetString() == "InternalCanonical");
        Assert.Contains(versions, x => x.GetProperty("version").GetString() == RegulatorySchemas.SimulationVersion &&
            x.GetProperty("environment").GetString() == "Development");
        await Db(async db =>
        {
            var schema = await db.Set<RegulatorySchemaVersion>().AsNoTracking().SingleAsync(x => x.Version == CanonicalInvoices.Version);
            Assert.True(StructuredInvoiceValidation.SchemaCompatible(schema, "CreditMemo"));
            Assert.False(StructuredInvoiceValidation.SchemaCompatible(schema, "SalesOrder"));
            schema.Classification = "FutureOfficialCertification";
            schema.Environment = "Disabled";
            schema.Verification = "Unverified";
            Assert.False(StructuredInvoiceValidation.SchemaCompatible(schema, "SalesInvoice"));
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Set<RegulatorySchemaVersion>()
                .Where(x => x.Version == CanonicalInvoices.Version).ExecuteDeleteAsync());
            Assert.Equal("P0001", rejected.SqlState);
        });
    }

    [PostgresFact]
    public async Task ProviderAccreditationRegistryIsPrivateVersionedAndCannotGrantOfficialStatus()
    {
        const string path = "/api/vendor/accreditation";
        using var editor = await ConnectorVendor(VendorAccreditation.Grants);
        using var reader = await ConnectorVendor(VendorAccreditation.View);
        using var denied = await ConnectorVendor();
        using var customer = await BillingBuyer();
        await Expect(denied.GetAsync(path), HttpStatusCode.Forbidden);
        await Expect(customer.GetAsync(path), HttpStatusCode.Forbidden);

        var application = path + "/Application/embm-efiscal";
        await Expect(SaveAccreditation(reader, application, new { title = "EMBM eFiscal", status = "Preparing" }), HttpStatusCode.Forbidden);
        await Expect(SaveAccreditation(editor, application, new { title = "EMBM eFiscal", status = "Accredited" }), HttpStatusCode.BadRequest);
        var applicationOverview = await Json(reader, path);
        Assert.False(applicationOverview.GetProperty("officialAccreditationVerified").GetBoolean());
        Assert.Equal("EMBM Services OPC", applicationOverview.GetProperty("provider").GetString());
        Assert.Equal(15, applicationOverview.GetProperty("checklist").GetArrayLength());

        var key = "requirement-" + Guid.NewGuid().ToString("N");
        var target = path + "/Requirement/" + key;
        var input = new
        {
            title = "Electronic invoice and sales reporting scope",
            status = "UnderReview",
            reference = "RMC No. 98-2026",
            section = "Section III",
            description = "Synthetic review of electronic invoice and sales reporting scope.",
            applicability = "For internal applicability review.",
            sourceUrl = "https://bir-cdn.bir.gov.ph/BIR/pdf/RMC%20No.%2098-2026%20Digest.pdf",
            expectedVersion = 0
        };
        await Expect(SaveAccreditation(editor, target, input with { sourceUrl = "https://bir.gov.ph.example.test/fake" }), HttpStatusCode.BadRequest);
        await Expect(SaveAccreditation(editor, target, input with { status = "SourceReviewed" }), HttpStatusCode.BadRequest);
        await Expect(SaveAccreditation(editor, target, input), HttpStatusCode.OK);
        await Expect(SaveAccreditation(editor, target, input), HttpStatusCode.Conflict);
        await Expect(SaveAccreditation(editor, target, input with { expectedVersion = 1, status = "AwaitingExternalValidation" }), HttpStatusCode.OK);
        Assert.Equal(2, (await Json(reader, target)).GetArrayLength());
        Assert.Equal("""{"synthetic":true}""", await reader.GetStringAsync(target + "/1/evidence"));
        await Expect(denied.GetAsync(target + "/1/evidence"), HttpStatusCode.Forbidden);

        var changeKey = "change-" + Guid.NewGuid().ToString("N");
        var change = path + "/MaterialChange/" + changeKey;
        var assessment = new { title = "Reporting evidence version", status = "UnderReview", solutionVersion = "development",
            notes = "Review effect on internal reporting evidence.", expectedVersion = 0 };
        await Expect(SaveAccreditation(editor, change, assessment), HttpStatusCode.OK);
        await Expect(SaveAccreditation(editor, change, assessment with { expectedVersion = 1, status = "InProgress" }), HttpStatusCode.OK);
        Assert.Equal(2, (await Json(reader, change)).GetArrayLength());
        await Db(async db =>
        {
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Set<AccreditationVersion>()
                .Where(x => x.Key == key).ExecuteDeleteAsync());
            Assert.Equal("P0001", rejected.SqlState);
        });
    }

    private static async Task<HttpResponseMessage> SaveAccreditation(HttpClient browser, string path, object input)
    {
        var token = (await Json(browser, "/api/auth/csrf")).GetProperty("token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-EFiscal-CSRF", token);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(System.Text.Json.JsonSerializer.Serialize(input)), "metadata");
        content.Add(new StringContent("""{"synthetic":true}"""), "evidence", "synthetic.json");
        request.Content = content;
        return await browser.SendAsync(request);
    }
}
