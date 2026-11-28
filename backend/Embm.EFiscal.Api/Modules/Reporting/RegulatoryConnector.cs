using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class RegulatoryConnectorVersion
{
    public int Number { get; set; }
    public required string Environment { get; set; }
    public required string ApiVersion { get; set; }
    public string? Documentation { get; set; }
    public string? Origin { get; set; }
    public required string AuthenticationAdapter { get; set; }
    public JsonElement Specification { get; set; }
    public int TimeoutSeconds { get; set; }
    public int Attempts { get; set; }
    public int RateLimit { get; set; }
    public bool Batch { get; set; }
    public Guid Actor { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class RegulatoryConnectorEvent
{
    public int Version { get; set; }
    public int Sequence { get; set; }
    public required string State { get; set; }
    public required string Reason { get; set; }
    public Guid Actor { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class RegulatoryConnectorTest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; }
    public Guid Actor { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public required string Scenario { get; set; }
    public required string PayloadHash { get; set; }
    public required string ResponseHash { get; set; }
    public JsonElement Response { get; set; }
}

internal static class RegulatoryConnector
{
    public const string View = "Vendor.BirConnector.View";
    public const string ConfigureGrant = "Vendor.BirConnector.Configure";
    public const string Review = "Vendor.BirConnector.Review";
    public const string Activate = "Vendor.BirConnector.Activate";
    public const string Test = "Vendor.BirConnector.Test";
    internal static readonly string[] Grants = [View, ConfigureGrant, Review, Activate, Test];
    internal const string SyntheticOrigin = "https://bir-simulator.example.test";
    internal const string MappingVersion = "embm-development-regulatory-envelope-v1";
    private static readonly Dictionary<string, string> Methods = new(StringComparer.Ordinal)
    {
        ["/sim/submissions"] = "POST", ["/sim/status"] = "GET", ["/sim/acknowledgments"] = "GET"
    };
    internal static readonly string[] Scenarios = ["Accepted", "Processing", "DelayedAcknowledgment", "ValidationRejected",
        "PartialBatch", "RateLimited", "AuthenticationFailed", "TemporaryOutage", "TimeoutBeforeSubmission",
        "TimeoutAfterAcceptance", "Duplicate", "InvalidResponse", "StatusUnavailable"];
    internal sealed record Configuration(string Environment, string ApiVersion, string? Documentation, string? Origin,
        int TimeoutSeconds, int Attempts, int RateLimit, bool Batch, int ExpectedVersion, JsonElement? Manual);
    internal sealed record Transition(string State, int ExpectedSequence, string Reason);
    internal sealed record TestInput(string Scenario);

    internal static void Configure(ModelBuilder model)
    {
        model.Entity<RegulatoryConnectorVersion>().HasKey(x => x.Number);
        model.Entity<RegulatoryConnectorVersion>().Property(x => x.Number).ValueGeneratedNever();
        model.Entity<RegulatoryConnectorEvent>().HasKey(x => new { x.Version, x.Sequence });
        model.Entity<RegulatoryConnectorEvent>().HasOne<RegulatoryConnectorVersion>().WithMany().HasForeignKey(x => x.Version).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RegulatoryConnectorTest>().HasOne<RegulatoryConnectorVersion>().WithMany().HasForeignKey(x => x.Version).OnDelete(DeleteBehavior.Restrict);
    }

    internal static void MapRegulatoryConnector(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        var group = app.MapGroup("/api/vendor/bir-connector").RequireAuthorization("vendor").AddEndpointFilter(ApiSupport.Protect);
        group.MapGet("/", Register);
        group.MapPost("/", Create).RequireRateLimiting("connector-operation");
        group.MapPost("/{number:int}/transitions", Change);
        group.MapPost("/{number:int}/tests", RunTest).RequireRateLimiting("connector-operation");
    }

    private static async Task<IResult> Register(HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var actor = http.UserId();
        if (!await VendorRegulatoryReview.Has(db, actor, View, ct)) return Results.NotFound();
        var versions = await db.Set<RegulatoryConnectorVersion>().AsNoTracking().OrderByDescending(x => x.Number).Take(50).ToArrayAsync(ct);
        var numbers = versions.Select(x => x.Number).ToArray();
        var events = await db.Set<RegulatoryConnectorEvent>().AsNoTracking().Where(x => numbers.Contains(x.Version))
            .OrderByDescending(x => x.Sequence).ToArrayAsync(ct);
        var tests = await db.Set<RegulatoryConnectorTest>().AsNoTracking().OrderByDescending(x => x.At).Take(100).ToArrayAsync(ct);
        var grants = await db.VendorConnectorGrants.AsNoTracking().Where(x => x.UserId == actor && Grants.Contains(x.Permission))
            .Select(x => x.Permission).ToArrayAsync(ct);
        return Results.Ok(new { identity = "BIR reporting gateway", mappingVersion = MappingVersion, officialTransmission = false,
            certificationEnabled = false, versions, events, tests, scenarios = Scenarios, grants });
    }

    private static async Task<IResult> Create(HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var actor = http.UserId();
        if (!await VendorRegulatoryReview.Has(db, actor, ConfigureGrant, ct)) return Results.NotFound();
        try
        {
            if (!http.Request.HasFormContentType) return ApiSupport.Invalid("configuration", "Use multipart configuration data and an optional specification.");
            var form = await http.Request.ReadFormAsync(ct);
            var input = JsonSerializer.Deserialize<Configuration>(form["metadata"].ToString(), SpecImport.Json);
            if (input is null || input.Environment is not ("DevelopmentSimulation" or "OfficialPending") ||
                input.TimeoutSeconds is < 1 or > 30 || input.Attempts is < 1 or > 5 || input.RateLimit is < 1 or > 60 ||
                !ApiSupport.Text(input.ApiVersion, 50) || form.Files.Count > 1)
                return ApiSupport.Invalid("configuration", "Supply bounded configuration fields.");
            var documentation = input.Documentation is null ? null : SpecImport.Https(input.Documentation, true);
            SpecReview? review = null;
            var file = form.Files.GetFile("specification");
            if (form.Files.Count != (file is null ? 0 : 1) || file is not null && input.Manual.HasValue)
                return ApiSupport.Invalid("specification", "Supply one specification or manual configuration.");
            if (input.Environment == "OfficialPending")
            {
                if (input.Origin is not null || file is not null || input.Manual.HasValue)
                    return ApiSupport.Invalid("environment", "Official transport configuration awaits verified specifications.");
            }
            else
            {
                if (input.Origin != SyntheticOrigin) return ApiSupport.Invalid("origin", "Only the reserved in-process simulator origin is allowed.");
                if (file is not null)
                {
                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (file.Length is < 1 or > SpecImport.MaxBytes || ext is not (".json" or ".yaml" or ".yml"))
                        return ApiSupport.Invalid("specification", "Upload bounded JSON or YAML.");
                    using var bytes = new MemoryStream((int)file.Length);
                    await file.CopyToAsync(bytes, ct);
                    review = SpecImport.Parse(bytes.ToArray(), ext != ".json");
                }
                else if (input.Manual is { } specification)
                    review = SpecImport.Parse(System.Text.Encoding.UTF8.GetBytes(specification.GetRawText()), false);
                if (review is null || review.Format == "Postman Collection" ||
                    review.Warnings.Any(w => w.Contains("Scripts", StringComparison.Ordinal) || w.Contains("callbacks", StringComparison.Ordinal)) ||
                    review.Servers.Any(x => x.TrimEnd('/') != SyntheticOrigin) || review.Authentication.Length != 0 ||
                    review.Endpoints.Any(x => !Methods.TryGetValue(x.Path, out var method) || x.Method != method || x.Parameters.Length != 0) ||
                    !review.Endpoints.Any(x => x.Path == "/sim/submissions" && x.Request.HasValue && x.Response.HasValue))
                    return ApiSupport.Invalid("specification", "Use allowlisted simulator methods and schemas without scripts, credentials, callbacks, or external origins.");
            }
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await Lock(db, ct);
            var latest = await db.Set<RegulatoryConnectorVersion>().MaxAsync(x => (int?)x.Number, ct) ?? 0;
            if (input.ExpectedVersion != latest) return ApiSupport.Problem(409, "The registry version has changed.");
            var row = new RegulatoryConnectorVersion { Number = latest + 1, Actor = actor, Environment = input.Environment,
                ApiVersion = SpecImport.Text(input.ApiVersion, 50), Origin = input.Origin, Documentation = documentation,
                AuthenticationAdapter = input.Environment == "OfficialPending" ? "AwaitingOfficialSpecification" : "InProcessNoCredentials",
                Specification = DocumentRules.Freeze(review), TimeoutSeconds = input.TimeoutSeconds,
                Attempts = input.Attempts, Batch = input.Batch, RateLimit = input.RateLimit };
            db.Add(row);
            db.Add(new RegulatoryConnectorEvent { Version = row.Number, Sequence = 1, Actor = actor,
                State = row.Environment == "OfficialPending" ? "AwaitingOfficialSpecifications" : "Draft", Reason = "Configuration recorded for review" });
            db.Audit("Reporting.ConnectorVersionCreated", actor);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(row);
        }
        catch (Exception e) when (e is JsonException or ConnectorInputError or InvalidDataException or BadHttpRequestException)
        {
            return ApiSupport.Invalid("specification", "The specification or configuration violates the supported safe format.");
        }
    }

    private static Task Lock(EfiscalDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(982103207)", ct);

    internal static IQueryable<RegulatoryConnectorEvent> LatestEvents(EfiscalDbContext db) =>
        db.Set<RegulatoryConnectorEvent>().AsNoTracking().Where(x => !db.Set<RegulatoryConnectorEvent>()
            .Any(y => y.Version == x.Version && y.Sequence > x.Sequence));

    private static async Task<IResult> Change(int number, Transition input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var actor = http.UserId();
        var grant = input.State is "Active" or "Disabled" or "Suspended" or "Retired" ? Activate : Review;
        if (!await VendorRegulatoryReview.Has(db, actor, grant, ct)) return Results.NotFound();
        if (!ApiSupport.Text(input.Reason, 500)) return ApiSupport.Invalid("reason", "Record a reason.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, ct);
        var row = await db.Set<RegulatoryConnectorVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Number == number, ct);
        var latest = await LatestEvents(db).SingleOrDefaultAsync(x => x.Version == number, ct);
        if (row is null || latest is null) return Results.NotFound();
        if (latest.Sequence != input.ExpectedSequence || row.Environment != "DevelopmentSimulation")
            return ApiSupport.Problem(409, "Only current synthetic configuration versions may change state.");
        var valid = (latest.State, input.State) switch
        {
            ("Draft", "UnderReview") => true,
            ("UnderReview", "ConfigurationValidated") => row.Actor != actor,
            ("ConfigurationValidated", "ApprovedForActivation") => row.Actor != actor,
            ("ApprovedForActivation" or "Suspended", "Active") => row.Actor != actor && latest.Actor != actor,
            ("Active", "Suspended" or "Disabled") => true,
            ("Suspended", "Disabled") => true,
            ("Disabled", "Retired") => true,
            _ => false
        };
        if (!valid) return ApiSupport.Problem(409, "The requested transition requires an independent reviewer or activator and an eligible state.");
        if (input.State == "Active")
        {
            var previous = await LatestEvents(db).Where(x => x.State == "Active" && x.Version != number).ToArrayAsync(ct);
            db.AddRange(previous.Select(x => new RegulatoryConnectorEvent { Version = x.Version, Actor = actor,
                Sequence = x.Sequence + 1, State = "Superseded", Reason = "Another development version was activated" }));
        }
        var next = new RegulatoryConnectorEvent { Version = number, Sequence = latest.Sequence + 1, State = input.State,
            Actor = actor, Reason = input.Reason.Trim() };
        db.Add(next);
        db.Audit("Reporting.Connector." + next.State, actor);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(next);
    }

    internal static JsonElement Payload(JsonElement canonical) => DocumentRules.Freeze(new
    {
        environment = "DevelopmentSimulation", mapping = MappingVersion, official = false, canonical
    });

    internal static JsonElement SimulatedResponse(string scenario, Guid operation) => DocumentRules.Freeze(new
    {
        environment = "DevelopmentSimulation", outcome = scenario,
        acknowledgment = scenario == "Accepted" ? "SIM-BIR-ACK-" + operation.ToString("N") : null,
        governmentStatus = "NotSubmitted"
    });

    private static async Task<IResult> RunTest(int number, TestInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var actor = http.UserId();
        if (!await VendorRegulatoryReview.Has(db, actor, Test, ct)) return Results.NotFound();
        if (!Scenarios.Contains(input.Scenario)) return ApiSupport.Invalid("scenario", "Choose a fictional response.");
        if (!await LatestEvents(db).AnyAsync(x => x.Version == number &&
            (x.State == "ConfigurationValidated" || x.State == "ApprovedForActivation" || x.State == "Active"), ct))
            return ApiSupport.Problem(409, "Validate a simulated configuration before testing.");
        var payload = Payload(DocumentRules.Freeze(new { documentNumber = "SYNTHETIC-1", currency = "PHP", total = "112.00" }));
        var id = Guid.NewGuid();
        var response = SimulatedResponse(input.Scenario, id);
        var test = new RegulatoryConnectorTest { Id = id, Actor = actor, Version = number, Scenario = input.Scenario, Response = response,
            PayloadHash = DocumentRules.Digest(payload), ResponseHash = DocumentRules.Digest(response) };
        db.Add(test);
        db.Audit("Reporting.ConnectorTestRun", actor, target: test.Id);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { payload, test });
    }
}
