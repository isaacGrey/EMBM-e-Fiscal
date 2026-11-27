using System.Text.Json;

namespace Embm.EFiscal.Api.Modules.MasterData;

internal record RegulatoryRule(string Id, string Label, string Authority, string Source, DateOnly SupportedFrom,
    string Version, string VerificationStatus, string Restrictions);

internal static class RegulatoryCatalog
{
    public const string Vat = "PH-NIRC-STANDARD-VAT";
    public const string Books = "PH-109R-BOOKS";
    public const string Export = "PH-EXPORT-GOODS";
    public const string NonVat = "PH-NONVAT-SELLER";
    public const string Senior = "PH-SC-MEDICINE";
    public const string Pwd = "PH-PWD-MEDICINE";
    public static readonly RegulatoryRule[] Rules =
    [
        new(Vat, "Standard domestic VAT", "Bureau of Internal Revenue",
            "https://bir-cdn.bir.gov.ph/BIR/pdf/RR%203-2024%20%28final%29.pdf", new(2024, 4, 27), "NIRC-106-108/EOPT",
            "VerifiedScope", "Ordinary domestic sales by a VAT-registered seller, subject to the applicable NIRC exceptions."),
        new(Books, "Section 109(R) qualifying books", "Philippine Congress / Bureau of Internal Revenue",
            "https://bir-cdn.bir.gov.ph/local/pdf/RMC%20No.%2089-2021%20RA%2011534.pdf", new(2021, 4, 11), "RA-11534/109-R",
            "VerifiedScope", "Covered books and qualifying publications that are not primarily devoted to paid advertisements. Authorize the item category and attest applicability."),
        new(Export, "Direct export of goods", "Bureau of Internal Revenue",
            "https://bir-cdn.bir.gov.ph/BIR/pdf/RR%2010-2025.pdf", new(2025, 4, 1), "RR-10-2025/106-A-2-a-1",
            "VerifiedScope", "VAT-registered seller, actual shipment from the Philippines to a foreign country, acceptable foreign currency proceeds accounted for under BSP rules. Applies to the narrow direct export scenario."),
        new(NonVat, "Configured non-VAT seller", "Bureau of Internal Revenue",
            "https://bir-cdn.bir.gov.ph/BIR/pdf/RR%203-2024%20%28final%29.pdf", new(2024, 4, 27), "NIRC-105/116-EOPT",
            "VerifiedScope", "Reflects configured NonVAT seller status. Percentage tax and other taxes require separate review."),
        new(Senior, "Senior citizen qualifying medicine", "Philippine Congress / Bureau of Internal Revenue",
            "https://www.lawphil.net/statutes/repacts/ra2010/ra_9994_2010.html", new(2022, 5, 18), "RA-9994/RMC-71-2022",
            "VerifiedScope", "20% on the VAT-exclusive price of qualifying medicine for exclusive personal use, with documented eligibility and proof. A separate permitted promotion may be more favorable."),
        new(Pwd, "PWD qualifying medicine", "Philippine Congress / Bureau of Internal Revenue",
            "https://lawphil.net/statutes/repacts/ra2016/ra_10754_2016.html", new(2022, 5, 18), "RA-10754/RMC-71-2022",
            "VerifiedScope", "20% on the VAT-exclusive price of qualifying medicine for exclusive personal use, with documented eligibility and proof. One statutory concession applies."),
        new("PH-SOLO-PARENT", "Solo parent", "Philippine Congress / Bureau of Internal Revenue",
            "https://www.bir.gov.ph/2023-Revenue-Regulations", new(2023, 1, 1), "RR-1-2023", "ReferenceOnly",
            "Product, child age, income, identification, and booklet rules require a dedicated eligibility workflow."),
        new("PH-ATHLETE-COACH", "National athlete or coach", "Philippine Congress",
            "https://www.lawphil.net/statutes/repacts/ra2015/ra_10699_2015.html", new(2015, 11, 13), "RA-10699", "ReferenceOnly",
            "Eligibility, covered goods or services, and related tax treatment require a dedicated rule."),
        new("PH-WITHHOLDING", "Philippine withholding reference", "Bureau of Internal Revenue",
            "https://bir-cdn.bir.gov.ph/BIR/pdf/RR%20No.%2024-2025.pdf", new(2025, 11, 1), "RR-24-2025", "ReferenceOnly",
            "Payer, payment type, threshold, rate and base require a separately verified scenario.")
    ];

    public static RegulatoryRule? Find(string? id) => Rules.SingleOrDefault(x => x.Id == id);

    public static string Status(string kind, JsonElement data)
    {
        if (kind == "discount-groups" && data.Str("category") == "Commercial") return "Commercial";
        var source = Find(data.Str("authority"));
        if (source is not { VerificationStatus: "VerifiedScope" }) return "Unverified";
        if (!Supports(kind, data, source.Id)) return "ConfigurationRequiresReview";
        return data.Flag("companyAuthorized") ? "AuthorizedForPreview" : "CompanyAuthorizationRequired";
    }

    public static bool Supports(string kind, JsonElement data, string source)
    {
        if (kind == "tax-groups")
        {
            var classification = data.Str("classification");
            var components = data.Array("components");
            if (components.Any(x => x.Str("kind") != "VAT")) return false;
            return source switch
            {
                Vat => classification == "VATable" && components.Length == 1 && components[0].Num("rate") == 12m && components[0].Str("basis") == "NetSales",
                Books => classification == "VATExempt" && data.Ref("category").HasValue,
                Export => classification == "ZeroRated" && data.Ref("category").HasValue,
                NonVat => classification == "NonVAT",
                _ => false
            };
        }
        return kind == "discount-groups" && (data.Str("category"), source) is ("SeniorCitizen", Senior) or ("PWD", Pwd)
            && data.Str("method") == "Percentage" && data.Num("amount") == 20m && data.Str("taxTreatment") == "VATExempt"
            && data.Str("combination") == "Exclusive" && data.Str("scope") == "Line" && !data.TryGetProperty("maximum", out _)
            && (data.Ref("item").HasValue || data.Ref("itemCategory").HasValue);
    }
}

