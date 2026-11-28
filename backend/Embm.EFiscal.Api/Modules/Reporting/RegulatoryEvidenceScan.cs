using System.Text.Json;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal static class RegulatoryEvidenceScan
{
    private static bool IsFixture(HttpContext http, bool synthetic) => synthetic &&
        http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();

    internal static string Inspect(HttpContext http, bool synthetic, byte[] bytes, string contentType)
    {
        if (!IsFixture(http, synthetic)) return "ScannerUnavailable";
        if (contentType == "application/json")
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            if (json.RootElement.TryGetProperty("syntheticScanOutcome", out var outcome) && outcome.ValueKind == JsonValueKind.String &&
                outcome.GetString() is "Pending" or "Suspicious" or "Quarantined" or "Failed" or "ScannerUnavailable")
                return outcome.GetString()!;
        }
        return "DevelopmentClean";
    }

    internal static bool Allows(HttpContext http, bool synthetic, string status) =>
        status == "DevelopmentClean" && IsFixture(http, synthetic);
}
