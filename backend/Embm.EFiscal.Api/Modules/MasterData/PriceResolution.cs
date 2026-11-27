using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;

namespace Embm.EFiscal.Api.Modules.MasterData;

internal static class PriceResolution
{
    public static async Task<CurrencySelection> Currency(IQueryable<MasterRecord> scope, string? code, MasterRecord? customer, DateOnly date, CancellationToken ct)
    {
        if (code is null && customer?.Data.Ref("currency") is Guid currencyId)
            code = (await Get(scope, currencyId, "currencies", "currency", ct))!.Code;
        code = code?.Trim().ToUpperInvariant() ?? "PHP";
        if (code == "PHP") return new("PHP", 2, 1m, null, "Base currency");
        var currency = await scope.SingleOrDefaultAsync(x => x.Kind == "currencies" && x.Code == code && x.Active, ct);
        Need(currency is not null, "currency", "Choose an active company currency.");
        var rate = await Matching(scope, "exchange-rates", new { currency = currency!.Id }, ct);
        var match = rate.Where(x => x.Data.Day("date") <= date).OrderByDescending(x => x.Data.Day("date")).FirstOrDefault();
        Need(match is not null, "currency", "Configure an authorized exchange rate on or before the preview date.");
        return new(code, (int)currency.Data.Num("precision"), match!.Data.Num("rate"), match.Data.Day("date"), match.Data.Str("source"), match.Id, match.Revision);
    }

    public static async Task<PriceSelection> Price(EfiscalDbContext db, IQueryable<MasterRecord> scope, MasterRecord? customer, MasterRecord? group, MasterRecord item,
        Guid? unit, decimal quantity, DateOnly date, string currency, Guid? requestedList, CancellationToken ct, bool manual = false)
    {
        var baseUnit = item.Data.Ref("unit")!.Value;
        var unitId = unit ?? baseUnit;
        await Get(scope, unitId, "units", "unit", ct);
        var factor = 1m;
        if (unitId != baseUnit)
        {
            var conversion = (await Matching(scope, "item-units", new { item = item.Id, unit = unitId }, ct)).SingleOrDefault();
            Need(conversion is not null, "unit", "This item has no active conversion for the selected unit.");
            factor = conversion!.Data.Num("factor");
        }
        var listId = requestedList ?? customer?.Data.Ref("priceList") ?? group?.Data.Ref("priceList");
        var listCache = new Dictionary<Guid, MasterRevision?>();
        if (requestedList is Guid requested)
        {
            var selected = await ReadList(db, scope, listCache, requested, date, ct);
            Need(selected is not null && await CurrencyCode(scope, selected.Data, ct) == currency,
                "priceList", "The selected list must be effective in the preview currency.");
        }
        if (manual) return new(0m, unitId, factor, null, null);
        var versions = new List<(MasterRecord Record, MasterRevision Entry)>();
        foreach (var identity in await Matching(scope, "price-entries", new { item = item.Id, unit = unitId }, ct, active: false))
        {
            if (quantity < identity.Data.Num("minimumQuantity") || requestedList.HasValue && identity.Data.Ref("priceList") != requestedList
                || PriceScope(identity.Data, customer?.Id, group?.Id, listId) == 0) continue;
            var revision = await MasterRegistry.EffectiveRevision(db, identity, date, ct);
            if (revision is not null) versions.Add((identity, revision));
        }
        var candidates = versions.Select(x => new { x.Entry, x.Record, Specificity = PriceScope(x.Entry.Data, customer?.Id, group?.Id, listId) })
            .OrderByDescending(x => x.Specificity).ThenByDescending(x => x.Entry.Data.Ref("priceList") == listId)
            .ThenByDescending(x => x.Entry.Data.Num("minimumQuantity")).ThenByDescending(x => x.Entry.EffectiveFrom).ThenBy(x => x.Record.Code);
        foreach (var candidate in candidates)
        {
            var id = candidate.Entry.Data.Ref("priceList")!.Value;
            var list = await ReadList(db, scope, listCache, id, date, ct);
            if (list is null || await CurrencyCode(scope, list.Data, ct) != currency) continue;
            return new(candidate.Entry.Data.Num("price"), unitId, factor, id, candidate.Record.Id, list.Revision, candidate.Entry.Revision);
        }
        Need(await CurrencyCode(scope, item.Data, ct) == currency, "unitPrice", "An explicit price or a matching price list is required in this currency.");
        return new(FinancialMath.Round(item.Data.Num("price") * factor, 8), unitId, factor, null, null);
    }

    private static async Task<MasterRevision?> ReadList(EfiscalDbContext db, IQueryable<MasterRecord> scope, Dictionary<Guid, MasterRevision?> cache,
        Guid id, DateOnly date, CancellationToken ct)
    {
        if (cache.TryGetValue(id, out var previous)) return previous;
        var record = await scope.SingleOrDefaultAsync(x => x.Id == id && x.Kind == "price-lists", ct);
        var version = record is null ? null : await MasterRegistry.EffectiveRevision(db, record, date, ct);
        cache[id] = version;
        return version;
    }

    private static int PriceScope(JsonElement entry, Guid? customer, Guid? group, Guid? list) =>
        entry.Ref("customer") is Guid person ? person == customer ? 3 : 0
        : entry.Ref("customerGroup") is Guid category ? category == group ? 2 : 0
        : list.HasValue && entry.Ref("priceList") == list ? 1 : 0;

    private static async Task<string?> CurrencyCode(IQueryable<MasterRecord> scope, JsonElement data, CancellationToken ct) =>
        data.Ref("currency") is Guid id ? (await Get(scope, id, "currencies", "currency", ct, false))?.Code : "PHP";

    private static Task<MasterRecord[]> Matching(IQueryable<MasterRecord> scope, string kind, object criteria, CancellationToken ct, bool active = true)
    {
        var json = JsonSerializer.Serialize(criteria);
        return scope.Where(x => x.Kind == kind && (!active || x.Active) && EF.Functions.JsonContains(x.Data, json)).ToArrayAsync(ct);
    }

    public static async Task<MasterRecord?> Get(IQueryable<MasterRecord> scope, Guid? id, string kind, string field, CancellationToken ct, bool required = true)
    {
        if (id is null) return null;
        var row = await scope.SingleOrDefaultAsync(x => x.Id == id && x.Kind == kind && x.Active, ct);
        Need(!required || row is not null, field, "Choose an active record from this company.");
        return row;
    }

}

