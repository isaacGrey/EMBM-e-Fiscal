using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.MasterData;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task ResolvesCustomerPriceTiersUnitsTermsAndAuthorizedCurrency()
    {
        var (tenant, _, email) = await SeedSubscriber();
        using var owner = Browser();
        await Login(owner, email, _password);
        var path = MasterPath(tenant, await CreateCompany(owner, tenant, "PRICE"));
        var unit = await Add(owner, path, "units", "EA", new { symbol = "ea" });
        var box = await Add(owner, path, "units", "BOX", new { symbol = "box" });
        var item = await Add(owner, path, "items", "ITEM", new { itemType = "Goods", unit, price = "15.125" });
        await Add(owner, path, "item-units", "ITEMBOX", new { item, unit = box, factor = "12" });
        await Expect(Mutate(owner, path + "/item-units", Record("WRONG", "Invalid factor", new { item, unit = box, factor = "-1" })), HttpStatusCode.BadRequest);
        var list = await Add(owner, path, "price-lists", "NORMAL", new { effectiveFrom = "2026-01-01" });
        var term = await Add(owner, path, "payment-terms", "NET15", new { method = "CalendarDays", days = "15" });
        var group = await Add(owner, path, "customer-groups", "REGULAR", new { priceList = list, paymentTerm = term });
        var customer = await Add(owner, path, "customers", "BUYER", new { classification = "Corporate", group });
        var generic = await Add(owner, path, "customers", "NEW", new { classification = "Corporate", priceList = list });
        await Add(owner, path, "price-entries", "GENERIC", new { priceList = list, item, unit = box, price = "170", effectiveFrom = "2026-01-01" });
        await Add(owner, path, "price-entries", "GROUP", new { priceList = list, item, unit = box, price = "160", customerGroup = group, effectiveFrom = "2026-01-01" });
        var tier = await Add(owner, path, "price-entries", "CUSTOM-10", new { priceList = list, item, unit = box, price = "149.955", customer,
            minimumQuantity = "10", effectiveFrom = "2026-01-01" });
        async Task<JsonElement> Quote(Guid? person, string quantity, string date = "2026-04-01") =>
            await RequestJson(owner, path + "/resolve-price", new { itemId = item, unitId = box, customerId = person, quantity, date });
        Assert.Equal("181.5", (await Quote(null, "1")).GetProperty("unitPrice").GetString());
        Assert.Equal("170", (await Quote(generic, "1")).GetProperty("unitPrice").GetString());
        Assert.Equal("160", (await Quote(customer, "9")).GetProperty("unitPrice").GetString());
        var best = await Quote(customer, "10");
        Assert.Equal("149.955", best.GetProperty("unitPrice").GetString());
        Assert.Equal("12", best.GetProperty("baseFactor").GetString());
        Assert.Equal("181.5", (await Quote(customer, "10", "2025-12-31")).GetProperty("unitPrice").GetString());
        await Expect(Mutate(owner, path + "/price-entries/" + tier, new { code = "CUSTOM-10", name = "Revised customer tier", active = true,
            revision = 1, data = new { priceList = list, item, unit = box, price = "145.25", customer, minimumQuantity = "10",
                effectiveFrom = "2026-05-01" } }, HttpMethod.Put), HttpStatusCode.OK);
        Assert.Equal("149.955", (await Quote(customer, "10")).GetProperty("unitPrice").GetString());
        var revised = await Quote(customer, "10", "2026-05-01");
        Assert.Equal("145.25", revised.GetProperty("unitPrice").GetString());
        Assert.Equal(2, revised.GetProperty("priceEntryRevision").GetInt32());

        var vat = await Add(owner, path, "tax-groups", "VAT", Vat("Exclusive", "2026-01-01"));
        var result = await RequestJson(owner, path + "/calculate", new { date = "2026-04-01", purpose = "SalesInvoice", customerId = customer,
            lines = new[] { Line(item, vat, quantity: "10", unit: box) } });
        Assert.Equal("2026-04-16", result.GetProperty("dueDate").GetString());
        var currency = await Add(owner, path, "currencies", "USD", new { symbol = "USD", precision = "2" });
        await Expect(Mutate(owner, path + "/calculate", new { date = "2026-04-01", purpose = "SalesInvoice", currency = "USD",
            lines = new[] { Line(item, vat, price: "10") } }), HttpStatusCode.BadRequest);
        await Add(owner, path, "exchange-rates", "RATE", new { currency, rate = "51.12345678", date = "2026-04-01", source = "Synthetic rate fixture" });
        var foreign = await RequestJson(owner, path + "/calculate", new { date = "2026-04-01", purpose = "SalesInvoice", currency = "USD",
            lines = new[] { Line(item, vat, price: "10") } });
        Assert.Equal("51.12345678", foreign.GetProperty("currency").GetProperty("exchangeRate").GetString());
    }

    [PostgresFact]
    public async Task CalculatesInclusiveExclusiveDiscountAllocationsAndCreditMemosWithoutWriting()
    {
        using var buyer = await BillingBuyer();
        var order = await StartOrder(buyer, "/api/billing", Order("InitialSubscription"));
        var paid = await Simulate(buyer, order.GetProperty("id").GetGuid(), "paid");
        var tenant = paid.GetProperty("subscriberId").GetGuid();
        var company = await CreateCompany(buyer, tenant, "PREVIEW");
        var path = MasterPath(tenant, company);
        var unit = await Add(buyer, path, "units", "EA", new { symbol = "ea" });
        var tax = await Add(buyer, path, "tax-groups", "VAT", Vat("Exclusive", "2026-01-01"));
        var item = await Add(buyer, path, "items", "ITEM", new { itemType = "Goods", unit, price = "100", taxGroup = tax });
        var ten = await Add(buyer, path, "discount-groups", "TEN", Discount("Percentage", "10", "Line"));
        var oneCent = await Add(buyer, path, "discount-groups", "CENT", Discount("Fixed", "0.01", "Document"));
        var tooBig = await Add(buyer, path, "discount-groups", "INVALID", Discount("Fixed", "500", "Line"));
        await Expect(Mutate(buyer, path + "/discount-groups", Record("OVER", "Over 100", Discount("Percentage", "100.1", "Line"))),
            HttpStatusCode.BadRequest);
        await Expect(Mutate(buyer, path + "/tax-groups/" + tax, new { code = "VAT", name = "Inclusive VAT", revision = 1, active = true,
            data = Vat("Inclusive", "2026-08-01") }, HttpMethod.Put), HttpStatusCode.OK);
        async Task<(int Ledger, int Lots, int Reservations, int Records, int Audits)> Counts()
        {
            (int, int, int, int, int) result = default;
            await Db(async db => result = (await db.CreditLedgerEntries.CountAsync(x => x.SubscriberId == tenant),
                await db.CreditLots.CountAsync(x => x.SubscriberId == tenant), await db.CreditReservations.CountAsync(x => x.SubscriberId == tenant),
                await db.MasterRecords.CountAsync(x => x.SubscriberId == tenant), await db.SecurityAudits.CountAsync(x => x.SubscriberId == tenant)));
            return result;
        }
        var before = await Counts();
        var exclusive = await PreviewFor(buyer, path, "2026-07-01", new[] { Line(item, null, discounts: [ten]) });
        Assert.Equal("10", Total(exclusive, "commercialDiscount"));
        Assert.Equal("90", Total(exclusive, "vatableSales"));
        Assert.Equal("10.8", Total(exclusive, "vat"));
        Assert.Equal("100.8", Total(exclusive, "grossDocumentTotal"));
        Assert.Equal(1, exclusive.GetProperty("lines")[0].GetProperty("tax").GetProperty("version").GetInt32());

        var credits = await PreviewFor(buyer, path, "2026-08-01", new[] { Line(item, null, price: "112") }, purpose: "CreditMemo");
        Assert.Equal(-1, credits.GetProperty("postingDirection").GetInt32());
        Assert.Equal("100", Total(credits, "vatableSales"));
        Assert.Equal("12", Total(credits, "vat"));
        Assert.Equal("112", Total(credits, "grossDocumentTotal"));
        Assert.Equal(2, credits.GetProperty("lines")[0].GetProperty("tax").GetProperty("version").GetInt32());

        var document = await PreviewFor(buyer, path, "2026-07-01", new[] { Line(item, null), Line(item, null) }, discounts: [oneCent]);
        Assert.Equal("0.01", Total(document, "commercialDiscount"));
        Assert.Equal("223.99", Total(document, "netAmountDue"));
        Assert.Equal("0.01", document.GetProperty("lines")[0].GetProperty("discounts")[0].GetProperty("amount").GetString());
        Assert.Equal("0", document.GetProperty("lines")[1].GetProperty("discounts")[0].GetProperty("amount").GetString());

        await Expect(Mutate(buyer, path + "/calculate", new { date = "2026-07-01", purpose = "SalesInvoice",
            lines = new[] { Line(item, null, discounts: [tooBig]) } }), HttpStatusCode.BadRequest);
        Assert.Equal(before, await Counts());
    }

    [PostgresFact]
    public async Task StatutoryMedicineAndExemptOrZeroRatedSalesRequireDocumentedEligibility()
    {
        var (tenant, _, email) = await SeedSubscriber();
        using var owner = Browser();
        await Login(owner, email, _password);
        var path = MasterPath(tenant, await CreateCompany(owner, tenant, "STATUTORY"));
        var unit = await Add(owner, path, "units", "EA", new { symbol = "ea" });
        var category = await Add(owner, path, "item-categories", "COVERED", new { description = "Synthetic configured category for rule tests" });
        var tax = await Add(owner, path, "tax-groups", "VAT", Vat("Inclusive", "2026-01-01"));
        var item = await Add(owner, path, "items", "MEDICINE", new { itemType = "Goods", unit, category, price = "112" });
        var consumer = await Add(owner, path, "customers", "INDIVIDUAL", new { classification = "Individual" });
        object Statutory(string group, string reference) => new { category = group, method = "Percentage", amount = "20", scope = "Line",
            priority = "10", combination = "Exclusive", taxTreatment = "VATExempt", authority = reference, companyAuthorized = true,
            item, effectiveFrom = "2026-01-01" };
        var senior = await Add(owner, path, "discount-groups", "SENIOR", Statutory("SeniorCitizen", RegulatoryCatalog.Senior));
        var pwd = await Add(owner, path, "discount-groups", "PWD", Statutory("PWD", RegulatoryCatalog.Pwd));
        var unverified = await Add(owner, path, "discount-groups", "SOLO", Statutory("SoloParent", "PH-SOLO-PARENT"));
        const string date = "2026-07-01";
        var seniorProof = new EligibilityProof("SeniorCitizen", null, true, true, true, true, false);
        var pwdProof = seniorProof with { Category = "PWD", Quantity = "1" };
        var exempt = await PreviewFor(owner, path, date, new[] { Line(item, tax, discounts: [senior], proof: seniorProof) }, consumer);
        Assert.Equal("20", Total(exempt, "statutoryDiscount"));
        Assert.Equal("80", Total(exempt, "vatExemptSales"));
        Assert.Equal("0", Total(exempt, "vat"));
        Assert.Equal("80", Total(exempt, "netAmountDue"));
        var partial = await PreviewFor(owner, path, date, new[] { Line(item, tax, "2", discounts: [pwd], proof: pwdProof) }, consumer);
        Assert.Equal("80", Total(partial, "vatExemptSales"));
        Assert.Equal("12", Total(partial, "vat"));
        Assert.Equal("192", Total(partial, "netAmountDue"));

        foreach (var invalid in new[]
        {
            Line(item, tax, discounts: [senior], proof: seniorProof with { IdentityChecked = false }),
            Line(item, tax, discounts: [senior], proof: seniorProof with { CompetingPromotion = true }),
            Line(item, tax, discounts: [senior, pwd], proof: seniorProof),
            Line(item, tax, discounts: [unverified], proof: seniorProof with { Category = "SoloParent" }),
        })
            await Expect(Mutate(owner, path + "/calculate", new { date, purpose = "SalesInvoice", customerId = consumer, lines = new[] { invalid } }), HttpStatusCode.BadRequest);

        var publication = await Add(owner, path, "tax-groups", "BOOKS", new { classification = "VATExempt", treatment = "Exclusive", rounding = "HalfAway",
            authority = RegulatoryCatalog.Books, companyAuthorized = true, category, effectiveFrom = "2026-01-01" });
        var export = await Add(owner, path, "tax-groups", "EXPORT", new { classification = "ZeroRated", treatment = "Exclusive", rounding = "HalfAway",
            authority = RegulatoryCatalog.Export, companyAuthorized = true, category, effectiveFrom = "2026-01-01" });
        var publicationProof = new EligibilityProof("CoveredPublication", null, false, false, true, true, false);
        var book = await Add(owner, path, "items", "BOOK", new { itemType = "Goods", unit, category, price = "100" });
        Assert.Equal("100", Total(await PreviewFor(owner, path, date, new[] { Line(book, publication, proof: publicationProof) }), "vatExemptSales"));
        var direct = publicationProof with { Category = "DirectExport" };
        var zero = await PreviewFor(owner, path, date, new[] { Line(book, export, proof: direct) });
        Assert.Equal("100", Total(zero, "zeroRatedSales"));
        Assert.Equal("100", Total(zero, "taxableAmount"));
        Assert.Equal("0", Total(zero, "vatableSales"));
        await Expect(Mutate(owner, path + "/calculate", new { date, purpose = "SalesInvoice", lines = new[] { Line(book, publication) } }), HttpStatusCode.BadRequest);
    }

    private static PreviewLine Line(Guid item, Guid? tax, string quantity = "1", string? price = null, Guid? unit = null, Guid[]? discounts = null,
        EligibilityProof? proof = null) => new(item, unit, quantity, price, tax, discounts, proof);
    private static object Discount(string method, string amount, string scope) => new
    {
        category = "Commercial", method, amount, scope, priority = "10", combination = "Stackable", taxTreatment = "ReduceBase", effectiveFrom = "2026-01-01"
    };
    private static async Task<Guid> Add(HttpClient owner, string path, string kind, string code, object data) =>
        (await MasterCreate(owner, path, kind, code, code, data)).GetProperty("id").GetGuid();

    private static Task<JsonElement> PreviewFor(HttpClient owner, string path, string date, PreviewLine[] lines, Guid? customer = null,
        Guid[]? discounts = null, string purpose = "SalesInvoice") => RequestJson(owner, path + "/calculate",
        new { purpose, date, lines, customerId = customer, documentDiscountIds = discounts });

    private static string? Total(JsonElement preview, string kind) => preview.GetProperty("totals").GetProperty(kind).GetString();

    private static async Task<JsonElement> RequestJson(HttpClient owner, string path, object input)
    {
        using var response = await Mutate(owner, path, input);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

public sealed class FinancialMathTests
{
    [Fact]
    public void RoundingAndWithholdingAreSeparateFromGrossTotals()
    {
        var item = new MasterRecord { Name = "Synthetic arithmetic fixture" };
        var rule = new RuleSnapshot(Guid.Empty, "TEST", "Synthetic arithmetic rule", 1, null, null, new { });
        var tax = new TaxTreatment("VATable", false, MidpointRounding.AwayFromZero,
            [new("VAT", "NetSales", 12m), new("CreditableWithholding", "NetSales", 3m), new("WithholdingVAT", "GrossTotal", 2m)], rule);
        var line = new CalculationLine(item, new(0, Guid.Empty, 1m, null, null), tax, 1m, 100.05m);
        var amount = FinancialMath.Tax(line, 2);
        Assert.Equal(112.06m, amount.InvoiceTotal);
        Assert.Equal(3.00m, amount.IncomeWithholding);
        Assert.Equal(2.24m, amount.VatWithholding);
        Assert.Equal(106.82m, amount.InvoiceTotal - amount.IncomeWithholding - amount.VatWithholding);
        Assert.Equal(1.00m, FinancialMath.Round(1.005m, 2, MidpointRounding.ToEven));
        Assert.Equal(1.01m, FinancialMath.Round(1.005m, 2));
        Assert.Equal([0.01m, 0m, 0m], FinancialMath.Allocate(0.01m, [1m, 1m, 1m], 2));
        Assert.Equal(new DateOnly(2028, 3, 10), FinancialMath.DueDate(new(2028, 2, 3), "MonthEnd", 10));
    }
}

