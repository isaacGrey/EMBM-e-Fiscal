using System.Security.Cryptography;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal static partial class RegulatoryAuthorizations
{
    private const int MaxEvidenceBytes = 2 * 1024 * 1024;
    private static readonly string[] Kinds =
        ["PermitToIssue", "EisCertification", "PermitToTransmit", "CasRegistration", "PermitToUse", "AcknowledgmentCertificate", "Other"];
    private static readonly string[] DocumentTypes = ["SalesInvoice", "CreditMemo"];
    private static readonly Dictionary<string, string> Decisions = new(StringComparer.Ordinal)
    {
        ["ReviewAccepted"] = Review,
        ["AdditionalEvidence"] = RequestMore,
        ["Rejected"] = Reject,
        ["ExternalAuthorizationDocumented"] = DocumentExternal,
        ["Suspended"] = Suspend,
        ["Revoked"] = Revoke
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record Submission(Guid BranchId, string Kind, string DocumentType, string Reference,
        string Authority, string Solution, string SolutionVersion, string Scope, DateOnly EffectiveFrom, DateOnly? ExpiresOn, int ExpectedVersion);
    internal sealed record ReviewCommand(string Decision, string Reason, int ExpectedSequence);

    internal static void MapRegulatoryAuthorizations(this WebApplication app)
    {
        var group = app.MapGroup("/api/workspace/{subscriber:guid}/companies/{company:guid}/regulatory-authorizations")
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect).WithTags("Regulatory authorizations");
        group.MapGet("/", Register);
        group.MapPost("/", (Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
            Save(subscriber, company, null, http, db, ct));
        group.MapGet("/{id:guid}/history", ReadHistory);
        group.MapPost("/{id:guid}/versions", (Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
            Save(subscriber, company, id, http, db, ct));
        group.MapGet("/{id:guid}/versions/{number:int}/evidence", Download);
        group.MapPost("/{id:guid}/versions/{number:int}/reviews", RecordReview);
        group.MapGet("/eligibility", Eligibility);
    }

    private static IQueryable<RegulatoryAuthorization> Records(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.Set<RegulatoryAuthorization>().AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
    private static IQueryable<RegulatoryAuthorizationVersion> Versions(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.Set<RegulatoryAuthorizationVersion>().AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
    private static IQueryable<RegulatoryAuthorizationReview> Reviews(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.Set<RegulatoryAuthorizationReview>().AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    private static async Task<IResult> Register(Guid subscriber, Guid company, int? page, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var access = await DocumentEndpoints.Access(subscriber, company, View, http, db, ct);
        if (access is null) return TenantAccess.Missing();
        if (page is < 1 or > 10000) return ApiSupport.Invalid("page", "Use a page between 1 and 10000.");
        var currentPage = page ?? 1;
        var query = Records(db, subscriber, company);
        var versions = Versions(db, subscriber, company);
        var reviews = Reviews(db, subscriber, company);
        var records = await query.OrderBy(x => x.Id).Skip((currentPage - 1) * 25).Take(25)
            .Select(record => new
            {
                record.Id, record.BranchId, record.Kind, record.DocumentType,
                latest = versions.Where(x => x.AuthorizationId == record.Id).OrderByDescending(x => x.Number).First(),
                decision = reviews.Where(x => x.AuthorizationId == record.Id &&
                    x.Number == versions.Where(v => v.AuthorizationId == record.Id).Max(v => v.Number))
                    .OrderByDescending(x => x.Sequence).Select(x => x.Decision).FirstOrDefault()
            }).ToArrayAsync(ct);
        var grants = access.Grant.Grants;
        return Results.Ok(new { records, page = currentPage, total = await query.CountAsync(ct),
            kinds = Kinds, documentTypes = DocumentTypes, canSubmit = grants.Contains(Submit),
            canViewHistory = grants.Contains(History), reviewActions = Decisions.Where(x => grants.Contains(x.Value)).Select(x => x.Key) });
    }

    private static async Task<IResult> ReadHistory(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, History, http, db, ct) is null) return TenantAccess.Missing();
        var record = await Records(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (record is null) return TenantAccess.Missing();
        var versions = await Versions(db, subscriber, company).Where(x => x.AuthorizationId == id).OrderByDescending(x => x.Number).ToArrayAsync(ct);
        var reviews = await Reviews(db, subscriber, company).Where(x => x.AuthorizationId == id).OrderByDescending(x => x.Number)
            .ThenByDescending(x => x.Sequence).ToArrayAsync(ct);
        return Results.Ok(new { record, versions, reviews });
    }

    private static async Task<IResult> Save(Guid subscriber, Guid company, Guid? id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, Submit, http, db, ct) is null) return TenantAccess.Missing();
        if (!http.Request.HasFormContentType) return ApiSupport.Invalid("evidence", "Provide multipart evidence and metadata.");
        IFormCollection form;
        Submission? input;
        try
        {
            form = await http.Request.ReadFormAsync(ct);
            input = JsonSerializer.Deserialize<Submission>(form["metadata"].ToString(), JsonOptions);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or BadHttpRequestException)
        {
            return ApiSupport.Invalid("evidence", "Invalid evidence or metadata.");
        }
        if (input is null || !Kinds.Contains(input.Kind) || !DocumentTypes.Contains(input.DocumentType) ||
            !ApiSupport.Text(input.Reference, 120) || !ApiSupport.Text(input.Authority, 120) || !ApiSupport.Text(input.Solution, 120) ||
            !ApiSupport.Text(input.SolutionVersion, 80) || !ApiSupport.Text(input.Scope, 1000) || input.EffectiveFrom == default ||
            input.ExpiresOn < input.EffectiveFrom || input.ExpectedVersion < 0)
            return ApiSupport.Invalid("metadata", "Provide valid authorization scope, dates, and reference details.");
        if (form.Files.Count != 1 || form.Files.GetFile("evidence") is not { Length: > 0 and <= MaxEvidenceBytes } file)
            return ApiSupport.Invalid("evidence", "Upload one evidence file up to 2 MiB.");
        var filename = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (!ApiSupport.Text(filename, 180) || filename.Any(char.IsControl))
            return ApiSupport.Invalid("evidence", "Provide a valid filename.");
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var contentType = EvidenceType(filename, bytes);
        if (contentType is null) return ApiSupport.Invalid("evidence", "Upload a PDF, PNG, JPEG, or JSON object.");

        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Submit, http, db, ct) is null ||
            !await db.Branches.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == input.BranchId, ct) ||
            !await db.TaxpayerProfiles.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company, ct))
            return TenantAccess.Missing();

        var records = Records(db, subscriber, company);
        var record = id is Guid existing
            ? await records.SingleOrDefaultAsync(x => x.Id == existing, ct)
            : await records.SingleOrDefaultAsync(x => x.BranchId == input.BranchId && x.Kind == input.Kind && x.DocumentType == input.DocumentType, ct);
        if (id.HasValue && record is null) return TenantAccess.Missing();
        if (record is not null && (!id.HasValue || record.BranchId != input.BranchId || record.Kind != input.Kind || record.DocumentType != input.DocumentType))
            return ApiSupport.Problem(409, "Use the existing authorization and preserve its branch, kind, and document type.");
        if (record is null)
        {
            record = new RegulatoryAuthorization { SubscriberId = subscriber, CompanyId = company, BranchId = input.BranchId,
                Kind = input.Kind, DocumentType = input.DocumentType };
            db.Add(record);
        }
        var latest = await Versions(db, subscriber, company).Where(x => x.AuthorizationId == record.Id).MaxAsync(x => (int?)x.Number, ct) ?? 0;
        if (latest != input.ExpectedVersion) return ApiSupport.Problem(409, "Authorization version has changed.");
        var version = new RegulatoryAuthorizationVersion
        {
            SubscriberId = subscriber, CompanyId = company, AuthorizationId = record.Id, Number = latest + 1,
            Reference = input.Reference.Trim(), Authority = input.Authority.Trim(), Solution = input.Solution.Trim(),
            SolutionVersion = input.SolutionVersion.Trim(), Scope = input.Scope.Trim(), EffectiveFrom = input.EffectiveFrom,
            ExpiresOn = input.ExpiresOn, Filename = filename, ContentType = contentType,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), Size = bytes.Length,
            SubmittedBy = http.UserId(), SubmittedAt = DateTimeOffset.UtcNow
        };
        db.Add(version);
        db.Add(new RegulatoryAuthorizationFile { SubscriberId = subscriber, CompanyId = company, AuthorizationId = record.Id,
            Number = version.Number, Content = bytes });
        db.Audit("Regulatory.AuthorizationEvidenceSubmitted", http.UserId(), subscriber, record.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Created($"/api/workspace/{subscriber}/companies/{company}/regulatory-authorizations/{record.Id}/history", version);
    }

    internal static string? EvidenceType(string name, byte[] bytes)
    {
        var content = bytes.AsSpan();
        switch (Path.GetExtension(name).ToLowerInvariant())
        {
            case ".pdf" when content.StartsWith("%PDF-"u8) && content.LastIndexOf("%%EOF"u8) >= 0: return "application/pdf";
            case ".png" when content.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && content.EndsWith(new byte[] { 73, 69, 78, 68, 174, 66, 96, 130 }): return "image/png";
            case ".jpg" or ".jpeg" when content.StartsWith(new byte[] { 255, 216, 255 }) && content.EndsWith(new byte[] { 255, 217 }): return "image/jpeg";
            case ".json":
                try
                {
                    using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
                    return json.RootElement.ValueKind == JsonValueKind.Object ? "application/json" : null;
                }
                catch (JsonException) { return null; }
            default: return null;
        }
    }

    private static async Task<IResult> Download(Guid subscriber, Guid company, Guid id, int number, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, View, http, db, ct) is not { } access) return TenantAccess.Missing();
        var versions = Versions(db, subscriber, company).Where(x => x.AuthorizationId == id);
        var version = await versions.SingleOrDefaultAsync(x => x.Number == number, ct);
        if (version is null || (!access.Grant.Grants.Contains(History) && await versions.AnyAsync(x => x.Number > number, ct)))
            return TenantAccess.Missing();

        var bytes = await db.Set<RegulatoryAuthorizationFile>().AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.AuthorizationId == id && x.Number == number)
            .Select(x => x.Content).SingleOrDefaultAsync(ct);
        if (bytes is null || bytes.Length != version.Size || Convert.ToHexString(SHA256.HashData(bytes)) != version.Sha256)
            return ApiSupport.Problem(409, "Evidence integrity check failed.");
        http.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(bytes, "application/octet-stream", version.Filename);
    }

    private static async Task<IResult> RecordReview(Guid subscriber, Guid company, Guid id, int number, ReviewCommand input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (input.Decision is null || !Decisions.TryGetValue(input.Decision, out var grant) ||
            !ApiSupport.Text(input.Reason, 1000) || input.ExpectedSequence < 0)
            return ApiSupport.Invalid("review", "Choose a review action and provide a reason.");
        if (await DocumentEndpoints.Access(subscriber, company, grant, http, db, ct) is null) return TenantAccess.Missing();
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, grant, http, db, ct) is null) return TenantAccess.Missing();
        var latest = await Versions(db, subscriber, company).Where(x => x.AuthorizationId == id)
            .OrderByDescending(x => x.Number).FirstOrDefaultAsync(ct);
        if (latest is null || latest.SubmittedBy == http.UserId()) return TenantAccess.Missing();
        if (latest.Number != number) return ApiSupport.Problem(409, "Review the current authorization version.");
        var prior = await Reviews(db, subscriber, company).Where(x => x.AuthorizationId == id && x.Number == number)
            .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        if ((prior?.Sequence ?? 0) != input.ExpectedSequence || !CanReview(prior?.Decision, input.Decision))
            return ApiSupport.Problem(409, "The requested review conflicts with the current review state.");
        if (input.Decision == "ExternalAuthorizationDocumented" && latest.ExpiresOn < RegulatoryEligibility.ManilaDate())
            return ApiSupport.Problem(409, "Expired coverage cannot be recorded as current authorization.");
        var row = new RegulatoryAuthorizationReview
        {
            SubscriberId = subscriber, CompanyId = company, AuthorizationId = id, Number = number,
            Sequence = input.ExpectedSequence + 1, Reviewer = http.UserId(), ReviewedAt = DateTimeOffset.UtcNow,
            Decision = input.Decision, Reason = input.Reason.Trim()
        };
        db.Add(row);
        db.Audit("Regulatory." + row.Decision, http.UserId(), subscriber, id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(row);
    }

    private static bool CanReview(string? previous, string next) => previous switch
    {
        null => next is "ReviewAccepted" or "AdditionalEvidence" or "Rejected",
        "ReviewAccepted" => next is "ExternalAuthorizationDocumented" or "AdditionalEvidence" or "Rejected",
        "ExternalAuthorizationDocumented" => next is "Suspended" or "Revoked",
        "Suspended" => next == "Revoked",
        _ => false
    };

    private static async Task<IResult> Eligibility(Guid subscriber, Guid company, Guid branch, string documentType,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, View, http, db, ct) is null ||
            !await db.Branches.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == branch, ct))
            return TenantAccess.Missing();
        if (!DocumentTypes.Contains(documentType)) return ApiSupport.Invalid("documentType", "Choose a Sales Invoice or Credit Memo.");
        return Results.Ok(await RegulatoryEligibility.Evaluate(db, subscriber, company, branch, documentType, ct));
    }
}
