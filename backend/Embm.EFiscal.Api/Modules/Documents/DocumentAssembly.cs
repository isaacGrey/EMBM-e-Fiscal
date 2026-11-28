using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Documents;

internal sealed class DocumentAssembly(EfiscalDbContext db, FinancialPreview calculation, MasterRegistry registry, DocumentRelationships relationships,
    DocumentSettings settings, HistoricalCreditSource historicalCredits)
{
    public async Task<PreparedDocument> Prepare(Company company, Guid actor, string type, DocumentInput input, bool fixture, CancellationToken ct)
    {
        Need(input.Action == Action(type), "action", "Use the final action for this document type.");
        Need(input.Date.Year is >= 2000 and <= 2199, "date", "Select a valid date.");
        Need(input.Lines is { Length: >= 1 and <= 100 } && input.Lines.All(x => x is not null), "lines", "Supply 1 to 100 lines.");
        Need(!input.RequestedDeliveryDate.HasValue || SalesOrder(type) && input.RequestedDeliveryDate >= input.Date,
            "requestedDeliveryDate", "A requested delivery date must fall on or after the Sales Order date.");
        Need(SalesOrder(type) || !input.AllowPartial && input.WarehouseReference is null,
            "warehouseReference", "Warehouse references and partial fulfillment settings belong to Sales Orders.");
        Need(type == "CreditMemo" || input.AdjustmentKind is null && input.AdjustmentReason is null,
            "adjustmentReason", "Adjustment fields belong to Credit Memos.");
        Need((type == "SalesInvoice" || input.ExternalOrderKey is null) && (!SalesOrder(type) || input.SourceDocumentId is null)
            && !(input.SourceDocumentId.HasValue && input.ExternalOrderKey is not null), "sourceDocumentId", "Choose one supported source.");

        Need(input.HistoricalInvoiceReferenceId is null || fixture && type == "CreditMemo" && input.SourceDocumentId is null,
            "historicalInvoiceReferenceId", "Choose one verified original invoice for this Credit Memo.");

        var subscriber = company.SubscriberId;
        var companyId = company.Id;
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == companyId
            && x.Id == input.BranchId, ct);
        Need(branch is not null, "branchId", "Select a company branch.");
        var source = await relationships.Source(subscriber, companyId, input.SourceDocumentId, type == "CreditMemo" ? "SalesInvoice" : "SalesOrder", ct);
        var historical = input.HistoricalInvoiceReferenceId is Guid reference
            ? await historicalCredits.Read(subscriber, companyId, reference, ct) : null;
        Need(input.HistoricalInvoiceReferenceId is null || historical is not null &&
            (historical.BranchId is null || historical.BranchId == branch!.Id),
            "historicalInvoiceReferenceId", "Complete, current original-invoice and adjustment evidence is required for this branch.");
        Need(type != "CreditMemo" || source is not null || historical is not null, "sourceDocumentId", "Select an original Sales Invoice.");
        var external = await relationships.External(subscriber, companyId, input.ExternalOrderKey, ct);
        Need(external?.FinancialReview is null, "externalOrderKey", external?.FinancialReview ?? "");
        var tax = await db.TaxpayerProfiles.AsNoTracking().SingleAsync(x => x.SubscriberId == subscriber && x.CompanyId == companyId, ct);
        var rdo = await db.RdoAssignments.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == companyId
            && (x.BranchId == null || x.BranchId == input.BranchId) && x.EffectiveFrom <= input.Date
            && (x.EffectiveUntil == null || x.EffectiveUntil > input.Date))
            .OrderByDescending(x => x.BranchId.HasValue).ThenByDescending(x => x.EffectiveFrom).Select(x => x.Code).FirstOrDefaultAsync(ct);
        var seller = new PartySnapshot(company.Code, company.RegisteredName, company.TradeName, tax.Tin, rdo, company.Address, null, null,
            company.Phone, company.Email, tax.VatStatus);
        var document = new FiscalDocument { SubscriberId = subscriber, CompanyId = companyId, BranchId = branch!.Id,
            ActorId = actor, Date = input.Date, Type = type, Credits = SalesOrder(type) ? 0 : 1, DevelopmentFixture = fixture,
            PartialFulfillment = input.AllowPartial, SourceDocumentId = source?.Id, HistoricalInvoiceReferenceId = historical?.ReferenceId, ExternalOrderKey = external?.Key, ExternalOrderNumber = external?.Number };

        PartySnapshot customer;
        object? term;
        object? exchange;
        DocumentAllocation[] links;
        var customKind = type switch { "SalesInvoice" => "sales-invoice", "CreditMemo" => "credit-memo", _ => "sales-order" };
        var headerCustom = await Custom(subscriber, companyId, type == "SalesOrder" ? customKind : customKind + "-header", input.Custom, ct);
        if (type == "CreditMemo")
        {
            Need(input.PaymentTermId is null, "paymentTerm", "Keep the original payment terms for a Credit Memo.");
            if (source is not null)
            {
                var parties = Read<DocumentParties>(source.Snapshot.Parties);
                var header = Read<DocumentHeader>(source.Snapshot.Header);
                customer = parties.Customer;
                term = header.PaymentTerm;
                exchange = header.ExchangeRate;
                document.CustomerId = source.CustomerId;
                document.Currency = source.Currency;
                document.ExchangeRate = source.ExchangeRate;
                document.Precision = source.Precision;
            }
            else
            {
                customer = historical!.Customer;
                term = null;
                exchange = new { code = "PHP", rate = "1", source = "Historical accounting evidence" };
                document.CustomerId = historical.CustomerId;
                document.Currency = "PHP";
                document.ExchangeRate = 1m;
                document.Precision = 2;
            }
            document.DueDate = input.Date;
            links = await relationships.CreditInvoice(document, input, source, historical, ct);
            for (var i = 0; i < document.Lines.Count; i++)
            {
                var originalTerms = document.Lines[i].Terms;
                var extensions = await Extensions(subscriber, companyId, customKind + "-line", input.Lines![i], ct);
                document.Lines[i].Terms = Freeze(new { originalTerms, extensions });
            }
        }
        else
        {
            Need(input.Lines!.All(line => line.AdjustmentAmount is null), "lines", "Use amount adjustments on a linked Credit Memo.");
            var requestedCustomer = input.CustomerId ?? source?.CustomerId ?? external?.CustomerId;
            Need(requestedCustomer.HasValue, "customerId", "Choose a customer.");
            var requestedCurrency = input.Currency ?? source?.Currency ?? external?.Currency;
            var financial = await calculation.Resolve(subscriber, companyId, new MasterData.PreviewInput(type, input.Date, requestedCurrency,
                requestedCustomer, input.PriceListId, external?.Marketplace?.Inputs(input) ?? input.Lines!.Select(x => new PreviewLine(x.ItemId, x.UnitId, x.Quantity, x.UnitPrice,
                    x.TaxGroupId, x.DiscountGroupIds, x.Eligibility)).ToArray(), input.DocumentDiscountIds), ct, input.PaymentTermId);
            external?.Marketplace?.Verify(input, financial);
            var person = financial.Customer!;
            customer = new(person.Code, person.Data.Str("registeredName") ?? person.Name, person.Data.Str("tradeName"), person.Data.Str("tin"),
                person.Data.Str("rdo"), person.Data.Str("registeredAddress"), person.Data.Str("billingAddress"), person.Data.Str("shippingAddress"),
                person.Data.Str("contact") ?? person.Data.Str("mobile"), person.Data.Str("email"), person.Data.Str("vatStatus"),
                person.Revision, person.Data.Str("classification"));
            var currency = financial.Currency;
            term = financial.PaymentTerm is { } terms ? new { terms.Id, terms.Code, terms.Name, terms.Revision, details = SafeData(terms.Data) } : null;
            exchange = new { currency.Code, rate = Money(currency.ExchangeRate), currency.RateId, currency.RateRevision, currency.RateDate, currency.Source };
            document.CustomerId = person.Id;
            document.Precision = currency.Precision;
            document.Currency = currency.Code;
            document.ExchangeRate = currency.ExchangeRate;
            document.DueDate = financial.DueDate;
            document.Rounding = FinancialMath.Round(financial.Amounts.Sum(x => x.RoundingAdjustment)
                + financial.Lines.Sum(x => x.Gross - x.Quantity * x.Price.UnitPrice), 8);
            var unitIds = financial.Lines.Select(x => x.Price.UnitId).Distinct().ToArray();
            var units = await registry.Scope(subscriber, companyId).Where(x => unitIds.Contains(x.Id) && x.Kind == "units")
                .AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
            for (var i = 0; i < financial.Lines.Count; i++)
            {
                var line = financial.Lines[i];
                var amounts = financial.Amounts[i];
                var unit = units[line.Price.UnitId];
                var request = input.Lines![i];
                var extra = await Extensions(subscriber, companyId, type == "SalesOrder" ? customKind : customKind + "-line", request, ct);
                document.Lines.Add(new FiscalDocumentLine { SubscriberId = subscriber, CompanyId = companyId, DocumentId = document.Id,
                    Position = i + 1, ItemId = line.Item.Id, UnitId = unit.Id, ItemCode = line.Item.Code, UnitCode = unit.Code,
                    Description = Text(request.Description, 600, "description") ?? line.Item.Data.Str("description") ?? line.Item.Name,
                    Quantity = line.Quantity, UnitPrice = line.Price.UnitPrice, BaseFactor = line.Price.BaseFactor, Gross = line.Gross,
                    CommercialDiscount = line.Discounts.Where(x => x.Scope != "Statutory").Sum(x => x.Amount),
                    StatutoryDiscount = line.Discounts.Where(x => x.Scope == "Statutory").Sum(x => x.Amount),
                    VatableSales = amounts.Taxable, ExemptSales = amounts.Exempt, ZeroRatedSales = amounts.ZeroRated, NonVatSales = amounts.NonVat,
                    Vat = amounts.Vat, Total = amounts.InvoiceTotal, IncomeWithholding = amounts.IncomeWithholding, VatWithholding = amounts.VatWithholding,
                    Terms = Freeze(new { item = new { line.Item.Id, line.Item.Code, line.Item.Name, line.Item.Revision, values = SafeData(line.Item.Data) },
                        unit = new { unit.Id, unit.Code, unit.Name, unit.Revision }, line.Tax.Rule, line.Tax.Classification, line.Tax.Inclusive,
                        line.Price.PriceEntryId, line.Price.PriceListId, line.Price.PriceEntryRevision, line.Price.PriceListRevision,
                        discounts = line.Discounts.Select(x => new { x.Rule, amount = Money(x.Amount), eligible = Money(x.EligibleAmount), x.Scope }), extra })
                });
            }
            links = await relationships.InvoiceOrder(document, input.Lines!, source, external, ct);
        }
        document.CustomerName = customer.Name;
        document.CustomerCode = customer.Code;
        document.LineCount = document.Lines.Count;
        Accumulate(document);
        var templates = await settings.Templates(subscriber, companyId, type, input, ct);
        var allowed = SalesOrder(type) ? [] : type == "CreditMemo" && source is not null ? Read<DeliveryStamp>(source.Snapshot.Delivery).AuthorizedRecipients
            : await Recipients(subscriber, companyId, document.CustomerId, ct);
        var delivery = await settings.Delivery(subscriber, companyId, type, input.Recipient, allowed, ct);
        var series = await settings.EffectiveSeries(subscriber, companyId, type, branch.Id, input.Date, ct);
        var headerSnap = new DocumentHeader(Text(input.Remarks, 1000, "remarks"), Text(input.AdjustmentReason, 500, "adjustmentReason"),
            input.AdjustmentKind, input.RequestedDeliveryDate, Text(input.ShippingAddress, 600, "shippingAddress") ?? customer.ShippingAddress,
            Text(input.Salesperson, 120, "salesperson"), Text(input.PurchaseOrderReference, 120, "purchaseOrderReference"),
            Text(input.ExternalReference, 120, "externalReference"), Text(input.WarehouseReference, 100, "warehouseReference"),
            source?.Number ?? historical?.Number ?? external?.Number, term, exchange, headerCustom,
            external is null ? historical?.Evidence : new { external.Connector, external.Account, external.ExternalCompany, external.RecordId, external.SourceAt, external.Number, marketplace = external.Marketplace,
                lines = external.Lines.Select(x => new { x.Key, x.Quantity, x.PreviouslyInvoiced }) });
        document.Snapshot = new FiscalDocumentSnapshot { SubscriberId = subscriber, CompanyId = companyId, DocumentId = document.Id,
            Parties = Freeze(new DocumentParties(seller, customer, branch.Code, branch.Name, branch.Address)), Header = Freeze(headerSnap),
            Templates = Freeze(templates), Delivery = Freeze(delivery), Numbering = Freeze(series is null ? null : new
            { series.Id, series.Revision, series.Prefix, series.Digits, series.EffectiveFrom, series.BranchId }) };
        var confirmation = Digest(new { type, document.Date, document.BranchId, document.CustomerId, document.Currency, document.Precision, document.ExchangeRate,
            document.DueDate, document.SourceDocumentId, document.ExternalOrderKey, document.PartialFulfillment,
            document.Snapshot.Parties, document.Snapshot.Header, document.Snapshot.Templates, document.Snapshot.Delivery, document.Snapshot.Numbering,
            lines = document.Lines.Select(x => new { x.ItemId, x.UnitId, x.Description, x.Quantity, x.UnitPrice, x.BaseFactor,
                x.Gross, x.Total, x.Vat, x.Terms }),
            links = links.Select(x => new { x.SourceLineId, x.ExternalLineKey, x.Kind, x.Quantity, x.Amount }) });
        return new(document, series, links, confirmation, allowed);
    }

    public async Task<string[]> Recipients(Guid subscriber, Guid company, Guid customer, CancellationToken ct)
    {
        var row = await registry.Scope(subscriber, company).AsNoTracking().SingleOrDefaultAsync(x => x.Kind == "customers" && x.Id == customer && x.Active, ct);
        return row is null ? [] : new[] { row.Data.Str("email") }.Concat(row.Data.Array("invoiceRecipients").Select(x => x.GetString()))
            .OfType<string>().Where(TemplateRules.Mailbox).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<object> Custom(Guid subscriber, Guid company, string kind, JsonElement? supplied, CancellationToken ct)
    {
        Need(supplied is null || supplied.Value.ValueKind == JsonValueKind.Object && supplied.Value.EnumerateObject().Count() <= 50,
            "custom", "Use up to 50 configured custom values.");
        return await registry.CustomValues(subscriber, company, kind, Freeze(new { custom = supplied ?? Freeze(new { }) }), ct);
    }

    private async Task<object> Extensions(Guid subscriber, Guid company, string kind, DocumentLineInput input, CancellationToken ct)
    {
        (Guid? Id, string Category)[] dimensions = [(input.ProjectId, "Project"), (input.DepartmentId, "Department"),
            (input.LocationId, "Location"), (input.AccountId, "AccountReference")];
        var ids = dimensions.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        var records = await registry.Scope(subscriber, company).AsNoTracking().Where(x => ids.Contains(x.Id) && x.Active && x.Kind == "dimensions")
            .ToDictionaryAsync(x => x.Id, ct);
        foreach (var (id, category) in dimensions)
            if (id.HasValue)
                Need(records.TryGetValue(id.Value, out var found) && found.Data.Str("dimensionType") == category, "dimensions", "Select a matching company dimension.");
        return new { dimensions = records.Values.OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Name, x.Revision, category = x.Data.Str("dimensionType") }),
            custom = await Custom(subscriber, company, kind, input.Custom, ct) };
    }

    private static string? Text(string? text, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        Need(ApiSupport.Optional(text, max) && text.All(c => !char.IsControl(c) || c is '\n' or '\r' or '\t'),
            field, "Use valid text within the supported length.");
        return text.Trim();
    }

    private static void Accumulate(FiscalDocument doc)
    {
        doc.GrossSales = doc.Lines.Sum(x => x.Gross);
        doc.CommercialDiscount = doc.Lines.Sum(x => x.CommercialDiscount);
        doc.StatutoryDiscount = doc.Lines.Sum(x => x.StatutoryDiscount);
        doc.VatableSales = doc.Lines.Sum(x => x.VatableSales);
        doc.ExemptSales = doc.Lines.Sum(x => x.ExemptSales);
        doc.ZeroRatedSales = doc.Lines.Sum(x => x.ZeroRatedSales);
        doc.NonVatSales = doc.Lines.Sum(x => x.NonVatSales);
        doc.Vat = doc.Lines.Sum(x => x.Vat);
        doc.IncomeWithholding = doc.Lines.Sum(x => x.IncomeWithholding);
        doc.VatWithholding = doc.Lines.Sum(x => x.VatWithholding);
        doc.Total = doc.Lines.Sum(x => x.Total);
        doc.AmountDue = doc.Total - doc.IncomeWithholding - doc.VatWithholding;
    }
}

