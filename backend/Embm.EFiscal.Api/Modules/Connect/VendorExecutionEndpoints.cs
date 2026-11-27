using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record ApproveOperation(Guid ConnectorVersionId, string Capability, string Method, string Path, string Authentication, int TimeoutMs, int PageLimit);
internal sealed record SandboxInput(Guid PolicyId, string? Scenario, Dictionary<string, string>? PathParameters, int? Page, JsonElement? Body, string? Sandbox,
    Dictionary<string, string>? Query = null, Dictionary<string, string>? Headers = null);

internal static partial class VendorConnectEndpoints
{
    private static void MapVendorExecution(RouteGroupBuilder group)
    {
        group.MapGet("/runtime/options", RuntimeOptions).AddEndpointFilter(Grant(View));
        group.MapPost("/runtime/policies", ApprovePolicy).AddEndpointFilter(Grant(Publish));
        group.MapPost("/runtime/test", TestApproved).AddEndpointFilter(Grant(Publish)).RequireRateLimiting("connector-operation");
        group.MapGet("/runtime/monitor", RuntimeMonitor).AddEndpointFilter(Grant(View));
    }

    private static async Task<IResult> RuntimeOptions(EfiscalDbContext db, CancellationToken ct)
    {
        var versions = await db.ConnectorVersions.AsNoTracking().Include(x => x.Connector).Where(x => x.State == "Published" &&
            !x.Connector.Disabled && x.Adapter == "mock-accounting").OrderBy(x => x.Connector.Code).ThenByDescending(x => x.Number).Take(100).ToArrayAsync(ct);
        var ids = versions.Select(x => x.Id).ToArray();
        var policies = await db.ExecutionPolicies.AsNoTracking().Where(x => ids.Contains(x.ConnectorVersionId)).OrderBy(x => x.Path).Take(200).ToArrayAsync(ct);
        return Results.Ok(new { versions = versions.Select(v => new { v.Id, v.Number, configuration = Read(v), specification = v.Specification }),
            policies, scenarios = ControlledExecution.Scenarios, sandbox = "Isolated development accounting" });
    }

    private static async Task<IResult> ApprovePolicy(ApproveOperation input, EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        var version = await db.ConnectorVersions.AsNoTracking().Include(x => x.Connector).SingleOrDefaultAsync(x => x.Id == input.ConnectorVersionId &&
            x.State == "Published" && !x.Connector.Disabled && x.Adapter == "mock-accounting", ct);
        if (version is null) return Missing();
        var config = Read(version);
        Require(ControlledExecution.Auth.Contains(input.Authentication) && config.Capabilities.Any(x => x.Code == input.Capability && x.Verification == "Development Verified")
            && AccountingMock.Contains(input.Capability) && config.Authentication.Contains(input.Authentication), "Select a capability and authentication declared by this published sandbox version.");
        var review = version.Specification?.Deserialize<SpecReview>(SpecImport.Json);
        var operation = review?.Endpoints.SingleOrDefault(x => x.Method == input.Method && x.Path == input.Path);
        Require(operation is not null && review!.Servers.Contains(ControlledExecution.Origin), "Select a reviewed operation for the approved synthetic origin.");
        Require(input.Method is "GET" or "POST" && input.TimeoutMs is >= 250 and <= 5000 && input.PageLimit is >= 1 and <= 10,
            "Use an approved GET or POST with a bounded timeout and pagination.");
        var policy = new ExecutionPolicy { ConnectorVersionId = version.Id, Capability = input.Capability, Method = input.Method, Path = input.Path,
            Origin = ControlledExecution.Origin, Authentication = input.Authentication, TimeoutMs = input.TimeoutMs, PageLimit = input.PageLimit,
            RequestSchema = operation!.Request, ResponseSchema = operation.Response, ApprovedBy = http.UserId() };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await Locked(db, version.ConnectorId, ct) is not { Disabled: false }) return Missing();
        if (await db.ExecutionPolicies.AnyAsync(x => x.ConnectorVersionId == version.Id && x.Capability == input.Capability &&
            x.Method == input.Method && x.Path == input.Path, ct)) return Conflict("This operation is already approved.");
        db.ExecutionPolicies.Add(policy);
        await Complete(db, http, "SandboxPolicyApproved", version.ConnectorId, tx, ct);
        return Results.Ok(policy);
    }

    private static async Task<IResult> TestApproved(SandboxInput input, EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        Require(input.Sandbox == "accounting-local", "Select the authorized development sandbox.");
        var operation = await db.ExecutionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.PolicyId, ct);
        if (operation is null) return Missing();
        var version = await db.ConnectorVersions.AsNoTracking().Include(x => x.Connector).SingleAsync(x => x.Id == operation.ConnectorVersionId, ct);
        if (version.State != "Published" || version.Connector.Disabled || version.Adapter != "mock-accounting" ||
            !Read(version).Capabilities.Any(x => x.Code == operation.Capability && x.Verification == "Development Verified")) return Missing();
        var scenario = input.Scenario ?? "Normal";
        Require(ControlledExecution.Scenarios.Contains(scenario), "Use an approved synthetic response status.");
        var id = Guid.NewGuid();
        var review = version.Specification?.Deserialize<SpecReview>(SpecImport.Json);
        var declared = review?.Endpoints.SingleOrDefault(x => x.Method == operation.Method && x.Path == operation.Path)?.Parameters;
        var result = await ControlledExecution.Execute(operation, input.Body, id, (_, cancel) => ControlledExecution.TestResponse(scenario, cancel),
            ct, input.PathParameters, input.Page, declared, input.Query, input.Headers);
        // Isolated responses are generated by the service. Never echo caller-supplied values or raw request headers.
        var diagnostics = Freeze(new { request = ControlledExecution.Preview(operation, id), result.Remaining, result.RetryAfter });
        db.ConnectorTestResults.Add(new ConnectorTestResult { Id = id, PolicyId = operation.Id, ActorId = http.UserId(),
            DurationMs = result.DurationMs, HttpStatus = result.HttpStatus, Status = result.Status, Diagnostics = diagnostics });
        db.Audit("Connectors.SandboxTest." + result.Status, http.UserId(), target: version.ConnectorId);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id, request = ControlledExecution.Preview(operation, id), result, transformedFields = result.Response });
    }

    private static async Task<IResult> RuntimeMonitor(EfiscalDbContext db, CancellationToken ct) => Results.Ok(new
    {
        availability = await db.ConnectorVersions.AsNoTracking().Where(x => x.State == "Published").GroupBy(x => x.Availability)
            .Select(x => new { state = x.Key, count = x.Count() }).ToArrayAsync(ct),
        connections = await db.ExternalConnections.AsNoTracking().GroupBy(x => new { x.Status, x.ConnectorVersionId })
            .Select(x => new { x.Key.Status, x.Key.ConnectorVersionId, count = x.Count() }).ToArrayAsync(ct),
        jobs = await db.DocumentOutboxMessages.AsNoTracking().Where(x => x.Kind == "Accounting").GroupBy(x => x.State)
            .Select(x => new { state = x.Key, count = x.Count() }).ToArrayAsync(ct),
        failures = await db.SyncAttempts.AsNoTracking().Where(x => x.Category != null).GroupBy(x => x.Category)
            .Select(x => new { category = x.Key, count = x.Count() }).ToArrayAsync(ct),
        mappingFailures = await db.MappingVersions.CountAsync(x => !x.Validated, ct),
        rateLimitEvents = await db.ConnectorTestResults.CountAsync(x => x.Status == "RateLimited", ct),
        latestAttempt = await db.SyncAttempts.MaxAsync(x => (DateTimeOffset?)x.CompletedAt, ct),
        reconciliationJobs = await db.ReconciliationJobs.AsNoTracking().GroupBy(x => x.State)
            .Select(x => new { state = x.Key, count = x.Count() }).ToArrayAsync(ct),
        reconciliationFailures = await db.ReconciliationJobs.AsNoTracking().Where(x => x.Failure != null).GroupBy(x => x.Failure)
            .Select(x => new { category = x.Key, count = x.Count() }).ToArrayAsync(ct),
        coverageVersions = await db.CoverageRevisions.CountAsync(ct),
        reconciliationFindings = await db.ReconciliationFindings.AsNoTracking().GroupBy(x => x.Category)
            .Select(x => new { category = x.Key, count = x.Count() }).ToArrayAsync(ct),
        staleVerifications = await db.ReconciliationChecks.CountAsync(x => x.FreshUntil < DateTimeOffset.UtcNow && x.Coverage == "Eligible" && x.Retrieval == "Complete", ct),
        latestReconciliationCompletion = await db.ReconciliationJobs.Where(x => x.State == "Completed").MaxAsync(x => (DateTimeOffset?)x.CompletedAt, ct)
    });
}

