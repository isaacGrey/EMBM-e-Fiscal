using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.MasterData;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;

namespace Embm.EFiscal.Api.Modules.Documents;

internal sealed record MarketplaceInvoiceTerms(Guid ConnectorVersionId, int MappingVersion, int SourceRevision,
    MarketplaceFinancials Components, Guid SellerDiscountGroupId, string ProductLine, decimal ProductUnitPrice)
{
    public const string ShippingLine = "marketplace-shipping";
    public const string ReviewRequired = "Marketplace financial components require an approved mapping and complete supported source evidence.";

    public PreviewLine[] Inputs(DocumentInput input)
    {
        Need(input.Currency is null or "PHP" && input.PriceListId is null && input.DocumentDiscountIds is null or { Length: 0 },
            "externalOrderKey", "Use the verified PHP marketplace amounts and its approved line-level discount.");
        return input.Lines!.Select(line =>
        {
            var shipping = line.ExternalLineKey == ShippingLine;
            var unitPrice = shipping ? Components.CustomerShipping : ProductUnitPrice;
            Need((line.ExternalLineKey == ProductLine || shipping) &&
                (line.UnitPrice is null || MasterSchema.DecimalText(line.UnitPrice, out var supplied) && supplied == unitPrice) &&
                line.TaxGroupId is null && line.DiscountGroupIds is null or { Length: 0 } && line.Eligibility is null,
                "lines", "Keep the mapped marketplace prices, tax treatment, and seller-funded discount.");
            return new PreviewLine(line.ItemId, line.UnitId, line.Quantity, Money(unitPrice), null,
                shipping ? [] : [SellerDiscountGroupId], null);
        }).ToArray();
    }

    public void Verify(DocumentInput input, ResolvedFinancialDocument financial)
    {
        Need(financial.Currency.Code == "PHP" && financial.Currency.Precision == 2 && financial.Currency.ExchangeRate == 1,
            "currency", ReviewRequired);
        Need(input.Lines!.Length == 2 && input.Lines.Count(x => x.ExternalLineKey == ShippingLine) == 1,
            "lines", "Include the mapped product and shipping lines exactly once.");
        for (var i = 0; i < input.Lines.Length; i++)
        {
            var shipping = input.Lines[i].ExternalLineKey == ShippingLine;
            var line = financial.Lines[i];
            var amounts = financial.Amounts[i];
            var gross = shipping ? Components.CustomerShipping : Components.ProductGross;
            var discount = shipping ? 0 : Components.SellerFundedDiscount;
            var taxable = gross - discount;
            Need(line.Gross == gross && line.Discounts.Sum(x => x.Amount) == discount &&
                line.Discounts.All(x => x.Scope == "Line") && line.Tax.Classification == "VATable" && !line.Tax.Inclusive &&
                amounts.Taxable == taxable && amounts.Vat == FinancialMath.Round(taxable * .12m, 2) &&
                amounts.IncomeWithholding == 0 && amounts.VatWithholding == 0,
                "lines", ReviewRequired);
        }
    }

    public decimal InvoiceTotal => FinancialMath.Round((Components.ProductGross - Components.SellerFundedDiscount
        + Components.CustomerShipping) * 1.12m, 2);
}
