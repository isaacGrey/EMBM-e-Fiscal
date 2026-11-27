using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.MasterData;

internal record ExternalInput(string? Connector, string? ExternalAccount, string? RecordType, string? ExternalId,
    string? ExternalCode, DateTimeOffset? SourceAt);

internal sealed partial class MasterRegistry(EfiscalDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 12 };
    public IQueryable<MasterRecord> Scope(Guid subscriber, Guid company) =>
        db.MasterRecords.Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    public async Task<MasterRecord> Write(Guid subscriber, Guid company, Guid actor, MasterDefinition definition,
        MasterInput input, MasterRecord? row, bool development, string action, CancellationToken ct)
    {
        var code = input.Code?.Trim().ToUpperInvariant();
        Need(code is not null && Code().IsMatch(code), "code", "Use 1 to 40 letters, digits, underscores, periods or hyphens.");
        Need(ApiSupport.Text(input.Name, 160) && ValidXml(input.Name!), "name", "Enter a name of 1 to 160 characters.");
        Need(row is null || input.Revision == row.Revision, "revision", "This record changed. Reload it before saving.");
        Need(row is null || row.Code == code, "code", "The record code is fixed after creation.");
        var updatingId = row?.Id;
        Need(!await Scope(subscriber, company).AnyAsync(x => x.Kind == definition.Key && x.Code == code
            && x.Id != updatingId, ct), "code", "A record already uses this code in the company.");
        if (definition.Key == "currencies")
            Need(code!.Length == 3 && code.All(char.IsAsciiLetter) && code != "PHP", "code", "Use a three-letter currency code. PHP is built in.");

        var data = input.Data ?? [];
        Need(data.Keys.All(key => definition.Fields.Any(f => f.Key == key)), "data", "The form includes an unsupported field.");
        var values = new Dictionary<string, object?>();
        foreach (var field in definition.Fields)
        {
            if (!data.TryGetValue(field.Key, out var raw) || raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                (raw.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(raw.GetString())))
            {
                Need(!field.Required, field.Key, $"{field.Label} is required.");
                continue;
            }
            values[field.Key] = Normalize(field, raw);
        }
        var normalized = JsonSerializer.SerializeToElement(values, Json);
        await ValidateRelations(subscriber, company, definition, normalized, row?.Id, ct);
        if (definition.Key == "customers")
        {
            if (normalized.Str("tin") is string tin)
                Need(Tin().IsMatch(tin), "tin", "Use nine TIN digits and an optional three- to five-digit branch suffix.");
            if (normalized.Str("rdo") is string rdo)
                Need(await db.RdoReferences.AnyAsync(x => x.Code == rdo && x.Active && (development || !x.DevelopmentFixture), ct),
                    "rdo", "Select an active RDO from the reference list.");
        }
        if (row is not null && definition.Key == "price-entries")
            Need(new[] { "priceList", "item", "unit", "customer", "customerGroup" }.All(key => row.Data.Ref(key) == normalized.Ref(key))
                && row.Data.Num("minimumQuantity") == normalized.Num("minimumQuantity"), "priceList",
                "Create a new entry to change the item, unit, list, customer coverage or quantity tier.");
        if (row is not null && definition.Key == "exchange-rates")
            Need(row.Data.Ref("currency") == normalized.Ref("currency") && row.Data.Day("date") == normalized.Day("date"),
                "date", "Create a new exchange rate for a different currency or date.");
        var effective = normalized.Day("effectiveFrom");
        var through = normalized.Day("effectiveThrough");
        Need(through is null || through >= effective, "effectiveThrough", "End date must be on or after the effective date.");
        if (definition.Effective && row is not null)
        {
            var latest = row.Data.Day("effectiveFrom");
            Need(effective >= latest, "effectiveFrom", "A new revision cannot precede the most recently saved effective date.");
        }
        ValidateKind(definition.Key, normalized);
        var target = row ?? new MasterRecord { SubscriberId = subscriber, CompanyId = company, Kind = definition.Key, Code = code! };
        if (definition.Key is "customers" or "items")
        {
            values["custom"] = await CustomValues(subscriber, company, definition.Key, normalized, ct);
            if (values.Remove("external", out var external))
                await SetExternal(subscriber, company, definition.Key, target.Id, (ExternalInput[])external!, ct);
            else if (row is not null) await SetExternal(subscriber, company, definition.Key, target.Id, [], ct);
            if (external is not null) values["external"] = external;
        }
        if (definition.Key == "custom-fields" && normalized.Str("default") is string fallback)
            await EnforceCustom(subscriber, company, normalized, JsonSerializer.SerializeToElement(fallback), ct);
        target.Name = input.Name!.Trim();
        target.Active = input.Active;
        target.Revision++;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        target.Data = JsonSerializer.SerializeToElement(values, Json);
        if (row is null) db.MasterRecords.Add(target);

        var links = definition.Fields.Where(f => f.Reference is not null && target.Data.Ref(f.Key).HasValue)
            .ToDictionary(f => f.Key, f => target.Data.Ref(f.Key)!.Value);
        if (target.Data.ValueKind == JsonValueKind.Object && target.Data.TryGetProperty("custom", out var custom))
        {
            var refs = await Scope(subscriber, company).Where(x => x.Kind == "custom-fields" && x.Active).ToArrayAsync(ct);
            foreach (var field in refs.Where(x => x.Data.Str("valueType") == "Reference"))
                if (custom.Ref(field.Code) is Guid id) links["custom:" + field.Code] = id;
        }
        if (row is not null)
        {
            var previous = await db.MasterLinks.Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.RecordId == target.Id).ToArrayAsync(ct);
            db.MasterLinks.RemoveRange(previous.Where(x => !links.TryGetValue(x.Field, out var id) || id != x.TargetId));
            foreach (var unchanged in previous.Where(x => links.TryGetValue(x.Field, out var id) && id == x.TargetId)) links.Remove(unchanged.Field);
        }
        db.MasterLinks.AddRange(links.Select(link => new MasterLink { SubscriberId = subscriber, CompanyId = company, RecordId = target.Id,
            Field = link.Key, TargetId = link.Value }));
        db.MasterRevisions.Add(new MasterRevision { SubscriberId = subscriber, CompanyId = company, RecordId = target.Id,
            Revision = target.Revision, Name = target.Name, Data = target.Data, Active = target.Active, EffectiveFrom = effective,
            EffectiveThrough = through, ActorId = actor, At = target.UpdatedAt, Action = action });
        db.Audit($"MasterData.{definition.Key}.{action}", actor, subscriber, target.Id);
        return target;
    }

    internal static async Task<MasterRevision?> EffectiveRevision(EfiscalDbContext db, MasterRecord record, DateOnly date, CancellationToken ct)
    {
        var revision = await db.MasterRevisions.AsNoTracking().Where(x => x.SubscriberId == record.SubscriberId
            && x.CompanyId == record.CompanyId && x.RecordId == record.Id && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
        return revision is { Active: true } && (revision.EffectiveThrough is null || date <= revision.EffectiveThrough) ? revision : null;
    }

    public static object SafeData(JsonElement data)
    {
        var properties = data.EnumerateObject().ToDictionary(x => x.Name, x => SafeValue(x.Value));
        return properties;
    }

    internal static object? SafeValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => value.EnumerateArray().Select(SafeValue).ToArray(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(x => x.Name, x => SafeValue(x.Value)),
        _ => value
    };

    private object Normalize(MasterField field, JsonElement raw)
    {
        var key = field.Key;
        var value = raw.ValueKind == JsonValueKind.String ? raw.GetString()?.Trim() : null;
        switch (field.Type)
        {
            case "decimal":
                Need(MasterSchema.DecimalText(value, out var amount), key, $"{field.Label} must be a decimal string with up to eight decimal places.");
                return amount;
            case "integer":
                Need(int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var integer) && integer <= 36500,
                    key, $"{field.Label} must be a whole number from 0 to 36,500.");
                return integer;
            case "boolean":
                Need(raw.ValueKind is JsonValueKind.True or JsonValueKind.False, key, $"{field.Label} must be true or false.");
                return raw.GetBoolean();
            case "date":
                Need(DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && date.Year is >= 1900 and <= 2199, key, $"{field.Label} must be an ISO calendar date.");
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case "choice":
                Need(value is not null && field.Choices!.Contains(value), key, $"Select a supported {field.Label.ToLowerInvariant()}.");
                return value!;
            case "reference":
                Need(Guid.TryParse(value, out var id) && id != Guid.Empty, key, $"Select a valid {field.Label.ToLowerInvariant()}.");
                return id;
            case "authority":
                Need(RegulatoryCatalog.Find(value) is not null, key, "Select a listed regulatory reference or leave it blank.");
                return value!;
            case "email":
                Need(ApiSupport.Email(value), key, "Enter a valid email address.");
                return value!;
            case "emails":
            case "list":
            case "record-types":
                var parts = ReadList(raw, key, field.Required);
                Need(field.Type != "emails" || parts.All(ApiSupport.Email), key, "Enter valid unique invoice email addresses.");
                Need(field.Type != "record-types" || parts.All(MasterSchema.RecordTypes.Contains), key, "Choose supported record types.");
                return parts;
            case "components":
                return Components(raw);
            case "custom-values":
                Need(raw.ValueKind == JsonValueKind.Object && raw.EnumerateObject().Count() <= 50, key, "Provide a custom field object with up to 50 fields.");
                return raw;
            case "external":
                Need(raw.ValueKind == JsonValueKind.Array && raw.GetArrayLength() <= 20, key, "Provide up to 20 scoped external references.");
                try { return raw.Deserialize<ExternalInput[]>(Json)!; }
                catch (JsonException) { throw new MasterValidation(key, "An external reference has an invalid format."); }
            default:
                Need(ApiSupport.Text(value, field.MaxLength) && ValidXml(value!), key, $"Enter up to {field.MaxLength} valid characters for {field.Label}.");
                return value!;
        }
    }

    private static string[] ReadList(JsonElement raw, string field, bool required)
    {
        Need(raw.ValueKind == JsonValueKind.Array && raw.GetArrayLength() <= 50, field, "Provide a list of up to 50 values.");
        var items = raw.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()?.Trim() : null).ToArray();
        Need((!required || items.Length > 0) && items.All(x => ApiSupport.Text(x, 254) && !x!.Contains('\0'))
            && items.Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Length, field, "Use valid, unique values.");
        return items!;
    }

    private static object[] Components(JsonElement raw)
    {
        Need(raw.ValueKind == JsonValueKind.Array && raw.GetArrayLength() <= 4, "components", "Provide up to four separate tax components.");
        var list = new List<object>();
        var seen = new HashSet<string>();
        foreach (var component in raw.EnumerateArray())
        {
            var kind = component.Str("kind");
            var basis = component.Str("basis");
            Need(kind is "VAT" or "CreditableWithholding" or "WithholdingVAT" or "Other" && seen.Add(kind), "components", "Use unique supported component types.");
            Need(basis is "NetSales" or "GrossTotal", "components", "Choose a supported calculation base.");
            Need(kind != "VAT" || basis == "NetSales", "components", "VAT calculates from net sales.");
            Need(MasterSchema.DecimalText(component.Str("rate"), out var rate) && rate >= 0 && rate <= 100,
                "components", "Enter each tax rate as a decimal percentage from 0 to 100.");
            var reference = component.Str("reference");
            Need(reference is null || ApiSupport.Text(reference, 240), "components", "Use a source reference of at most 240 characters.");
            list.Add(new { kind, basis, rate, reference });
        }
        return list.ToArray();
    }

    private async Task ValidateRelations(Guid subscriber, Guid company, MasterDefinition definition, JsonElement data, Guid? current, CancellationToken ct)
    {
        var fields = definition.Fields.Where(x => x.Reference is not null && data.Ref(x.Key) is not null).ToArray();
        var ids = fields.Select(x => data.Ref(x.Key)!.Value).Distinct().ToArray();
        var targets = await Scope(subscriber, company).Where(x => ids.Contains(x.Id) && x.Active).ToDictionaryAsync(x => x.Id, ct);
        foreach (var field in fields)
            Need(targets.TryGetValue(data.Ref(field.Key)!.Value, out var match) && match.Kind == field.Reference,
                field.Key, $"Select an active {field.Label.ToLowerInvariant()} from this company.");
        if (definition.Key == "items" && data.Ref("account") is Guid account)
            Need(targets[account].Data.Str("dimensionType") == "AccountReference", "account", "Select an accounting account reference.");
        if (definition.Key == "item-units" && data.Ref("item") is Guid item && data.Ref("unit") is Guid unit)
        {
            Need(targets[item].Data.Ref("unit") != unit, "unit", "The base unit already has a conversion factor of one.");
            await RequireUniqueCombination(subscriber, company, definition.Key, current, new { item, unit }, "unit", ct);
        }
        if (definition.Key == "exchange-rates")
        {
            var currency = data.Ref("currency");
            var date = data.Str("date");
            await RequireUniqueCombination(subscriber, company, definition.Key, current, new { currency, date }, "date", ct);
        }
        if (definition.Key == "price-entries")
        {
            Need(data.Ref("customer") is null || data.Ref("customerGroup") is null, "customer", "Choose a customer or customer group for this entry.");
            var itemId = data.Ref("item")!.Value;
            var unitId = data.Ref("unit")!.Value;
            if (targets[itemId].Data.Ref("unit") != unitId)
                Need(await Scope(subscriber, company).AnyAsync(x => x.Kind == "item-units" && x.Active &&
                    EF.Functions.JsonContains(x.Data, JsonSerializer.Serialize(new { item = itemId, unit = unitId })), ct),
                    "unit", "Define an active conversion before pricing an alternate unit.");
            var criteria = new { priceList = data.Ref("priceList"), item = itemId, unit = unitId };
            var candidates = await Scope(subscriber, company).Where(x => x.Kind == definition.Key && x.Id != current && x.Active &&
                EF.Functions.JsonContains(x.Data, JsonSerializer.Serialize(criteria))).ToArrayAsync(ct);
            Need(!candidates.Any(x => x.Data.Ref("customer") == data.Ref("customer") && x.Data.Ref("customerGroup") == data.Ref("customerGroup")
                && x.Data.Num("minimumQuantity") == data.Num("minimumQuantity") && x.Data.Day("effectiveFrom") == data.Day("effectiveFrom")),
                "minimumQuantity", "An entry already defines this price tier and effective date.");
        }
    }

    private async Task RequireUniqueCombination(Guid subscriber, Guid company, string kind, Guid? current, object values, string field, CancellationToken ct)
    {
        var criteria = JsonSerializer.Serialize(values);
        Need(!await Scope(subscriber, company).AnyAsync(x => x.Kind == kind && x.Id != current && EF.Functions.JsonContains(x.Data, criteria), ct),
            field, "This combination already exists. Edit the existing record.");
    }

    private static void ValidateKind(string kind, JsonElement data)
    {
        foreach (var key in new[] { "price", "minimumQuantity", "amount", "maximum" })
            if (key != "maximum" || kind != "custom-fields") Need(data.Num(key) >= 0, key, "Use a non-negative value.");
        if (kind == "item-units") Need(data.Num("factor") > 0, "factor", "The conversion factor must be positive.");
        if (kind == "exchange-rates") Need(data.Num("rate") > 0, "rate", "Supply a positive authorized exchange rate.");
        if (kind == "currencies") Need(data.Num("precision") <= 6, "precision", "Choose from zero to six decimal places.");
        if (kind == "payment-terms" && data.Str("method") == "Immediate")
            Need(data.Num("days") == 0, "days", "Immediate terms use zero days.");
        if (kind == "discount-groups")
        {
            Need(data.Str("method") != "Percentage" || data.Num("amount") <= 100, "amount", "Percentage discounts cannot exceed 100%.");
            Need(data.Str("category") != "Commercial" || data.Str("taxTreatment") == "ReduceBase",
                "taxTreatment", "Commercial discounts can only reduce the calculation base.");
        }
        if (kind == "tax-groups")
        {
            var vat = data.Array("components").SingleOrDefault(x => x.Str("kind") == "VAT");
            Need(data.Str("classification") == "VATable" ? vat.ValueKind != JsonValueKind.Undefined && vat.Num("rate") > 0
                : vat.ValueKind == JsonValueKind.Undefined || vat.Num("rate") == 0, "components", "The VAT component must match the tax classification.");
            Need(data.Str("treatment") != "Inclusive" || data.Str("classification") == "VATable",
                "treatment", "Inclusive VAT requires a VATable classification.");
        }
        if (kind == "custom-fields")
        {
            Need(data.Num("maximumLength", 240) is >= 1 and <= 2000, "maximumLength", "Use a text limit from 1 to 2,000 characters.");
            Need(data.Str("valueType") != "Dropdown" || data.Array("values").Length > 0, "values", "Define allowed dropdown values.");
            Need(data.Str("valueType") != "Reference" || data.Str("referenceType") is not null, "referenceType", "Choose a reference type.");
            Need(!data.TryGetProperty("maximum", out _) || !data.TryGetProperty("minimum", out _) || data.Num("minimum") <= data.Num("maximum"),
                "maximum", "The maximum must be at least the minimum.");
        }
    }

    internal async Task<Dictionary<string, object>> CustomValues(Guid subscriber, Guid company, string kind, JsonElement data, CancellationToken ct)
    {
        var fields = await Scope(subscriber, company).Where(x => x.Kind == "custom-fields" && x.Active).ToArrayAsync(ct);
        var applicable = fields.Where(x => x.Data.Array("recordTypes").Any(y => y.GetString() == kind)).ToDictionary(x => x.Code);
        var supplied = data.TryGetProperty("custom", out var custom) ? custom.EnumerateObject().ToDictionary(x => x.Name, x => x.Value) : [];
        Need(supplied.Keys.All(applicable.ContainsKey), "custom", "One or more custom fields do not apply to this record.");
        var result = new Dictionary<string, object>();
        foreach (var (code, field) in applicable)
        {
            if (!supplied.TryGetValue(code, out var value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.GetString() == "")
            {
                if (field.Data.Str("default") is string defaultValue) value = JsonSerializer.SerializeToElement(defaultValue);
                else
                {
                    Need(!field.Data.Flag("required"), "custom." + code, $"{field.Name} is required.");
                    continue;
                }
            }
            try { result[code] = await EnforceCustom(subscriber, company, field.Data, value, ct); }
            catch (MasterValidation error) { throw new MasterValidation("custom." + code, $"{field.Name}: {error.Message}"); }
        }
        return result;
    }

    private async Task<object> EnforceCustom(Guid subscriber, Guid company, JsonElement data, JsonElement value, CancellationToken ct)
    {
        var type = data.Str("valueType");
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        switch (type)
        {
            case "Boolean":
                Need(value.ValueKind is JsonValueKind.True or JsonValueKind.False || bool.TryParse(text, out _), "default", "Use true or false.");
                return value.ValueKind == JsonValueKind.True || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
            case "Integer":
            case "Decimal":
                Need(MasterSchema.DecimalText(text, out var number) && (type != "Integer" || decimal.Truncate(number) == number)
                    && (!data.TryGetProperty("minimum", out _) || number >= data.Num("minimum"))
                    && (!data.TryGetProperty("maximum", out _) || number <= data.Num("maximum")), "default", "Use a number in the allowed range.");
                return number;
            case "Date":
                Need(DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "default", "Use an ISO date.");
                break;
            case "Dropdown":
                Need(data.Array("values").Any(x => x.GetString() == text), "default", "Choose an allowed value.");
                break;
            case "Reference":
                Need(Guid.TryParse(text, out var id) && await Scope(subscriber, company).AnyAsync(x => x.Id == id && x.Active
                    && x.Kind == data.Str("referenceType"), ct), "default", "Select an authorized company reference.");
                break;
            default:
                Need(ApiSupport.Text(text, (int)data.Num("maximumLength", 240)) && !text!.Contains('\0'), "default", "Use text within the configured limit.");
                break;
        }
        return text!;
    }

    private async Task SetExternal(Guid subscriber, Guid company, string kind, Guid record, ExternalInput[] input, CancellationToken ct)
    {
        var normalized = new List<ExternalMasterReference>();
        foreach (var item in input)
        {
            Need(item is not null && ApiSupport.Text(item.Connector, 50) && ApiSupport.Text(item.ExternalAccount, 120)
                && ApiSupport.Text(item.ExternalId, 120) && ApiSupport.Optional(item.ExternalCode, 120)
                && new[] { item.Connector, item.ExternalAccount, item.ExternalId, item.ExternalCode }.All(x => x is null || !x.Contains('\0'))
                && item.RecordType == kind, "external", "Every external reference needs the connector, account, correct record type and stable external ID.");
            normalized.Add(new ExternalMasterReference { SubscriberId = subscriber, CompanyId = company, RecordId = record,
                Connector = item!.Connector!.Trim().ToLowerInvariant(), ExternalAccount = item.ExternalAccount!.Trim(), RecordType = kind,
                ExternalId = item.ExternalId!.Trim(), ExternalCode = item.ExternalCode?.Trim(), SourceAt = item.SourceAt?.ToUniversalTime() });
        }
        static (string, string, string) Key(ExternalMasterReference item) => (item.Connector, item.ExternalAccount, item.ExternalId);
        Need(normalized.Select(Key).Distinct().Count() == normalized.Count, "external", "Duplicate external record identifiers.");
        var importedIds = normalized.Select(x => x.ExternalId).Distinct().ToArray();
        var prior = await db.ExternalMasterReferences.Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.RecordType == kind
            && (x.RecordId == record || importedIds.Contains(x.ExternalId))).ToArrayAsync(ct);
        foreach (var item in normalized)
        {
            var match = prior.Concat(db.ExternalMasterReferences.Local).FirstOrDefault(x => Key(x) == Key(item)
                && x.SubscriberId == subscriber && x.CompanyId == company && x.RecordType == kind && db.Entry(x).State != EntityState.Deleted);
            Need(match is null || match.RecordId == record, "external", "An external record already belongs to another record in this company.");
            if (match is null) db.ExternalMasterReferences.Add(item);
            else { match.ExternalCode = item.ExternalCode; match.SourceAt = item.SourceAt; }
        }
        db.ExternalMasterReferences.RemoveRange(prior.Where(x => x.RecordId == record && normalized.All(item => Key(item) != Key(x))));
    }

    private static bool ValidXml(string value)
    {
        try { System.Xml.XmlConvert.VerifyXmlChars(value); return true; }
        catch (System.Xml.XmlException) { return false; }
    }

    internal static void Need(bool condition, string field, string message)
    {
        if (!condition) throw new MasterValidation(field, message);
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_.-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex Code();
    [GeneratedRegex(@"^(?:[0-9]{9}|[0-9]{3}-[0-9]{3}-[0-9]{3})(?:-?[0-9]{3,5})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Tin();
}

