using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    private const string VendorConnectPath = "/api/vendor/connectors";

    [PostgresFact]
    public async Task ConnectorImportsAreScopedSanitizedAndPublishedVersionsAreImmutable()
    {
        using var editor = await ConnectorVendor(ConnectorRules.View, ConnectorRules.Edit);
        using var publisher = await ConnectorVendor(ConnectorRules.VendorGrants);
        using var customer = await BillingBuyer();
        await Expect(customer.GetAsync(VendorConnectPath), HttpStatusCode.Forbidden);
        var created = await CreateConnector(editor);
        var id = created.GetProperty("connectorId").GetGuid();
        var path = VendorConnectPath + "/" + id + "/versions/" + created.GetProperty("id").GetGuid();
        var imported = await UploadSpec(editor, path, 1, "valid.json", """
            {"openapi":"3.1.0","info":{"title":"Development order API","version":"1"},
            "servers":[{"url":"https://api.example.com"}],"components":{"schemas":{"Order":{"type":"object","required":["id"],"properties":{"id":{"type":"string","example":"fixture-secret"}}}}},
            "paths":{"/orders":{"get":{"responses":{"200":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Order"}}}}}}}}}
            """);
        var review = imported.GetProperty("specification");
        Assert.Equal("/orders", review.GetProperty("endpoints")[0].GetProperty("path").GetString());
        Assert.Contains("required", review.GetRawText());
        Assert.DoesNotContain("fixture-secret", imported.GetRawText());
        var yaml = """
            openapi: 3.0.3
            info:
              title: Read only test
              version: '1.0'
            paths:
              /accounts:
                get:
                  responses:
                    '200':
                      description: Success
            """;
        var fromYaml = await UploadSpec(editor, path, 2, "valid.yaml", yaml);
        Assert.Equal("/accounts", fromYaml.GetProperty("specification").GetProperty("endpoints")[0].GetProperty("path").GetString());
        var postman = """
            {"info":{"name":"Development collection","schema":"https://schema.getpostman.com/json/collection/v2.1.0/collection.json"},
            "event":[{"listen":"prerequest","script":{"exec":["throw new Error('event-must-not-run')"]}}],
            "item":[{"name":"Read","request":{"method":"GET","url":{"raw":"https://api.example.com/orders?token=never-return",
            "query":[{"key":"token","value":"never-return"}]},"header":[{"key":"Authorization","value":"Bearer outside-secret"}]}}]}
            """;
        var fromPostman = await UploadSpec(editor, path, 3, "postman.json", postman);
        var normalized = fromPostman.GetRawText();
        Assert.Contains("Scripts and event handlers", normalized);
        Assert.Contains("Authorization", normalized);
        Assert.DoesNotContain("event-must-not-run", normalized);
        Assert.DoesNotContain("outside-secret", normalized);
        Assert.DoesNotContain("never-return", normalized);
        await Expect(UploadSpecResponse(editor, path, 4, "external.json", """
            {"openapi":"3.0.3","info":{"title":"References","version":"1"},"paths":{"/orders":{"$ref":"https://docs.example.com/path.json"}}}
            """), HttpStatusCode.BadRequest);
        await Expect(UploadSpecResponse(editor, path, 4, "bad.yaml", "openapi: ["), HttpStatusCode.BadRequest);
        await Expect(UploadSpecResponse(editor, path, 4, "aliases.yaml", "openapi: &tag 3.0.3\ninfo: *tag"), HttpStatusCode.BadRequest);
        await Expect(UploadSpecResponse(editor, path, 4, "unsafe.json", """
            {"openapi":"3.0.3","info":{"title":"Server","version":"1"},"servers":[{"url":"https://service.internal"}],"paths":{}}
            """), HttpStatusCode.BadRequest);
        var validated = await RequestJson(editor, path + "/validate", new { expectedRevision = 4 });
        Assert.True(validated.GetProperty("validation").GetProperty("valid").GetBoolean());
        await Expect(Mutate(editor, path + "/approve", new { expectedRevision = 5 }), HttpStatusCode.Forbidden);
        await Expect(Mutate(publisher, path + "/publish", new { expectedRevision = 5 }), HttpStatusCode.Conflict);
        await RequestJson(publisher, path + "/approve", new { expectedRevision = 5 });
        var published = await RequestJson(publisher, path + "/publish", new { expectedRevision = 6 });
        Assert.Equal("Published", published.GetProperty("state").GetString());
        await Expect(Mutate(publisher, path, Draft(), HttpMethod.Put), HttpStatusCode.Conflict);
        var copy = await RequestJson(publisher, path + "/copy", new { });
        Assert.Equal(2, copy.GetProperty("number").GetInt32());
        Assert.Equal("Draft Configuration", copy.GetProperty("state").GetString());
        await Db(async db =>
        {
            var versionId = published.GetProperty("id").GetGuid();
            var forbidden = await Assert.ThrowsAsync<PostgresException>(() => db.ConnectorVersions.Where(x => x.Id == versionId)
                .ExecuteUpdateAsync(v => v.SetProperty(x => x.Adapter, "review-only")));
            Assert.Equal("P0001", forbidden.SqlState);
        });

        var f = await PaidCompany(customer, "REGISTRY");
        var catalog = await Json(customer, ConnectPath(f) + "/catalog");
        Assert.Contains(catalog.GetProperty("items").EnumerateArray(), x => x.GetProperty("code").GetString() == "myob-business" && !x.GetProperty("canConnect").GetBoolean());
        Assert.Contains(catalog.GetProperty("items").EnumerateArray(), x => x.GetProperty("versionId").GetGuid() == published.GetProperty("id").GetGuid());
        var custom = published.GetProperty("code").GetString();
        var link = await BindFixture(customer, f, custom!, "sample-company-ceb");
        await PrepareFixture(customer, f, link);
        await Expect(customer.GetAsync(ConnectPath(f) + "/connections/" + link + "/sales-orders"), HttpStatusCode.NotFound);

        var copyPath = VendorConnectPath + "/" + id + "/versions/" + copy.GetProperty("id").GetGuid();
        await RequestJson(editor, copyPath + "/validate", new { expectedRevision = 1 });
        await RequestJson(publisher, copyPath + "/approve", new { expectedRevision = 2 });
        await RequestJson(publisher, copyPath + "/publish", new { expectedRevision = 3 });
        Assert.Equal(2, (await Json(customer, ConnectPath(f) + "/catalog/" + custom)).GetProperty("version").GetInt32());
        Assert.Equal(published.GetProperty("id").GetGuid(), (await Json(customer, ConnectPath(f) + "/connections")).GetProperty("items")[0].GetProperty("connectorVersionId").GetGuid());
        await Expect(Mutate(publisher, VendorConnectPath + "/" + id + "/lifecycle", new { state = "Deprecated" }), HttpStatusCode.NoContent);
        var deprecated = await Json(customer, ConnectPath(f) + "/catalog/" + custom);
        Assert.Equal("Deprecated", deprecated.GetProperty("availability").GetString());
        Assert.False(deprecated.GetProperty("canConnect").GetBoolean());
        Assert.Equal("Connected", (await Json(customer, ConnectPath(f) + "/connections")).GetProperty("items")[0].GetProperty("status").GetString());
        await Expect(Mutate(publisher, VendorConnectPath + "/" + id + "/disable", new { }), HttpStatusCode.NoContent);
        Assert.Equal("Suspended", (await Json(customer, ConnectPath(f) + "/connections")).GetProperty("items")[0].GetProperty("status").GetString());
        await Expect(Mutate(publisher, VendorConnectPath + "/" + id + "/lifecycle", new { state = "Retired" }), HttpStatusCode.NoContent);
        Assert.Equal("Retired", (await Json(customer, ConnectPath(f) + "/catalog/" + custom)).GetProperty("availability").GetString());
    }

    [PostgresFact]
    public async Task MockConnectionsEnforceEntitlementsAndInvoicingUsesOnlyExistingCommitment()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "CONNECT");
        var before = await Balance(owner, f.Subscriber);
        var path = ConnectPath(f);
        var accounting = await BindFixture(owner, f, "development-accounting", "sample-company-mnl");
        var commerce = await BindFixture(owner, f, "development-commerce", "sample-store-mnl");
        await Expect(Mutate(owner, path + "/connections/" + accounting + "/activate", new { }), HttpStatusCode.Conflict);
        var timeout = await RequestJson(owner, path + "/connections/" + accounting + "/test", new { scenario = "timeout" });
        Assert.Equal("TimedOut", timeout.GetProperty("lastError").GetString());
        await PrepareFixture(owner, f, accounting);
        await PrepareFixture(owner, f, commerce);
        await Expect(owner.GetAsync(path + "/connections/" + commerce + "/master-data/TaxCodeRead"), HttpStatusCode.NotFound);
        var orders = await Json(owner, path + "/connections/" + accounting + "/sales-orders?q=1001");
        var retrieved = Assert.Single(orders.GetProperty("items").EnumerateArray());
        var sourceKey = retrieved.GetProperty("key").GetString();
        Assert.Equal("EXT-SO-MNL-1001", retrieved.GetProperty("order").GetProperty("reference").GetString());
        var external = await Json(owner, f.Path + "/external-order/" + sourceKey);
        Assert.Equal(f.Branch, external.GetProperty("branchId").GetGuid());
        var first = await RequestJson(owner, path + "/connections/" + commerce + "/import-orders", new { });
        var duplicate = await RequestJson(owner, path + "/connections/" + commerce + "/import-orders", new { });
        Assert.Equal(3, first.GetProperty("imported").GetInt32());
        Assert.Equal(0, duplicate.GetProperty("imported").GetInt32());
        Assert.Equal(3, duplicate.GetProperty("alreadyPresent").GetInt32());
        var sourceRows = (await Json(owner, path + "/source-orders")).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, sourceRows.Length);
        Assert.Null(sourceRows.Single(x => x.GetProperty("status").GetString() == "Cancelled").GetProperty("key").GetString());
        var eligible = sourceRows.Single(x => x.GetProperty("status").GetString() == "Paid");
        Assert.Equal("25", eligible.GetProperty("order").GetProperty("shipping").GetRawText());
        var shopKey = eligible.GetProperty("key").GetString();
        var shopSource = await Json(owner, f.Path + "/external-order/" + shopKey);
        Assert.Equal("5", shopSource.GetProperty("lines")[0].GetProperty("remainingQuantity").GetString());
        Assert.Equal(before, await Balance(owner, f.Subscriber));
        Assert.Empty((await Json(owner, f.Path + "/SalesInvoice/register")).GetProperty("items").EnumerateArray());
        var input = Input(f, "SalesInvoice") with { ExternalOrderKey = shopKey,
            Lines = [new(f.Item, f.Unit, "5", null, null, null, null, ExternalLineKey: "001")] };
        var confirmed = await DocPreview(owner, f, "SalesInvoice", input);
        Assert.Equal(before, await Balance(owner, f.Subscriber));
        var posted = await CommitDocument(owner, f, "SalesInvoice", input, confirmed);
        Assert.Equal(before - 1, await Balance(owner, f.Subscriber));
        await Expect(Mutate(owner, f.Path + "/SalesInvoice/commit", Operation(input, confirmed)), HttpStatusCode.BadRequest);
        var detail = await Json(owner, path + "/source-orders/" + eligible.GetProperty("id").GetGuid());
        Assert.Equal(posted.GetProperty("id").GetGuid(), detail.GetProperty("relatedInvoices")[0].GetProperty("id").GetGuid());

        var company = await CreateCompany(owner, f.Subscriber, "CONNECT-OTHER");
        var otherPath = $"/api/workspace/{f.Subscriber}/companies/{company}/connect";
        var grant = await RequestJson(owner, path + "/catalog/development-accounting/authorize", new { });
        var binding = new { versionId = grant.GetProperty("versionId").GetGuid(), proof = grant.GetProperty("proof").GetString(),
            resourceId = "sample-company-mnl", name = "Duplicate" };
        await Expect(Mutate(owner, path + "/connections", binding), HttpStatusCode.Conflict);
        await Expect(Mutate(owner, otherPath + "/connections", binding), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, otherPath + "/connections/" + accounting + "/test", new { }), HttpStatusCode.NotFound);
        var bridgeInput = new { publicKeyFingerprint = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            agentVersion = "0.1", installedAdapters = new[] { new { code = "qbd", version = "0.1" } } };
        var bridgePath = path + "/bridge/registrations";
        using var registered = await Mutate(owner, bridgePath, bridgeInput);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var agent = await registered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending Agent Approval", agent.GetProperty("status").GetString());
        Assert.False((await Json(owner, bridgePath)).GetProperty("runtimeAvailable").GetBoolean());
        await Expect(Mutate(owner, bridgePath, bridgeInput), HttpStatusCode.Conflict);
        var entry = "/bridge/registrations/" + agent.GetProperty("id").GetGuid() + "/revoke";
        await Expect(Mutate(owner, otherPath + entry, new { }), HttpStatusCode.NotFound);
        Assert.Equal("Revoked", (await RequestJson(owner, path + entry, new { })).GetProperty("status").GetString());
        var (outsider, _, email) = await SeedSubscriber();
        using var stranger = Browser();
        await Login(stranger, email, _password);
        await Expect(stranger.GetAsync(path + "/connections"), HttpStatusCode.NotFound);
        var outsiderCompany = await CreateCompany(stranger, outsider, "FOREIGN-C");
        await Expect(stranger.GetAsync($"/api/workspace/{outsider}/companies/{outsiderCompany}/connect/source-orders/" + eligible.GetProperty("id").GetGuid()), HttpStatusCode.NotFound);

        using var production = Host("Production");
        using var prod = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await Expect(Mutate(prod, path + "/catalog/development-accounting/authorize", new { }), HttpStatusCode.NotFound);
        await Expect(prod.GetAsync(VendorConnectPath), HttpStatusCode.NotFound);
        var record = f.Path + "/SalesInvoice/" + posted.GetProperty("id").GetGuid();
        await WaitForDocument(owner, record, row => row.GetProperty("electronicDelivery").GetString() == "AcceptedByProvider");
    }

    [Fact]
    public void SwaggerAndEfiscalFormatsNormalizeManualOperationMetadata()
    {
        var swagger = SpecImport.Parse(Encoding.UTF8.GetBytes("""
            {"swagger":"2.0","info":{"title":"Swagger sample","version":"1"},"schemes":["https"],"host":"api.example.com",
            "paths":{"/orders":{"post":{"parameters":[{"name":"order","in":"body","schema":{"type":"object"}}],"responses":{"200":{"description":"ok"}}}}}}
            """), false);
        Assert.Equal("Swagger 2.0", swagger.Format);
        Assert.NotNull(Assert.Single(swagger.Endpoints).Request);
        var config = SpecImport.Parse(Encoding.UTF8.GetBytes("""
            {"efiscalConnector":1,"servers":["https://api.example.com"],"authentication":["Bearer Token"],
            "endpoints":[{"method":"GET","path":"/orders","response":{"type":"array","items":{"type":"string"}}}]}
            """), false);
        Assert.Equal("Bearer Token", Assert.Single(config.Authentication));
        Assert.Equal("array", Assert.Single(config.Endpoints).Response!.Value.GetProperty("type").GetString());
    }

    private static string ConnectPath(DocumentFixture f) =>
        $"/api/workspace/{f.Subscriber}/companies/{f.Company}/connect";

    private async Task<HttpClient> ConnectorVendor(params string[] grants)
    {
        var email = Email("connector-admin");
        await Db(async db =>
        {
            var user = NewIdentity(email);
            user.Kind = IdentityKind.Vendor;
            user.DevelopmentFixture = true;
            db.Users.Add(user);
            db.AddRange(grants.Select(permission => new VendorConnectorGrant { UserId = user.Id, Permission = permission }));
            await db.SaveChangesAsync();
        });
        var vendor = Browser();
        await Expect(Mutate(vendor, "/api/auth/vendor/login", new { email, password = _password }), HttpStatusCode.NoContent);
        return vendor;
    }

    private static ConnectorDraft Draft() => new("review-" + Guid.NewGuid().ToString("N"), "Accounting Software", "mock-accounting",
        new("Verified development fixture", "Synthetic registry and specification review", "Configurable REST API", "1", [], ["PH"], [], [],
            ["Synthetic Authorization"], "Additional", "DV", [new("DiscoverCompanyFiles", "Development Verified", null)], [], []), 7);

    private static async Task<JsonElement> CreateConnector(HttpClient vendor)
    {
        using var response = await Mutate(vendor, VendorConnectPath, Draft());
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> UploadSpecResponse(HttpClient vendor, string path, int revision, string file, string spec)
    {
        var token = (await Json(vendor, "/api/auth/csrf")).GetProperty("token").GetString();
        using var data = new MultipartFormDataContent();
        data.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(spec)), "file", file);
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "/import?expectedRevision=" + revision) { Content = data };
        request.Headers.Add("X-EFiscal-CSRF", token);
        return await vendor.SendAsync(request);
    }

    private static async Task<JsonElement> UploadSpec(HttpClient vendor, string path, int revision, string file, string spec)
    {
        using var response = await UploadSpecResponse(vendor, path, revision, file, spec);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> BindFixture(HttpClient owner, DocumentFixture f, string code, string resource)
    {
        var path = ConnectPath(f);
        var authorized = await RequestJson(owner, path + "/catalog/" + code + "/authorize", new { });
        Assert.Contains(authorized.GetProperty("account").GetProperty("resources").EnumerateArray(), x => x.GetProperty("id").GetString() == resource);
        using var response = await Mutate(owner, path + "/connections", new { versionId = authorized.GetProperty("versionId").GetGuid(),
            proof = authorized.GetProperty("proof").GetString(), resourceId = resource, name = "Bound " + resource, branchId = f.Branch });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain("credential", result.GetRawText(), StringComparison.OrdinalIgnoreCase);
        return result.GetProperty("id").GetGuid();
    }

    private static async Task PrepareFixture(HttpClient owner, DocumentFixture f, Guid id)
    {
        var path = ConnectPath(f) + "/connections/" + id;
        await RequestJson(owner, path + "/test", new { scenario = "success" });
        await Expect(Mutate(owner, path + "/mapping", new { customerId = f.Customer, itemId = f.Item, unitId = f.Unit, expectedVersion = 0 }, HttpMethod.Put), HttpStatusCode.OK);
        var activated = await RequestJson(owner, path + "/activate", new { });
        Assert.Equal("Connected", activated.GetProperty("status").GetString());
    }
}
