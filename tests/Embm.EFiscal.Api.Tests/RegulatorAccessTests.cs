using System.Net;
using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Embm.EFiscal.Api.Modules.Reporting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    private WebApplicationFactory<Program> RegulatorHost(string environment) => Host(environment).WithWebHostBuilder(builder =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            [RegulatorAccess.Setting] = "true"
        })));

    [PostgresFact]
    public async Task RegulatorReadsIntersectGrantsWithTenantBranchAndEffectiveRdo()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "REGULATOR");
        var branchPath = $"/api/workspace/{f.Subscriber}/companies/{f.Company}/branches";
        var secondBranch = (await RequestJson(owner, branchPath, Branch("DISTRICT") with { RdoCode = "009" })).GetProperty("id").GetGuid();
        var invoice = Input(f, "SalesInvoice");
        var committed = await CommitDocument(owner, f, "SalesInvoice", invoice, await DocPreview(owner, f, "SalesInvoice", invoice));
        var documentId = committed.GetProperty("id").GetGuid();
        var otherInvoice = invoice with { BranchId = secondBranch };
        var otherDoc = (await CommitDocument(owner, f, "SalesInvoice", otherInvoice,
            await DocPreview(owner, f, "SalesInvoice", otherInvoice))).GetProperty("id").GetGuid();
        var order = Input(f, "SalesOrder");
        await CommitDocument(owner, f, "SalesOrder", order, await DocPreview(owner, f, "SalesOrder", order));

        var email = Email("regulator");
        var otherEmail = Email("regulator-other");
        var now = DateTimeOffset.UtcNow;
        var companyGrant = Guid.Empty;
        var districtGrant = Guid.Empty;
        var nationwideGrant = Guid.Empty;
        var foreignGrant = Guid.Empty;
        var futureGrant = Guid.Empty;
        var expiredGrant = Guid.Empty;
        await Db(async db =>
        {
            foreach (var address in new[] { email, otherEmail })
            {
                var identity = NewIdentity(address);
                identity.Kind = IdentityKind.Regulator;
                identity.DevelopmentFixture = true;
                db.Add(identity);
            }
            await db.SaveChangesAsync();
            await db.RdoAssignments.Where(x => x.SubscriberId == f.Subscriber && x.CompanyId == f.Company)
                .ExecuteUpdateAsync(x => x.SetProperty(a => a.EffectiveFrom, invoice.Date.AddDays(-1)));
            db.Add(new ReconciliationCheck { SubscriberId = f.Subscriber, CompanyId = f.Company, DocumentId = documentId,
                BranchId = f.Branch, UnitKey = documentId.ToString(), Type = "SalesInvoice", Status = "ExactMatch" });
            db.Add(new ReportingOperation { SubscriberId = f.Subscriber, CompanyId = f.Company, DocumentId = documentId,
                BranchId = f.Branch, Mode = "Preparation", Scenario = "", SchemaVersion = "embm-preparation-v1", State = "Prepared" });
            await db.SaveChangesAsync();

            var input = new RegulatorFixtureCommands.GrantInput(email, "Company", f.Subscriber, f.Company, f.Branch,
                null, RegulatorAccess.Categories, true, "Synthetic fixture", now.AddMinutes(-5), now.AddHours(1));
            companyGrant = await RegulatorFixtureCommands.Grant(db, input);
            await Assert.ThrowsAsync<ArgumentException>(() => RegulatorFixtureCommands.Grant(db, input with { CompanyId = Guid.NewGuid() }));
            var district = input with { Scope = "Rdo", SubscriberId = null, CompanyId = null, BranchId = null,
                RdoCode = "001", Categories = ["documents"], CanExport = false };
            districtGrant = await RegulatorFixtureCommands.Grant(db, district);
            nationwideGrant = await RegulatorFixtureCommands.Grant(db, district with { Scope = "Nationwide", RdoCode = null });
            foreignGrant = await RegulatorFixtureCommands.Grant(db, input with { Email = otherEmail });
            futureGrant = await RegulatorFixtureCommands.Grant(db, input with { StartsAt = now.AddMinutes(5) });
            var regulator = await db.UserIdentities.SingleAsync(x => x.Email == email);
            var expired = new RegulatorGrant { UserId = regulator.UserId, Scope = "Nationwide", Categories = ["documents"],
                Reference = "Expired synthetic authorization", StartsAt = now.AddDays(-2), ExpiresAt = now.AddDays(-1) };
            db.Add(expired);
            await db.SaveChangesAsync();
            expiredGrant = expired.Id;
        });

        using var app = RegulatorHost("Development");
        using var reader = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string grants = "/api/bir/grants";
        await Expect(reader.GetAsync(grants), HttpStatusCode.Unauthorized);
        using var customer = app.CreateClient();
        await Login(customer, (await Json(owner, "/api/auth/me")).GetProperty("email").GetString()!, _password);
        await Expect(customer.GetAsync(grants), HttpStatusCode.Forbidden);
        await Expect(Mutate(reader, "/api/auth/login", new { email, password = _password }), HttpStatusCode.Unauthorized);
        await Expect(Mutate(reader, "/api/auth/bir/login", new { email, password = _password, rdo = "009" }), HttpStatusCode.NoContent);
        Assert.Equal("Regulator", (await Json(reader, "/api/auth/me")).GetProperty("kind").GetString());
        Assert.Equal(3, (await Json(reader, grants)).GetProperty("items").GetArrayLength());
        await Expect(reader.GetAsync("/api/workspace"), HttpStatusCode.Forbidden);
        await Expect(reader.GetAsync("/api/vendor/accreditation"), HttpStatusCode.Forbidden);

        string Path(Guid grant, string category = "documents", Guid? company = null) => $"/api/bir/records/{category}?grantId={grant}"
            + $"&subscriberId={f.Subscriber}&companyId={company ?? f.Company}&from={invoice.Date:yyyy-MM-dd}&through={invoice.Date:yyyy-MM-dd}";
        var doc = Assert.Single((await Json(reader, Path(companyGrant))).GetProperty("items").EnumerateArray());
        Assert.Equal(documentId, doc.GetProperty("id").GetGuid());
        Assert.False(doc.TryGetProperty("customerName", out _));

        var summary = Assert.Single((await Json(reader, Path(companyGrant, "tax-summary"))).GetProperty("items").EnumerateArray());
        Assert.Equal(1, summary.GetProperty("count").GetInt32());
        Assert.Equal(112, summary.GetProperty("total").GetDecimal());
        Assert.Equal("ExactMatch", Assert.Single((await Json(reader, Path(companyGrant, "reconciliation")))
            .GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
        Assert.Equal("NotSubmitted", Assert.Single((await Json(reader, Path(companyGrant, "reporting")))
            .GetProperty("items").EnumerateArray()).GetProperty("governmentStatus").GetString());
        Assert.Contains((await Json(reader, Path(companyGrant, "audit"))).GetProperty("items").EnumerateArray(),
            row => row.GetProperty("action").GetString() == "Documents.SalesInvoice.Committed");

        await Expect(reader.GetAsync(Path(companyGrant, company: Guid.NewGuid())), HttpStatusCode.NotFound);
        await Expect(reader.GetAsync(Path(companyGrant) + $"&branchId={secondBranch}"), HttpStatusCode.NotFound);
        await Expect(reader.GetAsync(Path(foreignGrant)), HttpStatusCode.NotFound);
        await Expect(reader.GetAsync(Path(futureGrant)), HttpStatusCode.NotFound);
        await Expect(reader.GetAsync(Path(expiredGrant)), HttpStatusCode.NotFound);
        await Expect(reader.GetAsync(Path(districtGrant) + "&rdo=009"), HttpStatusCode.NotFound);
        await Expect(reader.GetAsync(Path(districtGrant, "audit")), HttpStatusCode.NotFound);
        await Expect(reader.GetAsync(Path(districtGrant) + "&export=true"), HttpStatusCode.NotFound);
        var districtDocs = (await Json(reader, Path(districtGrant))).GetProperty("items").EnumerateArray();
        Assert.Equal(documentId, Assert.Single(districtDocs).GetProperty("id").GetGuid());
        var nationwide = (await Json(reader, Path(nationwideGrant))).GetProperty("items").EnumerateArray();
        Assert.Equal(2, nationwide.Count());
        Assert.Contains(nationwide, row => row.GetProperty("id").GetGuid() == otherDoc);

        using var export = await reader.GetAsync(Path(companyGrant) + "&export=true");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("attachment", export.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(export.Headers.CacheControl?.NoStore);
        await Db(async db =>
        {
            Assert.True(await db.Set<RegulatorReadAudit>().AnyAsync(x => x.GrantId == companyGrant &&
                x.CompanyId == f.Company && x.BranchId == f.Branch && x.Export && x.Count == 1));
            await RegulatorFixtureCommands.Revoke(db, companyGrant);
        });
        await Expect(reader.GetAsync(Path(companyGrant)), HttpStatusCode.NotFound);
        Assert.Equal(2, (await Json(reader, grants)).GetProperty("items").GetArrayLength());
        await Db(db => db.Users.Where(x => x.Identity.Email == email)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Kind, IdentityKind.Customer)));
        await Expect(reader.GetAsync(Path(nationwideGrant)), HttpStatusCode.Unauthorized);
    }

    [PostgresFact]
    public async Task ProductionDisablesRegulatorPortalEvenWithFixtureFlag()
    {
        using var app = RegulatorHost("Production");
        using var production = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.False((await Json(production, "/api/auth/bir/availability")).GetProperty("enabled").GetBoolean());
        await Expect(production.PostAsync("/api/auth/bir/login", null), HttpStatusCode.Forbidden);
        await Expect(production.GetAsync("/api/bir/grants"), HttpStatusCode.NotFound);
    }
}
