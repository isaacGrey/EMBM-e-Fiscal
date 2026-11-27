namespace Embm.EFiscal.Api.Modules.MasterData;

internal record RuleSnapshot(Guid Id, string Code, string Name, int Version, DateOnly? EffectiveFrom, string? Authority, object Terms);
internal record AppliedDiscount(RuleSnapshot Rule, string Combination, decimal Amount, decimal EligibleAmount, string Scope);
internal record TaxComponent(string Kind, string Base, decimal Rate);
internal record TaxTreatment(string Classification, bool Inclusive, MidpointRounding Rounding, TaxComponent[] Components, RuleSnapshot Rule);
internal record PriceSelection(decimal UnitPrice, Guid UnitId, decimal BaseFactor, Guid? PriceListId, Guid? PriceEntryId, int? PriceListRevision = null, int? PriceEntryRevision = null);
internal record CurrencySelection(string Code, int Precision, decimal ExchangeRate, DateOnly? RateDate, string? Source, Guid? RateId = null, int? RateRevision = null);
internal record EligibilityProof(string? Category, string? Quantity, bool IdentityChecked, bool ExclusiveUse, bool ItemCovered,
    bool SupportingEvidenceChecked, bool CompetingPromotion);
internal record PreviewLine(Guid ItemId, Guid? UnitId, string? Quantity, string? UnitPrice, Guid? TaxGroupId,
    Guid[]? DiscountGroupIds, EligibilityProof? Eligibility);
internal record PreviewInput(string? Purpose, DateOnly Date, string? Currency, Guid? CustomerId, Guid? PriceListId,
    PreviewLine[]? Lines, Guid[]? DocumentDiscountIds);
internal record PriceInput(DateOnly Date, Guid ItemId, Guid? UnitId, string? Quantity, string? Currency, Guid? CustomerId, Guid? PriceListId);

internal sealed class CalculationLine(MasterRecord item, PriceSelection price, TaxTreatment tax, decimal quantity, decimal gross)
{
    public MasterRecord Item { get; } = item;
    public PriceSelection Price { get; } = price;
    public TaxTreatment Tax { get; } = tax;
    public decimal Quantity { get; } = quantity;
    public decimal Gross { get; } = gross;
    public decimal Remaining { get; set; } = gross;
    public decimal StatutoryPortion { get; set; }
    public decimal StatutoryBase { get; set; }
    public List<AppliedDiscount> Discounts { get; } = [];
}

internal record LineAmounts(decimal Taxable, decimal Exempt, decimal ZeroRated, decimal NonVat, decimal Vat, decimal InvoiceTotal,
    decimal IncomeWithholding, decimal VatWithholding, decimal RoundingAdjustment);

internal static class FinancialMath
{
    public static decimal Round(decimal value, int precision, MidpointRounding rounding = MidpointRounding.AwayFromZero) =>
        decimal.Round(value, precision, rounding);

    public static decimal Discount(decimal eligible, decimal amount, string method, decimal? maximum, int precision, MidpointRounding rounding)
    {
        var calculated = method == "Percentage" ? eligible * (amount / 100m) : amount;
        if (maximum.HasValue) calculated = Math.Min(calculated, maximum.Value);
        var result = Round(calculated, precision, rounding);
        MasterRegistry.Need(calculated >= 0 && calculated <= eligible && result <= eligible, "discounts", "The discount exceeds the eligible amount.");
        return result;
    }

    public static decimal[] Allocate(decimal amount, IReadOnlyList<decimal> bases, int precision)
    {
        var result = new decimal[bases.Count];
        if (amount == 0) return result;
        var sum = bases.Sum();
        MasterRegistry.Need(sum > 0 && amount <= sum, "documentDiscounts", "The discount exceeds the eligible lines.");
        var scale = 1m;
        for (var number = 0; number < precision; number++) scale *= 10m;
        var raw = bases.Select(value => amount * (value / sum)).ToArray();
        for (var index = 0; index < raw.Length; index++) result[index] = decimal.Floor(raw[index] * scale) / scale;
        var available = (int)Round((amount - result.Sum()) * scale, 0);
        foreach (var index in Enumerable.Range(0, bases.Count).OrderByDescending(x => raw[x] - result[x]).ThenBy(x => x).Take(available))
            result[index] += 1m / scale;
        MasterRegistry.Need(result.Sum() == amount && result.Zip(bases).All(x => x.First <= x.Second),
            "documentDiscounts", "The document discount could not be allocated.");
        return result;
    }

    public static LineAmounts Tax(CalculationLine line, int precision)
    {
        var tax = line.Tax;
        var quote = line.Remaining - line.StatutoryPortion;
        var statutory = line.Discounts.Where(x => x.Scope == "Statutory").Sum(x => x.Amount);
        var vatRate = tax.Components.SingleOrDefault(x => x.Kind == "VAT")?.Rate / 100m ?? 0;
        var unroundedNet = tax.Inclusive ? quote / (1m + vatRate) : quote;
        var net = Round(unroundedNet, precision, tax.Rounding);
        var taxable = tax.Classification == "VATable" ? net : 0;
        var exempt = (tax.Classification == "VATExempt" ? net : 0) + line.StatutoryBase - statutory;
        var vat = tax.Inclusive ? quote - net : Round(taxable * vatRate, precision, tax.Rounding);
        var total = net + vat + line.StatutoryBase - statutory;
        var income = Withholding(tax, "CreditableWithholding", net + line.StatutoryBase - statutory, total, precision);
        var vatWithholding = Withholding(tax, "WithholdingVAT", net + line.StatutoryBase - statutory, total, precision);
        MasterRegistry.Need(income + vatWithholding <= total, "tax", "Withholding exceeds the invoice total.");
        return new(taxable, exempt, tax.Classification == "ZeroRated" ? net : 0, tax.Classification == "NonVAT" ? net : 0,
            vat, total, income, vatWithholding, tax.Inclusive ? 0 : total - (unroundedNet + taxable * vatRate + line.StatutoryBase - statutory));
    }

    private static decimal Withholding(TaxTreatment tax, string kind, decimal net, decimal total, int precision)
    {
        var component = tax.Components.SingleOrDefault(x => x.Kind == kind);
        return component is null ? 0 : Round((component.Base == "GrossTotal" ? total : net) * (component.Rate / 100m), precision, tax.Rounding);
    }

    public static DateOnly DueDate(DateOnly date, string? method, int days) =>
        method switch { "CalendarDays" => date.AddDays(days), "MonthEnd" => new DateOnly(date.Year, date.Month,
            DateTime.DaysInMonth(date.Year, date.Month)).AddDays(days), _ => date };
}

