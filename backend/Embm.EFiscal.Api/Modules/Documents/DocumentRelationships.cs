using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.MasterData;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;

namespace Embm.EFiscal.Api.Modules.Documents;

internal record ExternalOrderLine(string Key, Guid ItemId, Guid UnitId, decimal Quantity, decimal Factor);
internal record ExternalOrder(string Key, string Number, string Connector, string Account, string RecordId, DateTimeOffset SourceAt,
    Guid CustomerId, ExternalOrderLine[] Lines, bool Partial, string Currency = "PHP", Guid? BranchId = null,
    string? FinancialReview = null);
internal interface IExternalSalesOrderProvider
{
    Task<ExternalOrder?> Find(Guid subscriber, Guid company, string key, CancellationToken ct);
}

internal sealed class DevelopmentSalesOrders(EfiscalDbContext db, IWebHostEnvironment environment) : IExternalSalesOrderProvider
{
    public async Task<ExternalOrder?> Find(Guid subscriber, Guid company, string key, CancellationToken ct)
    {
        if (!environment.IsDevelopment() || key != "sample-order" ||
            !await db.Subscribers.AnyAsync(x => x.Id == subscriber && x.DevelopmentFixture, ct)) return null;
        var records = db.MasterRecords.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Active);
        var customer = await records.SingleOrDefaultAsync(x => x.Kind == "customers" && x.Code == "DEMO-EXT-CUSTOMER", ct);
        var item = await records.SingleOrDefaultAsync(x => x.Kind == "items" && x.Code == "DEMO-EXT-ITEM", ct);
        return customer is not null && item?.Data.Ref("unit") is Guid unit
            ? new("sample-order", "TEST-SO-101", "Development fixture", "sandbox-" + company.ToString("N"), "sample-order",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), customer.Id, [new("sample-line-1", item.Id, unit, 5m, 1m)], true)
            : null;
    }
}

internal sealed class DocumentRelationships(EfiscalDbContext db, IExternalSalesOrderProvider external)
{
    public IQueryable<FiscalDocument> Scope(Guid subscriber, Guid company) =>
        db.FiscalDocuments.Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    public async Task<FiscalDocument?> Source(Guid subscriber, Guid company, Guid? id, string type, CancellationToken ct)
    {
        if (id is null) return null;
        var source = await Scope(subscriber, company).AsNoTracking().Include(x => x.Snapshot).Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id && x.Type == type, ct);
        Need(source is not null, "sourceDocumentId", "Select an original committed record from this company.");
        return source;
    }

    public async Task<ExternalOrder?> External(Guid subscriber, Guid company, string? key, CancellationToken ct)
    {
        if (key is null) return null;
        var found = await external.Find(subscriber, company, key, ct);
        Need(found is not null, "externalOrderKey", "The authorized development Sales Order fixture is unavailable.");
        return found;
    }

    public async Task<Dictionary<Guid, (decimal Quantity, decimal Amount)>> Allocated(Guid subscriber, Guid company, Guid[] ids, CancellationToken ct)
    {
        var values = await db.DocumentAllocations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company
            && x.SourceLineId.HasValue && ids.Contains(x.SourceLineId.Value)).GroupBy(x => x.SourceLineId!.Value)
            .Select(group => new { Line = group.Key, Quantity = group.Sum(x => x.Quantity), Amount = group.Sum(x => x.Amount) })
            .ToArrayAsync(ct);
        return values.ToDictionary(x => x.Line, x => (x.Quantity, x.Amount));
    }

    public async Task<Dictionary<string, decimal>> AllocatedExternal(Guid subscriber, Guid company, string key, CancellationToken ct) =>
        await db.DocumentAllocations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ExternalOrderKey == key)
            .GroupBy(x => x.ExternalLineKey!).Select(x => new { Key = x.Key, Quantity = x.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, ct);

    public async Task<DocumentAllocation[]> InvoiceOrder(FiscalDocument document, DocumentLineInput[] inputs, FiscalDocument? order,
        ExternalOrder? imported, CancellationToken ct)
    {
        if (order is null && imported is null)
        {
            Need(inputs.All(x => x.SourceLineId is null && x.ExternalLineKey is null), "lines", "Source lines require a Sales Order.");
            return [];
        }
        Need(imported?.FinancialReview is null, "externalOrderKey", imported?.FinancialReview ?? "");
        var subscriber = document.SubscriberId;
        var company = document.CompanyId;
        var committedUsed = order is null ? [] : await Allocated(subscriber, company, order.Lines.Select(x => x.Id).ToArray(), ct);
        var importedUsed = imported is null ? [] : await AllocatedExternal(subscriber, company, imported.Key, ct);
        var remaining = order is not null
            ? order.Lines.ToDictionary(x => x.Id.ToString(), x => (x.ItemId, Available: BaseQuantity(x.Quantity, x.BaseFactor) - committedUsed.GetValueOrDefault(x.Id).Quantity))
            : imported!.Lines.ToDictionary(x => x.Key, x => (x.ItemId, Available: BaseQuantity(x.Quantity, x.Factor) - importedUsed.GetValueOrDefault(x.Key)));
        Need(order is null || order.CustomerId == document.CustomerId && order.Currency == document.Currency && order.Date <= document.Date,
            "sourceDocumentId", "The invoice must match the order customer and currency and use a date on or after the order.");
        Need(imported is null || imported.CustomerId == document.CustomerId && imported.Currency == document.Currency
            && (!imported.BranchId.HasValue || imported.BranchId == document.BranchId) && DateOnly.FromDateTime(imported.SourceAt.UtcDateTime) <= document.Date, "externalOrderKey",
            "Use the source order’s customer, currency and applicable branch. Date the invoice on or after the order.");
        List<DocumentAllocation> allocations = [];
        for (var i = 0; i < document.Lines.Count; i++)
        {
            var input = inputs[i];
            var line = document.Lines[i];
            var sourceKey = order is not null ? input.SourceLineId?.ToString() : input.ExternalLineKey;
            Need(sourceKey is not null && remaining.TryGetValue(sourceKey, out _) && (order is not null ? input.ExternalLineKey is null : input.SourceLineId is null),
                "lines", "Every invoice line must identify an eligible source order line.");
            var source = remaining[sourceKey!];
            var proposed = BaseQuantity(line.Quantity, line.BaseFactor);
            Need(source.ItemId == line.ItemId && proposed <= source.Available, "quantity", "The item or quantity exceeds what remains invoiceable on the order line.");
            remaining[sourceKey!] = (source.ItemId, source.Available - proposed);
            allocations.Add(new DocumentAllocation { SubscriberId = subscriber, CompanyId = company, LineId = line.Id,
                SourceLineId = input.SourceLineId, ExternalOrderKey = imported?.Key, ExternalLineKey = input.ExternalLineKey,
                Quantity = proposed, Amount = line.Total, Kind = "InvoiceOrder" });
        }
        if (order is { PartialFulfillment: false } || imported is { Partial: false })
            Need(remaining.Values.All(x => x.Available == 0m), "lines", "This order must be invoiced in full.");
        return allocations.ToArray();
    }

    public async Task<DocumentAllocation[]> CreditInvoice(FiscalDocument document, DocumentInput input, FiscalDocument original,
        CancellationToken ct)
    {
        Need(input.AdjustmentKind is "Return" or "PriceAdjustment" && ApiSupport.Text(input.AdjustmentReason, 500),
            "adjustmentReason", "Choose a supported adjustment type and provide a reason.");
        Need(input.CustomerId is null || input.CustomerId == original.CustomerId, "customerId", "Use the customer from the original invoice.");
        Need(input.Currency is null || input.Currency == original.Currency, "currency", "Use the original invoice currency.");
        Need(original.Date <= input.Date, "date", "The Credit Memo cannot precede the original invoice.");
        Need(input.PriceListId is null && input.DocumentDiscountIds is null or { Length: 0 }, "lines", "Credit adjustments use only the committed invoice terms.");
        var sourceIds = input.Lines!.Select(x => x.SourceLineId).ToArray();
        Need(sourceIds.All(x => x.HasValue) && sourceIds.Distinct().Count() == sourceIds.Length, "lines", "Select each original invoice line once.");
        var ids = sourceIds.Select(x => x!.Value).ToArray();
        var subscriber = document.SubscriberId;
        var company = document.CompanyId;
        var billed = original.Lines.Where(x => ids.Contains(x.Id)).ToDictionary(x => x.Id);
        var adjustments = await (from allocation in db.DocumentAllocations.AsNoTracking()
                join prior in db.FiscalDocumentLines.AsNoTracking()
                    on new { allocation.SubscriberId, allocation.CompanyId, Id = allocation.LineId }
                    equals new { prior.SubscriberId, prior.CompanyId, prior.Id }
                where allocation.SubscriberId == subscriber && allocation.CompanyId == company && allocation.Kind == "CreditInvoice"
                    && allocation.SourceLineId.HasValue && ids.Contains(allocation.SourceLineId.Value)
                select new { allocation.SourceLineId, Line = prior }).ToListAsync(ct);
        var priorLines = adjustments.ToLookup(x => x.SourceLineId, x => x.Line);
        List<DocumentAllocation> allocations = [];
        foreach (var (requested, index) in input.Lines!.Select((line, index) => (line, index)))
        {
            Need(billed.TryGetValue(requested.SourceLineId!.Value, out var originalLine), "lines", "Choose a line from this invoice.");
            var source = originalLine!;
            Need(source.StatutoryDiscount == 0 && source.IncomeWithholding == 0 && source.VatWithholding == 0,
                "lines", "This adjustment requires review of the original statutory or withholding scenario.");
            Need((requested.ItemId == Guid.Empty || requested.ItemId == source.ItemId) && (requested.UnitId is null || requested.UnitId == source.UnitId)
                && requested.UnitPrice is null && requested.TaxGroupId is null && requested.DiscountGroupIds is null && requested.Eligibility is null,
                "lines", "Keep the committed item, unit, tax and discount terms for Credit Memo adjustments.");
            var prior = priorLines[source.Id];
            var remainingQuantity = source.Quantity - prior.Sum(x => x.Quantity);
            var remainingAmount = source.Total - prior.Sum(x => x.Total);
            decimal quantity;
            decimal money;
            if (input.AdjustmentKind == "Return")
            {
                quantity = FinancialRules.Quantity(requested.Quantity);
                Need(requested.AdjustmentAmount is null && quantity <= remainingQuantity, "quantity", "The return exceeds the remaining eligible quantity.");
                money = quantity == remainingQuantity ? remainingAmount
                    : FinancialMath.Round(source.Total * (quantity / source.Quantity), document.Precision);
            }
            else
            {
                quantity = 0;
                Need(requested.Quantity is null && MasterSchema.DecimalText(requested.AdjustmentAmount, out _), "amount", "Enter the exact gross adjustment amount.");
                MasterSchema.DecimalText(requested.AdjustmentAmount, out money);
            }
            Need(money > 0 && money <= remainingAmount && FinancialMath.Round(money, document.Precision) == money,
                "amount", "Use a positive currency amount within the remaining eligible value.");
            var quote = FinancialMath.Allocate(money, [source.VatableSales - prior.Sum(x => x.VatableSales),
                source.ExemptSales - prior.Sum(x => x.ExemptSales), source.ZeroRatedSales - prior.Sum(x => x.ZeroRatedSales),
                source.NonVatSales - prior.Sum(x => x.NonVatSales), source.Vat - prior.Sum(x => x.Vat)], document.Precision);
            var ratio = money / remainingAmount;
            var credited = new FiscalDocumentLine { SubscriberId = subscriber, CompanyId = company, DocumentId = document.Id, Position = index + 1,
                ItemId = source.ItemId, UnitId = source.UnitId, ItemCode = source.ItemCode, Description = source.Description, UnitCode = source.UnitCode,
                UnitPrice = source.UnitPrice, BaseFactor = source.BaseFactor, Quantity = quantity, Total = money,
                Gross = FinancialMath.Round((source.Gross - prior.Sum(x => x.Gross)) * ratio, document.Precision),
                CommercialDiscount = FinancialMath.Round((source.CommercialDiscount - prior.Sum(x => x.CommercialDiscount)) * ratio, document.Precision),
                VatableSales = quote[0], ExemptSales = quote[1], ZeroRatedSales = quote[2], NonVatSales = quote[3], Vat = quote[4], Terms = source.Terms };
            document.Lines.Add(credited);
            allocations.Add(new DocumentAllocation { SubscriberId = subscriber, CompanyId = company, LineId = credited.Id,
                SourceLineId = source.Id, Quantity = quantity, Amount = money, Kind = "CreditInvoice" });
        }
        return allocations.ToArray();
    }

    public static decimal BaseQuantity(decimal amount, decimal factor)
    {
        var value = amount * factor;
        Need(value > 0 && value <= 1_000_000_000_000_000_000m && FinancialMath.Round(value, 8) == value,
            "quantity", "The source unit conversion exceeds the supported base quantity or precision.");
        return value;
    }
}

