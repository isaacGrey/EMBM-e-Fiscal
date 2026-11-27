using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task CustomerMasterRecordsAreVersionedAndScopedByCompanyAndPermission()
    {
        var (tenant, _, email) = await SeedSubscriber();
        using var owner = Browser();
        await Login(owner, email, _password);
        var first = await CreateCompany(owner, tenant, "MASTER-A");
        var second = await CreateCompany(owner, tenant, "MASTER-B");
        var a = MasterPath(tenant, first);
        var b = MasterPath(tenant, second);
        var external = new[] { new { connector = "QBO", externalAccount = "fixture-realm", recordType = "customers", externalId = "id-11" } };
        var data = new { classification = "Individual", external, billingAddress = "Sample, address", invoiceRecipients = new[] { "second@example.test" } };
        var customer = await MasterCreate(owner, a, "customers", "c-1", "Sample consumer", data);
        Assert.Equal("C-1", customer.GetProperty("code").GetString());
        var id = customer.GetProperty("id").GetGuid();
        await Expect(Mutate(owner, a + "/customers", Record("C-1", "Duplicate", data)), HttpStatusCode.BadRequest);
        await Expect(Mutate(owner, a + "/customers", Record("C-2", "Duplicate reference", data)), HttpStatusCode.BadRequest);
        await Expect(Mutate(owner, a + "/customers", Record("C-3", "Bad RDO", new { classification = "Individual", rdo = "INVALID" })), HttpStatusCode.BadRequest);
        await Expect(Mutate(owner, a + "/customers", Record("C-4", "Bad TIN", new { classification = "Corporate", tin = "bad" })), HttpStatusCode.BadRequest);
        await MasterCreate(owner, b, "customers", "C-1", "Second company consumer", data);
        await Expect(owner.GetAsync(b + "/customers/" + id), HttpStatusCode.NotFound);
        var term = await MasterCreate(owner, b, "payment-terms", "NET", "Net 15", new { days = "15", method = "CalendarDays" });
        await Expect(Mutate(owner, a + "/customers", Record("C-5", "Foreign linkage", new { classification = "Individual",
            paymentTerm = term.GetProperty("id").GetGuid() })), HttpStatusCode.BadRequest);

        await Expect(Mutate(owner, a + "/customers/" + id, new { code = "C-1", name = "Inactive consumer", active = false, revision = 1, data }, HttpMethod.Put), HttpStatusCode.OK);
        await Expect(Mutate(owner, a + "/customers/" + id, new { code = "C-1", name = "Stale edit", active = true, revision = 1, data }, HttpMethod.Put), HttpStatusCode.Conflict);
        var detail = await Json(owner, a + "/customers/" + id);
        Assert.Equal(2, detail.GetProperty("history").GetArrayLength());
        Assert.Equal("Sample consumer", detail.GetProperty("history")[1].GetProperty("name").GetString());
        Assert.Single((await Json(owner, a + "/customers?q=inactive&active=false&filter=Individual&sort=name")).GetProperty("items").EnumerateArray());
        Assert.Empty((await Json(owner, a + "/customers?active=true")).GetProperty("items").EnumerateArray());
        await Db(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.MasterRevisions.Where(x => x.RecordId == id)
                .ExecuteUpdateAsync(change => change.SetProperty(x => x.Name, "Forbidden rewrite")));
            Assert.Equal("P0001", error.SqlState);
        });

        var memberEmail = Email("master-encoder");
        await Db(async db =>
        {
            var encoder = NewIdentity(memberEmail);
            db.AddRange(encoder, new SubscriberMembership { SubscriberId = tenant, UserId = encoder.Id, RoleId = "encoder", CreatedAt = DateTimeOffset.UtcNow },
                new CompanyMembership { SubscriberId = tenant, CompanyId = first, UserId = encoder.Id });
            await db.SaveChangesAsync();
        });
        using var member = Browser();
        await Login(member, memberEmail, _password);
        await Expect(member.GetAsync(a + "/customers"), HttpStatusCode.OK);
        await Expect(member.GetAsync(b + "/customers"), HttpStatusCode.NotFound);
        await Expect(Mutate(member, a + "/tax-groups", Record("VA", "Domestic VAT", Vat("Exclusive", "2026-01-01"))), HttpStatusCode.NotFound);
        await Expect(member.GetAsync(a + "/customers/file?format=csv"), HttpStatusCode.NotFound);

        var (foreignTenant, _, foreignEmail) = await SeedSubscriber();
        using var outsider = Browser();
        await Login(outsider, foreignEmail, _password);
        await Expect(outsider.GetAsync(a + "/customers"), HttpStatusCode.NotFound);
        await Expect(owner.GetAsync(MasterPath(foreignTenant, first) + "/customers"), HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task CsvAndExcelImportRequireTheExactPreviewAndExportSafely()
    {
        var (tenant, _, email) = await SeedSubscriber();
        using var owner = Browser();
        await Login(owner, email, _password);
        var company = await CreateCompany(owner, tenant, "IMPORT-A");
        var second = await CreateCompany(owner, tenant, "IMPORT-B");
        var path = MasterPath(tenant, company);
        var other = MasterPath(tenant, second);
        await MasterCreate(owner, path, "units", "EA", "Each", new { symbol = "ea" });

        var data = Encoding.UTF8.GetBytes("code,name,classification,invoiceRecipients\r\nC1,\"Name, Consumer\",Individual,second@example.test;third@example.test\r\n");
        var preview = await Upload(owner, path + "/customers/import/preview", "customers.csv", data);
        var ticket = preview.GetProperty("previewToken").GetString();
        Assert.NotNull(ticket);
        Assert.Empty((await Json(owner, path + "/customers")).GetProperty("items").EnumerateArray());
        using (var crossed = await UploadResponse(owner, other + "/customers/import/confirm", "customers.csv", data, ticket))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        using (var changed = await UploadResponse(owner, path + "/customers/import/confirm", "customers.csv", [..data, 10], ticket))
            Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        Assert.Equal(1, (await Upload(owner, path + "/customers/import/confirm", "customers.csv", data, ticket)).GetProperty("imported").GetInt32());
        Assert.True((await Upload(owner, path + "/customers/import/confirm", "customers.csv", data, ticket)).GetProperty("replay").GetBoolean());

        var invalid = Encoding.UTF8.GetBytes("code,name,classification\r\nC1,Already exists,Individual\r\nC2,Wrong,Unknown\r\n");
        var rejected = await Upload(owner, path + "/customers/import/preview", "customers.csv", invalid);
        Assert.Equal(2, rejected.GetProperty("errors").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("previewToken").ValueKind);
        using var errorFile = await UploadResponse(owner, path + "/customers/import/errors", "customers.csv", invalid);
        Assert.Contains("A record already uses this code", await errorFile.Content.ReadAsStringAsync());

        var excel = MasterSpreadsheet.Write([["code", "name", "itemType", "unit", "price"],
            ["SV1", "Synthetic service", "Services", "EA", "123.4555"]], "xlsx");
        var reviewed = await Upload(owner, path + "/items/import/preview", "items.xlsx", excel);
        Assert.Equal(1, reviewed.GetProperty("valid").GetInt32());
        await Upload(owner, path + "/items/import/confirm", "items.xlsx", excel, reviewed.GetProperty("previewToken").GetString());
        var item = Assert.Single((await Json(owner, path + "/items")).GetProperty("items").EnumerateArray());
        Assert.Equal("123.4555", item.GetProperty("data").GetProperty("price").GetString());

        using var csv = await owner.GetAsync(path + "/items/file?format=csv");
        Assert.Contains("\"123.4555\"", await csv.Content.ReadAsStringAsync());
        using var xlsx = await owner.GetAsync(path + "/items/file?format=xlsx");
        Assert.Contains("123.4555", MasterSpreadsheet.Read(await xlsx.Content.ReadAsByteArrayAsync(), "xlsx")[1]);
        using var template = await owner.GetAsync(path + "/customers/file?format=xlsx&template=true");
        Assert.Contains("classification", Assert.Single(MasterSpreadsheet.Read(await template.Content.ReadAsByteArrayAsync(), "xlsx")));

        await MasterCreate(owner, path, "customers", "SAFE", "=1+1", new { classification = "Individual" });
        using var export = await owner.GetAsync(path + "/customers/file?format=csv");
        Assert.Contains("\"'=1+1\"", await export.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task ValidatesCompanySpecificTypedCustomFieldsAndReferences()
    {
        var (tenant, _, email) = await SeedSubscriber();
        using var owner = Browser();
        await Login(owner, email, _password);
        var path = MasterPath(tenant, await CreateCompany(owner, tenant, "CUSTOM"));
        var other = MasterPath(tenant, await CreateCompany(owner, tenant, "CUSTOM-OTHER"));
        var localDepartment = await Add(owner, path, "dimensions", "LOCAL", new { dimensionType = "Department" });
        var foreignDepartment = await Add(owner, other, "dimensions", "FOREIGN", new { dimensionType = "Department" });
        await MasterCreate(owner, path, "custom-fields", "PRIORITY", "Priority", new
        {
            recordTypes = new[] { "customers" }, valueType = "Dropdown", required = true, displayOrder = "1",
            values = new[] { "normal", "high" }, @default = "normal"
        });
        await MasterCreate(owner, path, "custom-fields", "COEFFICIENT", "Internal factor", new
        {
            recordTypes = new[] { "customers" }, valueType = "Decimal", displayOrder = "2", minimum = "-0.001", maximum = "10"
        });
        await MasterCreate(owner, path, "custom-fields", "DEPARTMENT", "Department reference", new
        {
            recordTypes = new[] { "customers" }, valueType = "Reference", displayOrder = "3", referenceType = "dimensions"
        });
        var inputs = new Dictionary<string, object> { ["COEFFICIENT"] = "0.12345678", ["DEPARTMENT"] = localDepartment };
        var customer = await MasterCreate(owner, path, "customers", "TEST", "Typed values", new { classification = "Individual", custom = inputs });
        var custom = customer.GetProperty("data").GetProperty("custom");
        Assert.Equal("normal", custom.GetProperty("PRIORITY").GetString());
        Assert.Equal("0.12345678", custom.GetProperty("COEFFICIENT").GetString());
        inputs["DEPARTMENT"] = foreignDepartment;
        await Expect(Mutate(owner, path + "/customers", Record("FOREIGN", "Foreign value", new { classification = "Individual", custom = inputs })), HttpStatusCode.BadRequest);
        inputs["DEPARTMENT"] = localDepartment;
        inputs["PRIORITY"] = "unsupported";
        await Expect(Mutate(owner, path + "/customers", Record("BAD", "Invalid option", new { classification = "Individual", custom = inputs })), HttpStatusCode.BadRequest);
        await MasterCreate(owner, other, "customers", "OTHER", "Local rules only", new { classification = "Individual" });
    }

    private static string MasterPath(Guid subscriber, Guid company) => $"/api/workspace/{subscriber}/companies/{company}/master-data";
    private static object Record(string code, string name, object data) => new { code, name, active = true, data };
    private static async Task<JsonElement> MasterCreate(HttpClient browser, string path, string kind, string code, string name, object data)
    {
        using var response = await Mutate(browser, path + '/' + kind, Record(code, name, data));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> UploadResponse(HttpClient client, string path, string name, byte[] bytes, string? ticket = null)
    {
        var token = (await Json(client, "/api/auth/csrf")).GetProperty("token").GetString()!;
        using var data = new MultipartFormDataContent();
        data.Add(new ByteArrayContent(bytes), "file", name);
        if (ticket is not null) data.Add(new StringContent(ticket), "previewToken");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = data };
        request.Headers.Add("X-EFiscal-CSRF", token);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Upload(HttpClient client, string path, string name, byte[] bytes, string? ticket = null)
    {
        using var response = await UploadResponse(client, path, name, bytes, ticket);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Vat(string treatment, string effectiveFrom) => new
    {
        classification = "VATable", treatment, rounding = "HalfAway", effectiveFrom,
        components = new[] { new { kind = "VAT", basis = "NetSales", rate = "12" } },
        authority = RegulatoryCatalog.Vat, companyAuthorized = true
    };
}

