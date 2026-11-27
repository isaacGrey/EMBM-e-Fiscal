using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.CompanyConfiguration;

public sealed record SelectTemplateDefault(Guid? TemplateId);

public static class CompanyConfigurationEndpoints
{
    public static void MapCompanyConfiguration(this WebApplication app)
    {
        var group = app.MapGroup("/api/workspace/{subscriberId:guid}/companies/{companyId:guid}/configuration")
            .WithTags("Company delivery configuration").RequireAuthorization("customer");
        group.AddEndpointFilter(ApiSupport.Protect);
        group.AddEndpointFilter(RequireCompany);
        group.MapGet("/sender", GetSender);
        group.MapPut("/sender", SaveSender).AddEndpointFilter(Transaction);
        group.MapPost("/sender/validate", (SenderProfile input) => TemplateRules.ValidateSender(input) ??
            Results.Ok(new { state = "ConfigurationValid", connectivityChecked = false, senderVerified = false }));
        if (app.Environment.IsDevelopment())
            group.MapPost("/sender/test", TestSender).RequireRateLimiting("company-email-preview");
        group.MapGet("/templates/{kind}/{documentType}", ListTemplates);
        group.MapPost("/templates/{kind}/{documentType}", CreateTemplate).AddEndpointFilter(Transaction);
        group.MapPut("/templates/{kind}/{documentType}/default", SetDefault).AddEndpointFilter(Transaction);
        group.MapGet("/templates/{templateId:guid}", GetTemplate);
        group.MapPut("/templates/{templateId:guid}", UpdateTemplate).AddEndpointFilter(Transaction);
        group.MapPost("/templates/{templateId:guid}/duplicate", Duplicate).AddEndpointFilter(Transaction);
        group.MapPost("/templates/{templateId:guid}/restore", Restore).AddEndpointFilter(Transaction);
        group.MapPost("/email-preview/{documentType}", PreviewEmail).RequireRateLimiting("company-email-preview");
        group.MapPost("/email-preview/{documentType}/test", TestEmail).RequireRateLimiting("company-email-preview");
    }

    private static (Guid Subscriber, Guid Company) Scope(HttpContext http) =>
        (Guid.Parse((string)http.Request.RouteValues["subscriberId"]!), Guid.Parse((string)http.Request.RouteValues["companyId"]!));

    private static async ValueTask<object?> RequireCompany(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<EfiscalDbContext>();
        var scope = Scope(http);
        if (await TenantAccess.CompanyAsync(db, http, scope.Subscriber, scope.Company, Permissions.CompanyManage, http.RequestAborted) is null)
            return TenantAccess.Missing();
        return await next(context);
    }

    private static async ValueTask<object?> Transaction(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<EfiscalDbContext>();
        var (subscriber, company) = Scope(http);
        var ct = http.RequestAborted;
        await using var transaction = await db.LockSubscriberAsync(subscriber, ct);
        if (await TenantAccess.CompanyAsync(db, http, subscriber, company, Permissions.CompanyManage, ct) is null)
            return TenantAccess.Missing();
        var result = await next(context);
        if (result is not IStatusCodeHttpResult { StatusCode: >= 400 }) await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task<IResult> GetSender(Guid subscriberId, Guid companyId, EfiscalDbContext db, ICompanyMailSampleSink sink, CancellationToken ct)
    {
        var row = await db.CompanyEmailSettings.FindAsync([subscriberId, companyId], ct);
        var sampleHistory = await db.SecurityAudits.AsNoTracking()
            .Where(x => x.SubscriberId == subscriberId && x.TargetId == companyId &&
                (x.Action == "CompanyEmailSampleWritten" || x.Action == "CompanyEmailSampleFailed"))
            .OrderByDescending(x => x.Id).Take(15)
            .Select(x => new { x.At, status = x.Action == "CompanyEmailSampleWritten" ? "Local preview written" : "Local preview failed" }).ToListAsync(ct);
        var lastSuccessfulTestAt = await db.SecurityAudits.AsNoTracking()
            .Where(x => x.SubscriberId == subscriberId && x.TargetId == companyId && x.Action == "CompanyEmailSampleWritten")
            .OrderByDescending(x => x.Id).Select(x => (DateTimeOffset?)x.At).FirstOrDefaultAsync(ct);
        return Results.Ok(new
        {
            profile = row is null ? null : JsonSerializer.Deserialize<SenderProfile>(row.ProfileJson, TemplateRules.Json),
            revision = row?.Revision ?? 0, updatedAt = row?.UpdatedAt, testMailboxAvailable = sink.Available,
            senderVerified = false, state = "PendingProviderAuthorization", connectionStatus = "PendingProviderAuthorization",
            sampleHistory, lastSuccessfulTestAt
        });
    }

    private static async Task<IResult> SaveSender(Guid subscriberId, Guid companyId, SenderUpdate input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (TemplateRules.ValidateSender(input.Profile) is { } problem) return problem;
        var row = await db.CompanyEmailSettings.FindAsync([subscriberId, companyId], ct);
        if ((row?.Revision ?? 0) != input.ExpectedRevision) return Conflict();
        if (row is null)
        {
            row = new CompanyEmailSettings { SubscriberId = subscriberId, CompanyId = companyId };
            db.CompanyEmailSettings.Add(row);
        }
        row.ProfileJson = JsonSerializer.Serialize(input.Profile with
        {
            SenderName = input.Profile.SenderName.Trim(),
            ReplyTo = string.IsNullOrWhiteSpace(input.Profile.ReplyTo) ? null : input.Profile.ReplyTo
        }, TemplateRules.Json);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.Revision++;
        db.Audit("CompanyEmailSettingsSaved", http.UserId(), subscriberId, companyId);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { row.Revision, row.UpdatedAt, state = "PendingProviderAuthorization" });
    }

    private static async Task<IResult> ListTemplates(Guid subscriberId, Guid companyId, string kind, string documentType, EfiscalDbContext db, CancellationToken ct)
    {
        if (!TemplateRules.ValidType(kind, documentType)) return TenantAccess.Missing();
        var records = await InScope(db, subscriberId, companyId).Where(x => x.Kind == kind && x.DocumentType == documentType)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToListAsync(ct);
        var defaults = TemplateRules.Defaults(documentType);
        return Results.Ok(new { templates = records.Select(Info), defaultContent = kind == "email" ? (object)defaults.Email : defaults.Layout,
            variables = TemplateRules.Fields });
    }

    private static async Task<IResult> CreateTemplate(Guid subscriberId, Guid companyId, string kind, string documentType, TemplateInput input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!TemplateRules.ValidType(kind, documentType)) return TenantAccess.Missing();
        if (Serialize(kind, input) is not { } content) return InvalidTemplate();
        return await AddTemplate(subscriberId, companyId, kind, documentType, input.Name.Trim(), content, http, db, ct);
    }

    private static async Task<IResult> AddTemplate(Guid subscriberId, Guid companyId, string kind, string documentType, string name, string content,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await InScope(db, subscriberId, companyId).CountAsync(x => x.Kind == kind && x.DocumentType == documentType, ct) >= 20)
            return ApiSupport.Problem(409, "Template limit reached for this document type");
        var template = new CompanyTemplate { SubscriberId = subscriberId, CompanyId = companyId, Kind = kind, DocumentType = documentType };
        db.CompanyTemplates.Add(template);
        await AddVersion(template, name, content, "CompanyTemplateCreated", http, db, ct);
        return Results.Created($"{http.Request.PathBase}/api/workspace/{subscriberId}/companies/{companyId}/configuration/templates/{template.Id}", Info(template));
    }

    private static async Task<IResult> GetTemplate(Guid subscriberId, Guid companyId, Guid templateId, EfiscalDbContext db, CancellationToken ct)
    {
        var template = await InScope(db, subscriberId, companyId).SingleOrDefaultAsync(x => x.Id == templateId, ct);
        if (template is null) return TenantAccess.Missing();
        var versions = Versions(db, template);
        var current = await versions.SingleAsync(x => x.Version == template.CurrentVersion, ct);
        var history = await versions.OrderByDescending(x => x.Version)
            .Select(x => new { x.Version, x.Name, x.CreatedAt }).ToListAsync(ct);
        using var content = JsonDocument.Parse(current.ContentJson);
        return Results.Ok(new { template = Info(template), content = content.RootElement.Clone(), history });
    }

    private static async Task<IResult> UpdateTemplate(Guid subscriberId, Guid companyId, Guid templateId, TemplateInput input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var template = await InScope(db, subscriberId, companyId).SingleOrDefaultAsync(x => x.Id == templateId, ct);
        if (template is null) return TenantAccess.Missing();
        if (template.CurrentVersion != input.ExpectedVersion) return Conflict();
        if (Serialize(template.Kind, input) is not { } content) return InvalidTemplate();
        await AddVersion(template, input.Name.Trim(), content, "CompanyTemplateVersionSaved", http, db, ct);
        return Results.Ok(Info(template));
    }

    private static async Task<IResult> Restore(Guid subscriberId, Guid companyId, Guid templateId, ChangeVersion input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var template = await InScope(db, subscriberId, companyId).SingleOrDefaultAsync(x => x.Id == templateId, ct);
        if (template is null) return TenantAccess.Missing();
        if (template.CurrentVersion != input.ExpectedVersion) return Conflict();
        var previous = await Versions(db, template).SingleOrDefaultAsync(x => x.Version == input.Version, ct);
        if (previous is null) return TenantAccess.Missing();
        await AddVersion(template, previous.Name, previous.ContentJson, "CompanyTemplateVersionRestored", http, db, ct);
        return Results.Ok(Info(template));
    }

    private static async Task<IResult> Duplicate(Guid subscriberId, Guid companyId, Guid templateId, DuplicateTemplate input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var template = await InScope(db, subscriberId, companyId).SingleOrDefaultAsync(x => x.Id == templateId, ct);
        if (template is null) return TenantAccess.Missing();
        if (!ApiSupport.Text(input.Name, 100) || input.Name.Any(char.IsControl)) return InvalidTemplate();
        var latest = await Versions(db, template).SingleAsync(x => x.Version == template.CurrentVersion, ct);
        return await AddTemplate(subscriberId, companyId, template.Kind, template.DocumentType, input.Name.Trim(), latest.ContentJson, http, db, ct);
    }

    private static async Task<IResult> SetDefault(Guid subscriberId, Guid companyId, string kind, string documentType, SelectTemplateDefault input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!TemplateRules.ValidType(kind, documentType)) return TenantAccess.Missing();
        var matching = InScope(db, subscriberId, companyId).Where(x => x.Kind == kind && x.DocumentType == documentType);
        var selected = input.TemplateId is Guid id ? await matching.SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (input.TemplateId is not null && selected is null) return TenantAccess.Missing();
        var current = await matching.SingleOrDefaultAsync(x => x.IsDefault, ct);
        if (current?.Id == selected?.Id) return Results.NoContent();
        if (current is not null)
        {
            current.IsDefault = false;
            await db.SaveChangesAsync(ct);
        }
        if (selected is not null) selected.IsDefault = true;
        db.Audit("CompanyTemplateDefaultChanged", http.UserId(), subscriberId, selected?.Id ?? companyId);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static IResult PreviewEmail(string documentType, PreviewInput input)
    {
        if (!TemplateRules.ValidType("email", documentType)) return TenantAccess.Missing();
        return TemplateRules.ValidEmail(input.Email) ? Results.Ok(TemplateRules.RenderSample(input.Email, documentType)) : InvalidTemplate();
    }

    private static async Task<IResult> TestSender(Guid subscriberId, Guid companyId, HttpContext http, EfiscalDbContext db,
        ICompanyMailSampleSink sink, CancellationToken ct)
    {
        var row = await db.CompanyEmailSettings.FindAsync([subscriberId, companyId], ct);
        var profile = row is null ? null : JsonSerializer.Deserialize<SenderProfile>(row.ProfileJson, TemplateRules.Json);
        if (profile is not { Provider: "platform" } || !Documents.DocumentCommitService.TestAddress(profile.SenderEmail))
            return ApiSupport.Problem(409, "Save a platform sender with a synthetic .test address before testing.");
        return await TestEmail(subscriberId, companyId, "invoice", new(TemplateRules.Defaults("invoice").Email),
            http, db, sink, ct);
    }

    private static async Task<IResult> TestEmail(Guid subscriberId, Guid companyId, string documentType, PreviewInput input,
        HttpContext http, EfiscalDbContext db, ICompanyMailSampleSink sink, CancellationToken ct)
    {
        if (!TemplateRules.ValidType("email", documentType)) return TenantAccess.Missing();
        if (!sink.Available) return ApiSupport.Problem(403, "Local test email is unavailable");
        if (!TemplateRules.ValidEmail(input.Email)) return InvalidTemplate();
        try
        {
            await sink.WriteAsync(companyId, documentType, TemplateRules.RenderSample(input.Email, documentType), ct);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            db.Audit("CompanyEmailSampleFailed", http.UserId(), subscriberId, companyId);
            await db.SaveChangesAsync(ct);
            return ApiSupport.Problem(503, "The local test mailbox could not be written");
        }
        db.Audit("CompanyEmailSampleWritten", http.UserId(), subscriberId, companyId);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { status = "LocalPreviewWritten", recipient = "customer@example.test", sentExternally = false });
    }

    private static string? Serialize(string kind, TemplateInput input)
    {
        if (!ApiSupport.Text(input.Name, 100) || input.Name.Any(char.IsControl)) return null;
        if (kind == "email" && input.Layout is null && TemplateRules.ValidEmail(input.Email))
            return JsonSerializer.Serialize(input.Email, TemplateRules.Json);
        if (kind == "pdf" && input.Email is null && TemplateRules.ValidLayout(input.Layout))
            return JsonSerializer.Serialize(input.Layout, TemplateRules.Json);
        return null;
    }

    private static async Task AddVersion(CompanyTemplate template, string name, string content, string action,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        template.Name = name;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        template.CurrentVersion++;
        db.CompanyTemplateVersions.Add(new CompanyTemplateVersion
        {
            SubscriberId = template.SubscriberId, CompanyId = template.CompanyId, TemplateId = template.Id,
            Version = template.CurrentVersion, Name = name, ContentJson = content, CreatedAt = template.UpdatedAt, CreatedBy = http.UserId()
        });
        db.Audit(action, http.UserId(), template.SubscriberId, template.Id);
        await db.SaveChangesAsync(ct);
    }

    private static IQueryable<CompanyTemplate> InScope(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.CompanyTemplates.Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    private static IQueryable<CompanyTemplateVersion> Versions(EfiscalDbContext db, CompanyTemplate template) =>
        db.CompanyTemplateVersions.Where(x => x.SubscriberId == template.SubscriberId && x.CompanyId == template.CompanyId && x.TemplateId == template.Id);

    private static object Info(CompanyTemplate template) => new
    {
        template.Id, template.Kind, template.DocumentType, template.Name, template.IsDefault, template.CurrentVersion, template.UpdatedAt
    };

    private static IResult InvalidTemplate() =>
        ApiSupport.Invalid("template", "Check the name, text lengths, allowed variables, and layout values. Markup and executable content are not accepted.");
    private static IResult Conflict() => ApiSupport.Problem(409, "The configuration changed. Reload the saved version before editing.");
}
