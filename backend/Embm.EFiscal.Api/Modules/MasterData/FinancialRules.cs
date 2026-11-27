using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;

namespace Embm.EFiscal.Api.Modules.MasterData;

internal record ResolvedRule(MasterRecord Record, MasterRevision Revision)
{
    public JsonElement Data => Revision.Data;
    public RuleSnapshot Snapshot() => new(Record.Id, Record.Code, Revision.Name, Revision.Revision, Revision.EffectiveFrom,
        Data.Str("authority"), MasterRegistry.SafeData(Data));
}

internal sealed class EffectiveRules(EfiscalDbContext db, IQueryable<MasterRecord> scope, DateOnly date)
{
    private readonly Dictionary<Guid, ResolvedRule> cache = [];

    public async Task<ResolvedRule> Find(Guid id, string kind, string field, CancellationToken ct)
    {
        if (!cache.TryGetValue(id, out var match))
        {
            // Select by date before evaluating activity or end date, so an expired revision does not revive an earlier rule.
            var record = await scope.SingleOrDefaultAsync(x => x.Id == id && x.Kind == kind, ct);
            Need(record is not null, field, "Choose a rule from this company.");
            var revision = await MasterRegistry.EffectiveRevision(db, record!, date, ct);
            Need(revision is not null,
                field, $"No active version of {record!.Code} applies on the selected date.");
            match = new(record!, revision!);
            cache[id] = match;
        }
        Need(match.Record.Kind == kind, field, "Choose a rule of the applicable type.");
        return match;
    }

    public async Task<ResolvedRule[]> Discounts(Guid[] ids, CancellationToken ct)
    {
        Need(ids.Length <= 20 && ids.Length == ids.Distinct().Count(), "discounts", "Select up to 20 distinct discounts.");
        var list = new List<ResolvedRule>();
        foreach (var id in ids) list.Add(await Find(id, "discount-groups", "discounts", ct));
        return list.OrderBy(x => x.Data.Num("priority")).ThenBy(x => x.Record.Code).ToArray();
    }
}

internal static class FinancialRules
{
    public static TaxTreatment Tax(ResolvedRule selected, MasterRecord item, MasterRecord? group, EligibilityProof? eligibility, DateOnly date, string seller)
    {
        var rule = selected.Data;
        var classification = rule.Str("classification")!;
        var authority = RegulatoryCatalog.Find(rule.Str("authority"));
        Need(authority is { VerificationStatus: "VerifiedScope" } && rule.Flag("companyAuthorized") && date >= authority.SupportedFrom &&
            RegulatoryCatalog.Supports("tax-groups", rule, authority.Id), "taxGroup",
            "Select a company-authorized, verified tax rule that matches this classification and date.");
        var components = rule.Array("components").Select(x => new TaxComponent(x.Str("kind")!, x.Str("basis")!, x.Num("rate"))).ToArray();
        Need(classification is not ("VATable" or "ZeroRated") || seller == "VAT", "taxGroup", "This treatment requires a configured VAT-registered seller.");
        Need(classification != "NonVAT" || seller == "NonVAT", "taxGroup", "This treatment requires a configured non-VAT seller.");
        Need(Matches(rule.Ref("category"), item.Data.Ref("category")) && Matches(rule.Ref("customerGroup"), group?.Id),
            "taxGroup", "This tax group does not apply to the selected item or customer group.");
        if (authority!.Id == RegulatoryCatalog.Books)
            Need(rule.Ref("category").HasValue && eligibility is { Category: "CoveredPublication", ItemCovered: true, SupportingEvidenceChecked: true },
                "eligibility", "Verify this publication satisfies Section 109(R) and use its authorized item category.");
        if (authority.Id == RegulatoryCatalog.Export)
            Need(rule.Ref("category").HasValue && item.Data.Str("itemType") == "Goods" &&
                eligibility is { Category: "DirectExport", ItemCovered: true, SupportingEvidenceChecked: true },
                "eligibility", "Confirm the goods, actual export and qualifying accounted proceeds using supporting documents.");
        return new(classification, rule.Str("treatment") == "Inclusive", rule.Str("rounding") == "HalfEven"
            ? MidpointRounding.ToEven : MidpointRounding.AwayFromZero, components, selected.Snapshot());
    }

    public static void ApplyLine(CalculationLine line, ResolvedRule selected, EligibilityProof? proof, MasterRecord? customer,
        MasterRecord? group, DateOnly date, int precision)
    {
        var rule = selected.Data;
        Need(rule.Str("scope") == "Line", "discounts", "Use document discounts at document level.");
        Need(Applies(rule, line, customer, group), "discounts", "A selected discount is outside its eligibility limits.");
        CanCombine(line, rule, "discounts");
        if (rule.Str("category") != "Commercial")
        {
            Statutory(line, selected, proof, customer, date, precision);
            return;
        }
        var amount = Discount(rule, line.Remaining, precision, line.Tax.Rounding);
        line.Discounts.Add(new(selected.Snapshot(), rule.Str("combination")!, amount, line.Remaining, "Line"));
        line.Remaining -= amount;
    }

    public static void ApplyDocument(IReadOnlyList<CalculationLine> lines, ResolvedRule selected, MasterRecord? customer, MasterRecord? group, int precision)
    {
        var rule = selected.Data;
        Need(rule.Str("scope") == "Document" && rule.Str("category") == "Commercial", "documentDiscounts",
            "Select a commercial discount configured at document level.");
        var eligible = lines.Where(line => Applies(rule, line, customer, group)).ToArray();
        Need(eligible.Length > 0, "documentDiscounts", "No lines meet this document discount's conditions.");
        foreach (var line in eligible) CanCombine(line, rule, "documentDiscounts");
        var bases = eligible.Select(line => line.Remaining).ToArray();
        var amount = Discount(rule, bases.Sum(), precision, MidpointRounding.AwayFromZero);
        var allocated = FinancialMath.Allocate(amount, bases, precision);
        for (var index = 0; index < eligible.Length; index++)
        {
            eligible[index].Discounts.Add(new(selected.Snapshot(), rule.Str("combination")!, allocated[index], bases[index], "Document"));
            eligible[index].Remaining -= allocated[index];
        }
    }

    private static void Statutory(CalculationLine line, ResolvedRule selected, EligibilityProof? proof,
        MasterRecord? customer, DateOnly date, int precision)
    {
        var rule = selected.Data;
        var authority = RegulatoryCatalog.Find(rule.Str("authority"));
        var category = rule.Str("category");
        Need(authority is { VerificationStatus: "VerifiedScope" } && date >= authority.SupportedFrom && rule.Flag("companyAuthorized")
            && RegulatoryCatalog.Supports("discount-groups", rule, authority.Id)
            && line.Item.Data.Str("itemType") == "Goods" && line.Tax.Classification is "VATable" or "NonVAT",
            "discounts", "Choose an authorized statutory medicine rule with the verified rate, conditions and tax treatment.");
        Need(customer?.Data.Str("classification") == "Individual" &&
            proof is { IdentityChecked: true, ExclusiveUse: true, ItemCovered: true, SupportingEvidenceChecked: true, CompetingPromotion: false }
            && proof.Category == category, "eligibility",
            "Verify the individual, qualifying medicine, exclusive personal use and applicable evidence. Review any competing promotion separately.");
        var quantity = proof!.Quantity is null ? line.Quantity : Quantity(proof.Quantity);
        Need(quantity <= line.Quantity && quantity >= rule.Num("minimumQuantity"), "eligibility",
            "Use a qualifying quantity within the line quantity and configured eligibility.");
        var rounding = line.Tax.Rounding;
        line.StatutoryPortion = FinancialMath.Round(line.Remaining * (quantity / line.Quantity), precision, rounding);
        var divisor = line.Tax.Inclusive ? 1.12m : 1m;
        line.StatutoryBase = FinancialMath.Round(line.StatutoryPortion / divisor, precision, rounding);
        var reduction = FinancialMath.Discount(line.StatutoryBase, 20m, "Percentage", null, precision, rounding);
        line.Discounts.Add(new(selected.Snapshot(), "Exclusive", reduction, line.StatutoryBase, "Statutory"));
    }

    private static void CanCombine(CalculationLine line, JsonElement rule, string field) =>
        Need(line.Discounts.Count == 0 || rule.Str("combination") == "Stackable" && line.Discounts.All(x => x.Combination == "Stackable"),
            field, "The selected discounts cannot be combined on a line.");
    private static decimal Discount(JsonElement rule, decimal basis, int precision, MidpointRounding rounding) =>
        FinancialMath.Discount(basis, rule.Num("amount"), rule.Str("method")!, OptionalAmount(rule, "maximum"), precision, rounding);
    private static bool Applies(JsonElement rule, CalculationLine line, MasterRecord? customer, MasterRecord? group) =>
        Matches(rule.Ref("item"), line.Item.Id) && Matches(rule.Ref("itemCategory"), line.Item.Data.Ref("category"))
        && Matches(rule.Ref("customer"), customer?.Id) && Matches(rule.Ref("customerGroup"), group?.Id)
        && line.Quantity >= rule.Num("minimumQuantity");
    private static bool Matches(Guid? condition, Guid? id) => condition is null || condition == id;
    private static decimal? OptionalAmount(JsonElement data, string key) => data.TryGetProperty(key, out _) ? data.Num(key) : null;

    public static decimal Quantity(string? value)
    {
        Need(MasterSchema.DecimalText(value, out var quantity) && quantity > 0 && quantity <= 1_000_000_000m,
            "quantity", "Enter a positive decimal quantity up to one billion.");
        return quantity;
    }
}

