using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class AccreditationVersion
{
    public required string Category { get; set; }
    public required string Key { get; set; }
    public int Number { get; set; }
    public required AccreditationInput Details { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string? Filename { get; set; }
    public string? ContentType { get; set; }
    public string? Sha256 { get; set; }
    public int? Size { get; set; }
}

internal sealed class AccreditationFile
{
    public required string Category { get; set; }
    public required string Key { get; set; }
    public int Number { get; set; }
    public required byte[] Content { get; set; }
}

internal sealed record AccreditationInput(
    string Title, string Status, string? Reference, string? SourceUrl, string? Section,
    string? Applicability, DateOnly? EffectiveDate, DateOnly? SourceReviewedOn, string? ImplementationStatus,
    string? Description, string? Notes, string? Outstanding, string? Supersedes,
    DateOnly? SubmittedOn, string? PresentationStatus, string? EvaluationStatus, string? ExternalDecisionStatus,
    string? CertificateReference, string? SolutionVersion, string? Modules, string? Infrastructure, int ExpectedVersion);

internal static partial class VendorAccreditation
{
    internal const string View = "Accreditation.View";
    internal const string Edit = "Accreditation.Edit";
    internal static readonly string[] Grants = [View, Edit];
    private static readonly string[] Categories = ["Application", "Checklist", "Requirement", "MaterialChange"];
    private static readonly string[] Readiness = ["UnderReview", "InProgress", "EvidenceReady", "AwaitingExternalValidation"];
    private static readonly Dictionary<string, string> Checklist = new()
    {
        ["registration"] = "Corporate and tax registration", ["provider"] = "Provider qualifications",
        ["personnel"] = "Technical and compliance personnel", ["architecture"] = "Architecture and data flows",
        ["privacy"] = "Security and privacy", ["interoperability"] = "Interoperability",
        ["integrity"] = "Electronic invoice integrity", ["reporting"] = "Regulatory reporting readiness",
        ["hosting"] = "Hosting infrastructure", ["recovery"] = "Backup and recovery", ["audit"] = "Audit trails",
        ["portability"] = "Data portability", ["demonstration"] = "System demonstration",
        ["assessments"] = "Security assessments", ["changes"] = "Material system changes"
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void Configure(ModelBuilder model)
    {
        var version = model.Entity<AccreditationVersion>();
        version.HasKey(x => new { x.Category, x.Key, x.Number });
        version.Property(x => x.Category).HasMaxLength(24);
        version.Property(x => x.Key).HasMaxLength(64);
        version.Property(x => x.Details).HasColumnType("jsonb").HasConversion(
            x => JsonSerializer.Serialize(x, Json), x => JsonSerializer.Deserialize<AccreditationInput>(x, Json)!);
        version.ToTable("AccreditationVersions", t =>
        {
            t.HasCheckConstraint("CK_Accreditation_Category", """
                "Number" > 0 AND "Category" IN ('Application','Checklist','Requirement','MaterialChange')
                """);
            t.HasCheckConstraint("CK_Accreditation_Evidence", """
                ("Filename" IS NULL AND "ContentType" IS NULL AND "Size" IS NULL AND "Sha256" IS NULL) OR
                ("Filename" IS NOT NULL AND "ContentType" IS NOT NULL AND "Size" IS NOT NULL AND
                 "Size" BETWEEN 1 AND 2097152 AND "Sha256" IS NOT NULL AND length("Sha256") = 64)
                """);
        });
        var file = model.Entity<AccreditationFile>();
        file.HasKey(x => new { x.Category, x.Key, x.Number });
        file.HasOne<AccreditationVersion>().WithOne().HasForeignKey<AccreditationFile>(x => new { x.Category, x.Key, x.Number })
            .OnDelete(DeleteBehavior.Restrict);
        file.ToTable("AccreditationFiles", t =>
            t.HasCheckConstraint("CK_AccreditationFile_Size", """ octet_length("Content") BETWEEN 1 AND 2097152 """));
    }

    internal static void MapVendorAccreditation(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        var group = app.MapGroup("/api/vendor/accreditation").RequireAuthorization("vendor")
            .AddEndpointFilter(ApiSupport.Protect).WithTags("Provider accreditation evidence");
        group.MapGet("", Register).AddEndpointFilter(Grant(View));
        group.MapGet("/{category}/{key}", History).AddEndpointFilter(Grant(View));
        group.MapPost("/{category}/{key}", Save).AddEndpointFilter(Grant(Edit)).RequireRateLimiting("connector-operation");
        group.MapGet("/{category}/{key}/{number:int}/evidence", Download).AddEndpointFilter(Grant(View));
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Grant(string permission) => async (context, next) =>
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<EfiscalDbContext>();
        return await db.VendorConnectorGrants.AnyAsync(x => x.UserId == http.UserId() && x.Permission == permission, http.RequestAborted)
            ? await next(context) : ApiSupport.Problem(403, "Provider accreditation permission is required.");
    };

    private static IQueryable<AccreditationVersion> Records(EfiscalDbContext db) => db.Set<AccreditationVersion>().AsNoTracking();

    private static async Task<IResult> Register(string? category, int? page, EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        category ??= "Application";
        if (!Categories.Contains(category) || page is < 1 or > 10000) return ApiSupport.Invalid("category", "Choose a valid register and page.");
        var query = Records(db).Where(x => x.Category == category);
        var current = query.Where(x => !query.Any(v => v.Key == x.Key && v.Number > x.Number));
        var items = await current.OrderBy(x => x.Key).Skip(((page ?? 1) - 1) * 25).Take(25).ToArrayAsync(ct);
        return Results.Ok(new { provider = "EMBM Services OPC", solution = "EMBM eFiscal",
            presentationReport = "The project owner reports that BIR requested a system presentation.",
            officialAccreditationVerified = false, checklist = Checklist.Select(x => new { key = x.Key, title = x.Value }),
            items, total = await current.CountAsync(ct), canEdit = await db.VendorConnectorGrants.AnyAsync(
                x => x.UserId == http.UserId() && x.Permission == Edit, ct) });
    }

    private static async Task<IResult> History(string category, string key, EfiscalDbContext db, CancellationToken ct)
    {
        if (!ValidKey(category, key)) return Results.NotFound();
        var rows = await Records(db).Where(x => x.Category == category && x.Key == key)
            .OrderByDescending(x => x.Number).ToArrayAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> Save(string category, string key, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!ValidKey(category, key) || !http.Request.HasFormContentType)
            return ApiSupport.Invalid("record", "Choose a valid record and provide multipart metadata.");
        IFormCollection form;
        AccreditationInput? details;
        try
        {
            form = await http.Request.ReadFormAsync(ct);
            details = JsonSerializer.Deserialize<AccreditationInput>(form["metadata"].ToString(), Json);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or BadHttpRequestException)
        { return ApiSupport.Invalid("record", "Invalid evidence or metadata."); }

        if (details is null || !ValidDetails(category, details))
            return ApiSupport.Invalid("record", "Check the status, required fields and official reference URL for this record.");
        if (form.Files.Count > 1 || (form.Files.Count == 1 && form.Files.GetFile("evidence") is null))
            return ApiSupport.Invalid("evidence", "Upload at most one supporting evidence file per version.");
        var file = form.Files.GetFile("evidence");
        string? filename = null;
        string? contentType = null;
        byte[]? bytes = null;
        if (file is not null)
        {
            filename = Path.GetFileName(file.FileName.Replace('\\', '/'));
            if (file.Length is < 1 or > 2097152 || !ApiSupport.Text(filename, 180) || filename.Any(char.IsControl))
                return ApiSupport.Invalid("evidence", "Upload one valid file up to 2 MiB.");
            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
            contentType = RegulatoryAuthorizations.EvidenceType(filename, bytes);
            if (contentType is null) return ApiSupport.Invalid("evidence", "Upload a PDF, PNG, JPEG, or JSON object.");
        }
        if (category == "Application" && details.ExternalDecisionStatus == "DecisionReceived" &&
            (bytes is null || !ApiSupport.Text(details.Reference, 120)))
            return ApiSupport.Invalid("evidence", "Attach supporting evidence and provide an application reference for a received decision.");

        if (details.Supersedes is not null && (category != "Requirement" || details.Supersedes == key ||
            !await Records(db).AnyAsync(x => x.Category == "Requirement" && x.Key == details.Supersedes, ct)))
            return ApiSupport.Invalid("supersedes", "Choose a different existing requirement.");
        var latest = await Records(db).Where(x => x.Category == category && x.Key == key).MaxAsync(x => (int?)x.Number, ct) ?? 0;
        if (latest != details.ExpectedVersion) return ApiSupport.Problem(409, "The record has a newer version.");
        if (latest > 0 && category == "Requirement")
        {
            var prior = await Records(db).SingleAsync(x => x.Category == category && x.Key == key && x.Number == latest, ct);
            if (details.Supersedes != prior.Details.Supersedes)
                return ApiSupport.Invalid("supersedes", "Preserve the original superseded requirement link.");
        }

        var record = new AccreditationVersion
        {
            Category = category, Key = key, Number = latest + 1,
            Details = category == "Checklist" ? details with { Title = Checklist[key] } : details,
            RecordedBy = http.UserId(), RecordedAt = DateTimeOffset.UtcNow, Filename = filename, ContentType = contentType,
            Size = bytes?.Length, Sha256 = bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes))
        };
        db.Add(record);
        if (bytes is not null) db.Add(new AccreditationFile { Category = category, Key = key, Number = record.Number, Content = bytes });
        db.Audit("Regulatory.Accreditation" + category + "VersionRecorded", actor: http.UserId());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (ApiSupport.IsUniqueViolation(error))
        { return ApiSupport.Problem(409, "The record has a newer version."); }
        return Results.Ok(record);
    }

    private static bool ValidKey(string category, string key) => category switch
    {
        "Application" => key == "embm-efiscal",
        "Checklist" => Checklist.ContainsKey(key),
        "Requirement" or "MaterialChange" => RecordKey().IsMatch(key),
        _ => false
    };

    private static bool ValidDetails(string category, AccreditationInput x)
    {
        if (!ApiSupport.Text(x.Title, 160) || x.ExpectedVersion < 0 ||
            !ApiSupport.Optional(x.Reference, 120) || !ApiSupport.Optional(x.Description, 4000) ||
            !ApiSupport.Optional(x.Notes, 4000) || !ApiSupport.Optional(x.Outstanding, 4000) ||
            !ApiSupport.Optional(x.Section, 160) || !ApiSupport.Optional(x.Applicability, 2000) || !ApiSupport.Optional(x.Supersedes, 64) ||
            !ApiSupport.Optional(x.ImplementationStatus, 80) || !ApiSupport.Optional(x.SolutionVersion, 80) ||
            !ApiSupport.Optional(x.CertificateReference, 120) || !ApiSupport.Optional(x.Modules, 2000) || !ApiSupport.Optional(x.Infrastructure, 2000) ||
            x.SourceUrl is not null && !OfficialSource(x.SourceUrl))
            return false;
        return category switch
        {
            "Application" => x.Status is "Preparing" or "Submitted" or "UnderEvaluation" or "Closed" &&
                (x.Status == "Preparing" || x.SubmittedOn is not null && ApiSupport.Text(x.Reference, 120)) &&
                x.PresentationStatus is "NotScheduled" or "Requested" or "Scheduled" or "Presented" &&
                x.EvaluationStatus is "NotStarted" or "InProgress" or "Complete" &&
                x.ExternalDecisionStatus is "Pending" or "DecisionReceived" &&
                (string.IsNullOrWhiteSpace(x.CertificateReference) || x.ExternalDecisionStatus == "DecisionReceived"),
            "Checklist" => Readiness.Contains(x.Status),
            "Requirement" => x.Status is "UnderReview" or "AwaitingExternalValidation" or "SourceReviewed" &&
                OfficialSource(x.SourceUrl) && ApiSupport.Text(x.Reference, 120) && ApiSupport.Text(x.Section, 160) && ApiSupport.Text(x.Description, 4000) &&
                ApiSupport.Text(x.Applicability, 2000) && (x.Status != "SourceReviewed" || x.SourceReviewedOn is not null),
            "MaterialChange" => Readiness.Contains(x.Status) && ApiSupport.Text(x.SolutionVersion, 80) && ApiSupport.Text(x.Notes, 4000),
            _ => false
        };
    }

    private static bool OfficialSource(string? url) => ApiSupport.Text(url, 500) &&
        Uri.TryCreate(url, UriKind.Absolute, out var source) && source.Scheme == "https" && source.IsDefaultPort &&
        source.UserInfo.Length == 0 && (source.Host == "bir.gov.ph" || source.Host.EndsWith(".bir.gov.ph", StringComparison.Ordinal));

    private static async Task<IResult> Download(string category, string key, int number, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!ValidKey(category, key)) return Results.NotFound();
        var version = await Records(db).SingleOrDefaultAsync(x => x.Category == category && x.Key == key && x.Number == number, ct);
        if (version?.Filename is null) return Results.NotFound();
        var bytes = await db.Set<AccreditationFile>().AsNoTracking().Where(x => x.Category == category && x.Key == key && x.Number == number)
            .Select(x => x.Content).SingleOrDefaultAsync(ct);
        if (bytes is null || bytes.Length != version.Size || Convert.ToHexString(SHA256.HashData(bytes)) != version.Sha256)
            return ApiSupport.Problem(409, "Evidence integrity check failed.");
        http.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(bytes, "application/octet-stream", version.Filename);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RecordKey();
}
