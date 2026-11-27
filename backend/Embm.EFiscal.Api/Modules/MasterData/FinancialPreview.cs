using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;
using static Embm.EFiscal.Api.Modules.MasterData.MasterSchema;
using static Embm.EFiscal.Api.Modules.MasterData.PriceResolution;

namespace Embm.EFiscal.Api.Modules.MasterData;

internal record ResolvedFinancialDocument(CurrencySelection Currency, MasterRecord? Customer, MasterRecord? PaymentTerm,
    DateOnly DueDate, IReadOnlyList<CalculationLine> Lines, LineAmounts[] Amounts);

internal sealed class FinancialPreview(EfiscalDbContext db, MasterRegistry registry)
{
    public async Task<object> Quote(Guid subscriber, Guid company, PriceInput input, CancellationToken ct)
    {
        EnsureDate(input.Date);
        var scope = registry.Scope(subscriber, company).AsNoTracking();
        var customer = await Get(scope, input.CustomerId, "customers", "customer", ct);
        var group = await Get(scope, customer?.Data.Ref("group"), "customer-groups", "customer", ct, false);
        var currency = await Currency(scope, input.Currency, customer, input.Date, ct);
        var item = (await Get(scope, input.ItemId, "items", "item", ct))!;
        var price = await Price(db, scope, customer, group, item, input.UnitId, FinancialRules.Quantity(input.Quantity), input.Date, currency.Code, input.PriceListId, ct);
        return new { item.Id, item.Name, price.UnitId, unitPrice = Money(price.UnitPrice),
            baseFactor = Money(price.BaseFactor), price.PriceListId, price.PriceEntryId, price.PriceListRevision, price.PriceEntryRevision, currency = currency.Code };
    }

    public async Task<object> Calculate(Guid subscriber, Guid company, PreviewInput input, CancellationToken ct) =>
        Totals(input, await Resolve(subscriber, company, input, ct));

    public async Task<ResolvedFinancialDocument> Resolve(Guid subscriber, Guid company, PreviewInput input, CancellationToken ct, Guid? paymentTermId = null)
    {
        EnsureDate(input.Date);
        Need(input.Purpose is "SalesInvoice" or "CreditMemo" or "SalesOrder", "purpose", "Choose a supported document type.");
        Need(input.Lines is { Length: >= 1 and <= 100 } && input.Lines.All(x => x is not null), "lines", "Supply 1 to 100 lines.");
        var scope = registry.Scope(subscriber, company).AsNoTracking();
        var taxpayer = await db.TaxpayerProfiles.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company)
            .Select(x => x.VatStatus).SingleAsync(ct);
        var customer = await Get(scope, input.CustomerId, "customers", "customer", ct);
        var group = await Get(scope, customer?.Data.Ref("group"), "customer-groups", "customer", ct, false);
        var currency = await Currency(scope, input.Currency, customer, input.Date, ct);
        var terms = await Get(scope, paymentTermId ?? customer?.Data.Ref("paymentTerm") ?? group?.Data.Ref("paymentTerm"), "payment-terms", "paymentTerm", ct);
        var rules = new EffectiveRules(db, scope, input.Date);
        List<CalculationLine> resolved = [];
        for (var index = 0; index < input.Lines!.Length; index++)
        {
            try
            {
                var line = input.Lines[index];
                var item = (await Get(scope, line.ItemId, "items", "item", ct))!;
                var quantity = FinancialRules.Quantity(line.Quantity);
                var selectedTax = line.TaxGroupId ?? item.Data.Ref("taxGroup") ?? customer?.Data.Ref("taxGroup") ?? group?.Data.Ref("taxGroup");
                Need(selectedTax.HasValue, "taxGroup", "Select a tax group or configure an applicable default.");
                var taxRule = await rules.Find(selectedTax!.Value, "tax-groups", "taxGroup", ct);
                var tax = FinancialRules.Tax(taxRule, item, group, line.Eligibility, input.Date, taxpayer);
                decimal manual = 0;
                Need(line.UnitPrice is null || (DecimalText(line.UnitPrice, out manual) && manual >= 0), "unitPrice",
                    "Enter a non-negative decimal string for the unit price.");
                var price = await Price(db, scope, customer, group, item, line.UnitId, quantity, input.Date, currency.Code, input.PriceListId, ct, line.UnitPrice is not null);
                if (line.UnitPrice is not null) price = price with { UnitPrice = manual };
                var raw = quantity * price.UnitPrice;
                Need(raw <= 1_000_000_000_000_000m, "quantity", "The line exceeds the supported amount.");
                var calculated = new CalculationLine(item, price, tax, quantity, FinancialMath.Round(raw, currency.Precision, tax.Rounding));
                var inherited = item.Data.Ref("discountGroup") ?? customer?.Data.Ref("discountGroup") ?? group?.Data.Ref("discountGroup");
                var discountIds = line.DiscountGroupIds ?? (inherited is Guid defaultId ? [defaultId] : []);
                foreach (var discount in await rules.Discounts(discountIds, ct))
                    FinancialRules.ApplyLine(calculated, discount, line.Eligibility, customer, group, input.Date, currency.Precision);
                resolved.Add(calculated);
            }
            catch (MasterValidation error) { throw new MasterValidation($"lines[{index}].{error.Field}", error.Message); }
        }
        foreach (var discount in await rules.Discounts(input.DocumentDiscountIds ?? [], ct))
            FinancialRules.ApplyDocument(resolved, discount, customer, group, currency.Precision);
        return new(currency, customer, terms, FinancialMath.DueDate(input.Date, terms?.Data.Str("method"), (int)(terms?.Data.Num("days") ?? 0m)),
            resolved, resolved.Select(line => FinancialMath.Tax(line, currency.Precision)).ToArray());
    }

    private static object Totals(PreviewInput input, ResolvedFinancialDocument financial)
    {
        var (currency, customer, terms, dueDate, resolved, amounts) = financial;
        var total = amounts.Sum(x => x.InvoiceTotal);
        var income = amounts.Sum(x => x.IncomeWithholding);
        var vatWithholding = amounts.Sum(x => x.VatWithholding);
        return new
        {
            input.Purpose, input.Date, postingDirection = input.Purpose == "CreditMemo" ? -1 : 1,
            currency = new { currency.Code, currency.Precision, exchangeRate = Money(currency.ExchangeRate), currency.RateDate, currency.Source, currency.RateId, currency.RateRevision },
            customer = customer is null ? null : new { customer.Id, customer.Code, customer.Name, customer.Revision },
            paymentTerm = terms is null ? null : new { terms.Id, terms.Code, terms.Name, terms.Revision },
            dueDate,
            totals = new
            {
                grossLines = Money(resolved.Sum(x => x.Gross)),
                commercialDiscount = Money(resolved.SelectMany(x => x.Discounts).Where(x => x.Scope != "Statutory").Sum(x => x.Amount)),
                statutoryDiscount = Money(resolved.SelectMany(x => x.Discounts).Where(x => x.Scope == "Statutory").Sum(x => x.Amount)),
                taxableAmount = Money(amounts.Sum(x => x.Taxable + x.ZeroRated)), vatableSales = Money(amounts.Sum(x => x.Taxable)),
                vatExemptSales = Money(amounts.Sum(x => x.Exempt)), zeroRatedSales = Money(amounts.Sum(x => x.ZeroRated)),
                nonVatSales = Money(amounts.Sum(x => x.NonVat)), vat = Money(amounts.Sum(x => x.Vat)), grossDocumentTotal = Money(total),
                incomeWithholding = Money(income), vatWithholding = Money(vatWithholding), netAmountDue = Money(total - income - vatWithholding),
                roundingAdjustment = Money(amounts.Sum(x => x.RoundingAdjustment) + resolved.Sum(x => x.Gross - x.Quantity * x.Price.UnitPrice))
            },
            lines = resolved.Zip(amounts, (line, a) => new
            {
                item = new { line.Item.Id, line.Item.Code, line.Item.Name, line.Item.Revision },
                line.Price.UnitId, line.Price.PriceListId, line.Price.PriceEntryId, line.Price.PriceListRevision, line.Price.PriceEntryRevision,
                unitPrice = Money(line.Price.UnitPrice),
                baseFactor = Money(line.Price.BaseFactor), quantity = Money(line.Quantity), gross = Money(line.Gross),
                netTaxBase = Money(a.Taxable + a.ZeroRated), vatableSales = Money(a.Taxable), exemptSales = Money(a.Exempt), zeroRatedSales = Money(a.ZeroRated), nonVatSales = Money(a.NonVat),
                vat = Money(a.Vat), total = Money(a.InvoiceTotal), incomeWithholding = Money(a.IncomeWithholding), vatWithholding = Money(a.VatWithholding),
                tax = line.Tax.Rule,
                discounts = line.Discounts.Select(d => new { d.Rule, d.Scope, eligibleAmount = Money(d.EligibleAmount), amount = Money(d.Amount) })
            })
        };
    }

    private static void EnsureDate(DateOnly date) => Need(date.Year is >= 1900 and <= 2199, "date", "Choose a valid calendar date.");
}

