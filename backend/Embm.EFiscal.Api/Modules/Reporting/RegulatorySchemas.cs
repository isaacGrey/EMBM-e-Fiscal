using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class RegulatorySchemaVersion
{
    public required string Version { get; set; }
    public required string Name { get; set; }
    public required string Classification { get; set; }
    public required string Environment { get; set; }
    public required string[] DocumentTypes { get; set; }
    public required string Content { get; set; }
    public required string Sha256 { get; set; }
    public required string Source { get; set; }
    public required string Verification { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public Guid? Reviewer { get; set; }
}

internal static class RegulatorySchemas
{
    internal const string SimulationVersion = "embm-reporting-simulation-v1";
    internal static readonly string[] Reserved = ["FutureOfficialCertification", "FutureOfficialProduction"];

    private static RegulatorySchemaVersion BuiltIn(string version, string name, string classification, string environment, string content)
    {
        using var parsed = JsonDocument.Parse(content);
        var bytes = DocumentRules.CanonicalBytes(parsed.RootElement);
        return new()
        {
            Version = version, Name = name, Classification = classification, Environment = environment,
            DocumentTypes = ["SalesInvoice", "CreditMemo"], Content = Encoding.UTF8.GetString(bytes),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), Source = "EMBM eFiscal software release",
            Verification = "InternalOnly", EffectiveFrom = new DateOnly(2026, 11, 27)
        };
    }

    internal static void Configure(ModelBuilder model)
    {
        var schema = model.Entity<RegulatorySchemaVersion>();
        schema.HasKey(x => x.Version);
        schema.Property(x => x.Version).HasMaxLength(80);
        schema.ToTable("RegulatorySchemaVersions", t =>
        {
            t.HasCheckConstraint("CK_RegulatorySchema_Classification", """
                "Classification" IN ('InternalCanonical','DevelopmentSimulation','FutureOfficialCertification','FutureOfficialProduction')
                """);
            t.HasCheckConstraint("CK_RegulatorySchema_Use", """
                ("Classification" = 'InternalCanonical' AND "Environment" = 'Internal' AND "Verification" = 'InternalOnly') OR
                ("Classification" = 'DevelopmentSimulation' AND "Environment" = 'Development' AND "Verification" = 'InternalOnly') OR
                ("Classification" IN ('FutureOfficialCertification','FutureOfficialProduction') AND "Environment" = 'Disabled' AND "Verification" = 'Unverified')
                """);
            t.HasCheckConstraint("CK_RegulatorySchema_Integrity", """
                length("Sha256") = 64 AND ("EffectiveUntil" IS NULL OR "EffectiveUntil" > "EffectiveFrom")
                """);
        });
        schema.HasData(
            BuiltIn(CanonicalInvoices.Version, "EMBM eFiscal canonical invoice", "InternalCanonical", "Internal", """
                {
                  "type": "object",
                  "required": ["schemaVersion", "format", "fiscalSnapshot", "originalFinancialSnapshotSha256",
                               "documentedAuthorizationReferences", "evidenceLimitations"],
                  "properties": {
                    "schemaVersion": {"const": "embm-invoice-v1"},
                    "format": {"const": "InternalCanonical"},
                    "originalFinancialSnapshotSha256": {"type": "string", "pattern": "^[A-F0-9]{64}$"},
                    "documentedAuthorizationReferences": {"type": ["array","null"]},
                    "evidenceLimitations": {"type": "array", "items": {"type": "string"}},
                    "fiscalSnapshot": {
                      "type": "object", "required": ["document","seller","buyer","lines","totals"],
                      "properties": {
                        "document": {"type": "object", "required": ["id","number","type","date","committedAt","currency",
                                                                    "exchangeRate","precision","seriesId","numbering","originalInvoiceId"],
                          "properties": {"type": {"enum": ["SalesInvoice","CreditMemo"]}}},
                        "seller": {"type": "object", "required": ["companyId","branchId","name","branchCode","tin","rdo"]},
                        "buyer": {"type": "object", "required": ["customerId","name","tin","classification"]},
                        "lines": {"type": "array", "minItems": 1,
                          "items": {"type": "object", "required": ["id","position","itemId","unitId","quantity","unitPrice","total",
                                                                     "taxClassification","financialTerms"]}},
                        "totals": {"type": "object", "required": ["grossSales","commercialDiscount","statutoryDiscount","vatableSales",
                          "vatExemptSales","zeroRatedSales","nonVatSales","vat","incomeWithholding","vatWithholding","total","amountDue","rounding"]}
                      }
                    }
                  }
                }
                """),
            BuiltIn(SimulationVersion, "EMBM eFiscal local reporting integrity simulation", "DevelopmentSimulation", "Development", """
                {
                  "type": "object", "required": ["id","outcome","governmentStatus"],
                  "properties": { "id": {"type": "string","format": "uuid"},
                    "outcome": {"const": "SimulationValidated"}, "governmentStatus": {"const": "NotSubmitted"} }
                }
                """));
    }

    internal static void MapRegulatorySchemas(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapGet("/api/vendor/regulatory-schemas", Register).RequireAuthorization("vendor")
            .AddEndpointFilter(ApiSupport.Protect).WithTags("Regulatory schema registry");
    }

    private static async Task<IResult> Register(HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!await db.VendorConnectorGrants.AnyAsync(x => x.UserId == http.UserId() && x.Permission == VendorAccreditation.View, ct))
            return ApiSupport.Problem(403, "Regulatory schema view permission is required.");
        var versions = await db.Set<RegulatorySchemaVersion>().AsNoTracking().OrderBy(x => x.Classification)
            .ThenByDescending(x => x.EffectiveFrom).ThenBy(x => x.Version).ToArrayAsync(ct);
        if (versions.Any(x => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x.Content))) != x.Sha256))
            return ApiSupport.Problem(409, "Regulatory schema checksum verification failed.");
        return Results.Ok(new { versions, reserved = Reserved, officialValidationAvailable = false });
    }
}
