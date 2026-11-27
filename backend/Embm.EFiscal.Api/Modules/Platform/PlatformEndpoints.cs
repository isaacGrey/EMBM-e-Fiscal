namespace Embm.EFiscal.Api.Modules.Platform;

public sealed record HealthResponse(string Status, string Service);

public static class PlatformEndpoints
{
    public static void MapPlatform(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health", (Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService checks, CancellationToken ct) =>
        {
            return CheckHealthAsync(checks, ct);
        })
        .AllowAnonymous()
        .WithName("GetHealth")
        .WithTags("Platform")
        .WithSummary("Checks whether the API process is healthy")
        .Produces<HealthResponse>()
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService checks, CancellationToken ct)
    {
        var report = await checks.CheckHealthAsync(ct);
        return report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
            ? TypedResults.Ok(new HealthResponse("healthy", "embm-efiscal-api"))
            : TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "API is unavailable");
    }
}
