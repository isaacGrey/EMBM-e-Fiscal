using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [Fact]
    public void DeclarativeRulesPreserveProtectedAmountsAndBoundReferences()
    {
        var sample = FieldMapper.Sample("SalesInvoice", FieldMapper.Outbound);
        sample.Header["total"] = 224.12345678m;
        sample.Header["remarks"] = "  checked  ";
        sample.Lines[0]["taxCode"] = "committed-tax-group";
        sample.Lines[0]["discountCode"] = "committed-discount-group";
        var sourceItem = sample.Lines[0]["item"]!.ToString()!;
        var required = FieldMapper.Defaults(FieldMapper.Outbound);
        var alternating = required.Select(r => r.Target switch
        {
            "item" => r with { Operation = "reference" },
            "remarks" => r with { Operation = "trim" },
            "description" => r with { Operation = "if", Value = ConnectorRules.Freeze("Different"), Fallback = ConnectorRules.Freeze("Review") },
            _ => r
        }).ToArray();
        var input = new[] { new BoundReference("Item", sourceItem, "DEMO-SERVICE", "DEMO"),
            new("Tax", "committed-tax-group", "VAT12", "VAT12") };
        var output = FieldMapper.Map(FieldMapper.Outbound, alternating, sample, input);
        Assert.Equal(224.12345678m, output.Header["total"]);
        Assert.Equal("checked", output.Header["remarks"]);
        Assert.Equal("Review", output.Lines[0]["description"]);
        Assert.Equal("DEMO-SERVICE", output.Lines[0]["item"]);
        Assert.Equal("VAT12", output.Lines[0]["taxCode"]);
        Assert.False(output.Lines[0].ContainsKey("discountCode"));
        Assert.Contains(FieldMapper.Validate(FieldMapper.Outbound, required.Select(x => x.Target == "taxCode" ?
            x with { Operation = "constant", Sources = [], Value = ConnectorRules.Freeze("EXEMPT") } : x).ToArray()), x => x.Contains("protected"));
        Assert.Throws<ConnectorInputError>(() => FieldMapper.Map(FieldMapper.Outbound, alternating, sample, []));
        Assert.Contains(FieldMapper.Validate(FieldMapper.Outbound, required.Where(x => x.Target != "vat").ToArray()), x => x.Contains("Required"));
        Assert.Contains(FieldMapper.Validate(FieldMapper.Outbound, required.Select(x => x.Target == "total" ?
            x with { Operation = "constant", Sources = [], Value = ConnectorRules.Freeze(1) } : x).ToArray()), x => x.Contains("protected"));
        Assert.Contains(FieldMapper.Validate(FieldMapper.Outbound, required.Select(x => x.Target == "remarks" ?
            x with { Operation = "shell" } : x).ToArray()), x => x.Contains("Unsupported"));
        sample.Lines[0]["unitPrice"] = "abc";
        Assert.Throws<ConnectorInputError>(() => FieldMapper.Map(FieldMapper.Outbound, required, sample, []));
    }

    [Fact]
    public async Task ExecutionRejectsUnsafeOriginsParametersAndRedirectsWithoutEchoingCredentials()
    {
        var approved = new ExecutionPolicy { Origin = ControlledExecution.Origin, Method = "GET", Path = "/v1/orders/{id}", Authentication = "Bearer Token" };
        Assert.Equal("accounting-sandbox.efiscal.example", ControlledExecution.Destination(approved, new() { ["id"] = "AB_1" }, 1).Host);
        Assert.Throws<ConnectorInputError>(() => ControlledExecution.Destination(approved, new() { ["id"] = ".." }, 1));
        Assert.Throws<ConnectorInputError>(() => ControlledExecution.Destination(approved, new() { ["id"] = "AB" }, 40));
        foreach (var reservation in new[] { "127.0.0.1", "169.254.169.254", "10.0.0.1", "192.168.0.1", "100.64.0.1", "::1", "fc00::1", "::ffff:127.0.0.1" })
            Assert.False(ControlledExecution.PublicAddress(IPAddress.Parse(reservation)));
        var calls = 0;
        var identifier = Guid.NewGuid();
        var result = await ControlledExecution.Execute(approved, null, identifier, (request, _) =>
        {
            calls++;
            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal(identifier.ToString("N"), Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            return Task.FromResult(new SandboxResponse(302, ConnectorRules.Freeze(new { authorization = "must-not-return" })));
        }, default, new() { ["id"] = "AB" });
        Assert.Equal(1, calls);
        Assert.Equal("RedirectBlocked", result.Status);
        Assert.Null(result.Response);
        Assert.Contains("[redacted]", ConnectorRules.Freeze(ControlledExecution.Preview(approved, identifier)).GetRawText());
        approved.Origin = "https://private.example/";
        Assert.Throws<ConnectorInputError>(() => ControlledExecution.Destination(approved));
    }

    [PostgresFact]
    public async Task ScopedTemplateCopySandboxAndActivatedMappingAreAudited()
    {
        using var owner = await BillingBuyer();
        using var vendor = await ConnectorVendor(ConnectorRules.VendorGrants);
        var f = await PaidCompany(owner, "MAP8");
        var link = await BindFixture(owner, f, "development-accounting", "sample-company-mnl");
        await PrepareFixture(owner, f, link);
        var path = ConnectPath(f);
        var profile = await CreateOutbound(owner, f, link, "SalesInvoice");
        var baseline = await Balance(owner, f.Subscriber);
        var version = (await Json(owner, path + "/catalog/development-accounting")).GetProperty("versionId").GetGuid();
        var templatePath = VendorConnectPath + "/mapping-templates";
        using var published = await Mutate(vendor, templatePath, new { connectorVersionId = version, name = "Synthetic default", documentType = "SalesInvoice", direction = "Outbound" });
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var global = await published.Content.ReadFromJsonAsync<JsonElement>();
        var id = global.GetProperty("id").GetGuid();
        var copied = await RequestJson(owner, profile + "/versions", new { expectedVersion = 0, templateId = id });
        Assert.Equal("Validated", copied.GetProperty("status").GetString());
        await RequestJson(owner, profile + "/activate", new { version = 1 });
        var preview = await RequestJson(owner, profile + "/preview", new { });
        Assert.True(preview.GetProperty("valid").GetBoolean());
        Assert.Contains("[redacted]", preview.GetRawText());
        Assert.Equal(baseline, await Balance(owner, f.Subscriber));
        var foreign = await CreateCompany(owner, f.Subscriber, "MAP8-OTHER");
        await Expect(owner.GetAsync($"/api/workspace/{f.Subscriber}/companies/{foreign}/connect/mapping-profiles/" + profile.Split('/').Last()),
            HttpStatusCode.NotFound);
        var mapping = Guid.Parse(profile.Split('/').Last());
        await Db(async db =>
        {
            var frozen = await Assert.ThrowsAsync<PostgresException>(() => db.MappingVersions.Where(x => x.ProfileId == mapping)
                .ExecuteUpdateAsync(change => change.SetProperty(x => x.Validated, false)));
            Assert.Equal("P0001", frozen.SqlState);
        });

        var endpoint = new Guid("f25ca108-0000-4000-8002-000000000004");
        var runtime = VendorConnectPath + "/runtime/test";
        await Expect(Mutate(vendor, runtime, new { sandbox = "accounting-local", policyId = Guid.NewGuid() }), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, runtime, new { sandbox = "accounting-local", policyId = endpoint }), HttpStatusCode.Forbidden);
        var tested = await RequestJson(vendor, runtime, new { sandbox = "accounting-local", policyId = endpoint, scenario = "Normal" });
        Assert.Equal("Confirmed", tested.GetProperty("result").GetProperty("status").GetString());
        Assert.Contains("[redacted]", tested.GetRawText());
        var invalid = await RequestJson(vendor, runtime, new { sandbox = "accounting-local", policyId = endpoint, scenario = "InvalidResponse" });
        Assert.Equal("InvalidResponse", invalid.GetProperty("result").GetProperty("status").GetString());
    }

    [PostgresFact]
    public async Task CommittedOrdersInvoicesAndMemosSyncAndUncertainWritesAreVerifiedWithoutDuplicates()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "SYNC8");
        var link = await BindFixture(owner, f, "development-accounting", "sample-company-mnl");
        await PrepareFixture(owner, f, link);
        var path = ConnectPath(f);
        var start = await Balance(owner, f.Subscriber);
        using var responseProfile = await Mutate(owner, path + "/mapping-profiles", new
        {
            connectionId = link, name = "Invoice readback", documentType = "SalesInvoice", direction = "Response"
        });
        Assert.Equal(HttpStatusCode.Created, responseProfile.StatusCode);
        var readback = path + "/mapping-profiles/" + (await responseProfile.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await RequestJson(owner, readback + "/versions", new { expectedVersion = 0, rules = FieldMapper.Defaults(FieldMapper.Response) });
        await RequestJson(owner, readback + "/activate", new { version = 1 });
        await ActivateDestination(owner, f, link, "SalesOrder", "Transient");
        await ActivateDestination(owner, f, link, "SalesInvoice", "TimeoutAfterWrite");
        await ActivateDestination(owner, f, link, "CreditMemo", "UncertainAfterWrite");
        var order = Input(f, "SalesOrder", "2");
        var saved = await CommitDocument(owner, f, "SalesOrder", order, await DocPreview(owner, f, "SalesOrder", order));
        var orderId = saved.GetProperty("id").GetGuid();
        Assert.Equal(start, await Balance(owner, f.Subscriber));
        var sourceLines = await Json(owner, f.Path + "/SalesOrder/" + orderId + "/source-lines");
        var orderLine = sourceLines.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var invoice = Input(f, "SalesInvoice", "2") with { SourceDocumentId = orderId,
            Lines = [new(f.Item, f.Unit, "2", null, null, null, null, SourceLineId: orderLine)] };
        var issued = await CommitDocument(owner, f, "SalesInvoice", invoice, await DocPreview(owner, f, "SalesInvoice", invoice));
        var invoiceId = issued.GetProperty("id").GetGuid();
        var orderStatus = await WaitForDocument(owner, path + "/accounting/jobs/" + orderId, x => x.GetProperty("status").GetString() == "RetryScheduled");
        Assert.Equal("RateLimited", orderStatus.GetProperty("attempts")[0].GetProperty("category").GetString());
        await Db(async db => await db.DocumentOutboxMessages.Where(x => x.Kind == "Accounting" && x.DocumentId == orderId && x.State == "RetryScheduled")
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.State, "Processing").SetProperty(t => t.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1))));
        var services = _app.Services.GetRequiredService<IServiceScopeFactory>();
        using var recovery = new AccountingWorker(services, NullLogger<AccountingWorker>.Instance);
        await Task.WhenAll(recovery.ProcessOne(default), recovery.ProcessOne(default));
        var orderResult = await WaitForDocument(owner, path + "/accounting/jobs/" + orderId, x => x.GetProperty("status").GetString() == "Synchronized");
        var invoiceResult = await WaitForDocument(owner, path + "/accounting/jobs/" + invoiceId, x => x.GetProperty("status").GetString() == "Synchronized");
        Assert.Equal("ACCT-SI-000001", invoiceResult.GetProperty("identity").GetProperty("externalNumber").GetString());
        Assert.Equal(start - 1, await Balance(owner, f.Subscriber));
        var invLines = await Json(owner, f.Path + "/SalesInvoice/" + invoiceId + "/source-lines");
        var originalLine = invLines.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var memo = Input(f, "CreditMemo") with { CustomerId = null, SourceDocumentId = invoiceId, AdjustmentReason = "Partial return", AdjustmentKind = "Return",
            Lines = [new(f.Item, f.Unit, "1", null, null, null, null, SourceLineId: originalLine)] };
        var committedMemo = await CommitDocument(owner, f, "CreditMemo", memo, await DocPreview(owner, f, "CreditMemo", memo));
        var memoId = committedMemo.GetProperty("id").GetGuid();
        var target = path + "/accounting/jobs/" + memoId;
        var uncertain = await WaitForDocument(owner, target, x => x.GetProperty("status").GetString() == "UncertainExternalOutcome");
        Assert.True(uncertain.GetProperty("canVerify").GetBoolean());
        Assert.False(uncertain.GetProperty("canRetry").GetBoolean());
        await Expect(Mutate(owner, target + "/retry", new { mode = "retry" }), HttpStatusCode.Conflict);
        await Expect(Mutate(owner, target + "/retry", new { mode = "verify" }), HttpStatusCode.Accepted);
        var verified = await WaitForDocument(owner, target, x => x.GetProperty("status").GetString() == "Synchronized");
        Assert.Equal("ACCT-CM-000001", verified.GetProperty("identity").GetProperty("externalNumber").GetString());
        Assert.Equal(start - 2, await Balance(owner, f.Subscriber));
        await Db(async db =>
        {
            var receipts = await db.MockAccountingRecords.Where(x => x.SubscriberId == f.Subscriber).ToArrayAsync();
            Assert.Equal(3, receipts.Length);
            Assert.Equal(orderResult.GetProperty("identity").GetProperty("externalId").GetString(), receipts.Single(x => x.DocumentId == invoiceId).Relationship);
            Assert.Equal(224m, (await db.FiscalDocuments.AsNoTracking().SingleAsync(x => x.Id == invoiceId)).Total);
            Assert.Equal(1, await db.SyncOperations.Where(x => x.DocumentId == invoiceId).Select(x => x.ResponseMappingNumber).SingleAsync());
        });
        await WaitForDocument(owner, f.Path + "/SalesInvoice/" + invoiceId, x => x.GetProperty("electronicDelivery").GetString() == "AcceptedByProvider");
        var outbound = (await Json(owner, path + "/mapping-profiles")).GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == "SalesInvoice default").GetProperty("id").GetGuid();
        await Expect(Mutate(owner, path + "/accounting/destinations", new { profileId = outbound, branchId = f.Branch,
            documentType = "SalesInvoice", enabled = true, automatic = true, scenario = "Variance" }, HttpMethod.Put), HttpStatusCode.OK);
        var direct = Input(f, "SalesInvoice");
        var posted = await CommitDocument(owner, f, "SalesInvoice", direct, await DocPreview(owner, f, "SalesInvoice", direct));
        var varianceId = posted.GetProperty("id").GetGuid();
        var variance = await WaitForDocument(owner, path + "/accounting/jobs/" + varianceId, x => x.GetProperty("status").GetString() == "RequiresReview");
        Assert.Equal("ExternalFinancialVariance", variance.GetProperty("attempts")[0].GetProperty("category").GetString());
        Assert.Equal("112", (await Json(owner, f.Path + "/SalesInvoice/" + varianceId)).GetProperty("totals").GetProperty("total").GetString());
        Assert.Equal(start - 3, await Balance(owner, f.Subscriber));
        await WaitForDocument(owner, f.Path + "/SalesInvoice/" + varianceId, x => x.GetProperty("electronicDelivery").GetString() == "AcceptedByProvider");
    }

    [PostgresFact]
    public async Task StorePaginationAndRefundObservationPreserveOriginalIdentityWithoutIssuance()
    {
        using var owner = await BillingBuyer();
        var f = await PaidCompany(owner, "SOURCE8");
        var id = await BindFixture(owner, f, "development-commerce", "sample-store-mnl");
        await PrepareFixture(owner, f, id);
        var path = ConnectPath(f);
        var connection = path + "/connections/" + id;
        var balance = await Balance(owner, f.Subscriber);
        var first = await RequestJson(owner, connection + "/import-orders?limit=2", new { });
        Assert.Equal(2, first.GetProperty("imported").GetInt32());
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await RequestJson(owner, connection + "/import-orders?limit=2&after=" + cursor, new { });
        Assert.Equal(1, second.GetProperty("imported").GetInt32());
        await Expect(Mutate(owner, connection + "/references", new { kind = "Customer", localKey = f.Customer.ToString(), externalId = "DEMO-BUYER" },
            HttpMethod.Put), HttpStatusCode.OK);
        await Expect(Mutate(owner, connection + "/references", new { kind = "Item", localKey = f.Item.ToString(), externalId = "DEMO-SERVICE" },
            HttpMethod.Put), HttpStatusCode.OK);
        using var profile = await Mutate(owner, path + "/mapping-profiles", new { connectionId = id, documentType = "SalesInvoice", direction = "Inbound", name = "Mapped store" });
        Assert.Equal(HttpStatusCode.Created, profile.StatusCode);
        var profilePath = path + "/mapping-profiles/" + (await profile.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var rules = FieldMapper.Defaults(FieldMapper.Inbound).Select(r => r.Target is "customer" or "item" ? r with { Operation = "reference" } : r).ToArray();
        await RequestJson(owner, profilePath + "/versions", new { expectedVersion = 0, rules });
        await RequestJson(owner, profilePath + "/activate", new { version = 1 });
        var order = (await Json(owner, path + "/source-orders")).GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("externalOrderId").GetString() == "0001");
        var sourceId = order.GetProperty("id").GetGuid();
        var key = order.GetProperty("key").GetString();
        var refreshed = await RequestJson(owner, connection + "/import-orders?scenario=RefundUpdate&changedSince=2026-01-01T00%3A00%3A00Z", new { });
        Assert.Equal(0, refreshed.GetProperty("imported").GetInt32());
        Assert.Equal(1, refreshed.GetProperty("updated").GetInt32());
        var updated = (await Json(owner, path + "/source-orders/" + sourceId)).GetProperty("record");
        Assert.Equal(2, updated.GetProperty("revision").GetInt32());
        Assert.Equal("Partially Refunded", updated.GetProperty("status").GetString());
        Assert.Equal(key, updated.GetProperty("key").GetString());
        var mapped = await Json(owner, f.Path + "/external-order/" + key);
        Assert.Equal("4", mapped.GetProperty("lines")[0].GetProperty("remainingQuantity").GetString());
        Assert.Equal(f.Customer, mapped.GetProperty("customerId").GetGuid());
        Assert.Equal(balance, await Balance(owner, f.Subscriber));
        Assert.Empty((await Json(owner, f.Path + "/SalesInvoice/register")).GetProperty("items").EnumerateArray());
        Assert.Empty((await Json(owner, f.Path + "/CreditMemo/register")).GetProperty("items").EnumerateArray());
    }

    private static async Task<string> CreateOutbound(HttpClient owner, DocumentFixture f, Guid connection, string type)
    {
        var path = ConnectPath(f) + "/mapping-profiles";
        using var response = await Mutate(owner, path, new { connectionId = connection, name = type + " default", documentType = type, direction = "Outbound" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return path + "/" + (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task ActivateDestination(HttpClient owner, DocumentFixture f, Guid connection, string type, string scenario)
    {
        var path = await CreateOutbound(owner, f, connection, type);
        var version = await RequestJson(owner, path + "/versions", new { expectedVersion = 0, rules = FieldMapper.Defaults(FieldMapper.Outbound) });
        Assert.Equal("Validated", version.GetProperty("status").GetString());
        await RequestJson(owner, path + "/activate", new { version = 1 });
        await Expect(Mutate(owner, ConnectPath(f) + "/accounting/destinations", new { profileId = Guid.Parse(path.Split('/').Last()),
            branchId = f.Branch, documentType = type, enabled = true, automatic = true, scenario }, HttpMethod.Put), HttpStatusCode.OK);
    }
}
