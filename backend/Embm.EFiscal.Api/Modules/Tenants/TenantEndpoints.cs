using System.Text;
using System.Text.RegularExpressions;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Billing;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Tenants;

public sealed record CompanyInput(string Code, string RegisteredName, string? TradeName, string BusinessClassification,
    string Address, string Email, string Phone);
public sealed record TaxpayerInput(string Tin, string VatStatus, string RdoCode, DateOnly EffectiveFrom,
    string? RegistrationDetails, string? PermitReference);
public sealed record BranchInput(string Type, string Code, string Name, string Address, string? TaxRegistration, string? RdoCode, string? Contact);
public sealed record CreateCompanyRequest(CompanyInput Company, TaxpayerInput Taxpayer, BranchInput Branch);
public sealed record ContextRequest(Guid CompanyId, Guid? BranchId);
public sealed record SubscriberInput(string Name);

public static partial class TenantEndpoints
{
    public static void MapTenants(this WebApplication app)
    {
        var workspace = app.MapGroup("/api/workspace").RequireAuthorization("customer").WithTags("Company workspace");
        workspace.AddEndpointFilter(ApiSupport.Protect);
        workspace.MapGet("/", Workspace);
        var account = workspace.MapGroup("/{subscriberId:guid}");
        account.MapGet("/", SubscriberInformation);
        account.MapPut("/", UpdateSubscriber);
        account.MapGet("/companies", Companies);
        account.MapPost("/companies", CreateCompany);
        account.MapGet("/rdos", References);
        account.MapPost("/context", SetContext);
        var company = account.MapGroup("/companies/{companyId:guid}");
        company.MapGet("/", Details);
        company.MapPut("/", UpdateCompany);
        company.MapPut("/taxpayer", UpdateTaxpayer);
        company.MapGet("/branches", Branches);
        company.MapGet("/branches/{branchId:guid}", GetBranch);
        company.MapPost("/branches", (Guid subscriberId, Guid companyId, BranchInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
            SaveBranch(subscriberId, companyId, null, input, http, db, ct));
        company.MapPut("/branches/{branchId:guid}", (Guid subscriberId, Guid companyId, Guid branchId, BranchInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
            SaveBranch(subscriberId, companyId, branchId, input, http, db, ct));
        company.MapPost("/evidence", UploadEvidence).DisableAntiforgery(); // The group enforces the SPA antiforgery header.
    }

    private static async Task<IResult> Workspace(HttpContext http, EfiscalDbContext db, IWebHostEnvironment environment, CancellationToken ct)
    {
        var id = http.UserId();
        var isDevelopment = environment.IsDevelopment();
        var members = await (from m in db.SubscriberMemberships
                       join s in db.Subscribers on m.SubscriberId equals s.Id
                       where m.UserId == id && (isDevelopment || !s.DevelopmentFixture)
                       orderby s.Name
                       select new { s.Id, s.Name, s.PlanId, s.Billing, s.Status, s.CompanyLimit, s.UserLimit, s.DevelopmentFixture, m.RoleId })
            .ToListAsync(ct);
        var subscribers = members.Select(s => new { s.Id, s.Name, s.PlanId, s.Billing, status = s.Status.ToString(),
            s.CompanyLimit, s.UserLimit, s.DevelopmentFixture, s.RoleId });
        var session = await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == http.SessionId(), ct);
        var intent = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id, ct);
        object? active = null;
        if (session.ActiveSubscriberId is Guid subscriber && session.ActiveCompanyId is Guid company &&
            await TenantAccess.CompanyAsync(db, http, subscriber, company, Permissions.CompanyView, ct, existingRecords: true) is not null)
        {
            var branch = session.ActiveBranchId is Guid branchId &&
                await db.Branches.AnyAsync(x => x.Id == branchId && x.SubscriberId == subscriber && x.CompanyId == company, ct) ? session.ActiveBranchId : null;
            active = new { subscriberId = subscriber, companyId = company, branchId = branch };
        }
        return Results.Ok(new { subscribers, active, pending = members.Count == 0 && intent is not null
            ? new { intent.PlanId, intent.Billing, intent.Classification, status = "PendingPayment" } : null });
    }

    private static async Task<IResult> SubscriberInformation(Guid subscriberId, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var access = await TenantAccess.ResolveAsync(db, http, subscriberId, null, ct);
        if (access is null) return TenantAccess.Missing();
        var account = access.Subscriber;
        return Results.Ok(new { account.Id, account.Name, account.PlanId, account.Billing, status = account.Status.ToString(),
            account.CompanyLimit, account.UserLimit, canManage = access.IsOwner });
    }

    private static async Task<IResult> UpdateSubscriber(Guid subscriberId, SubscriberInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriberId, null, ct) is not { IsOwner: true }) return TenantAccess.Missing();
        if (!ApiSupport.Text(input.Name, 150)) return ApiSupport.Invalid("name", "Enter a subscriber name of 1 to 150 characters.");
        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        if (await TenantAccess.ResolveAsync(db, http, subscriberId, null, ct) is not { IsOwner: true }) return TenantAccess.Missing();
        var row = (await db.Subscribers.FindAsync([subscriberId], ct))!;
        row.Name = input.Name.Trim();
        db.Audit("SubscriberInformationUpdated", http.UserId(), subscriberId, subscriberId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Companies(Guid subscriberId, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var grant = await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.CompanyView, ct, existingRecords: true);
        if (grant is null) return TenantAccess.Missing();
        var query = TenantAccess.Companies(db, subscriberId, http.UserId(), grant);
        var companies = await query.OrderBy(x => x.RegisteredName)
            .Select(x => new { x.Id, x.Code, x.RegisteredName }).ToListAsync(ct);
        return Results.Ok(new { companies, permissions = grant.Grants,
            canAddCompany = grant.IsOwner && grant.Subscriber.Status == CommercialStatus.Active && companies.Count < grant.Subscriber.CompanyLimit &&
                await new CreditEngine(db).AllowsOperations(subscriberId, DateTimeOffset.UtcNow, ct) });
    }

    private static async Task<IResult> Details(Guid subscriberId, Guid companyId, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var company = await TenantAccess.CompanyAsync(db, http, subscriberId, companyId, Permissions.CompanyView, ct, existingRecords: true);
        if (company is null) return TenantAccess.Missing();
        var taxpayer = await db.TaxpayerProfiles.AsNoTracking().SingleAsync(x => x.SubscriberId == subscriberId && x.CompanyId == companyId, ct);
        var assignments = await db.RdoAssignments.AsNoTracking().Where(x => x.SubscriberId == subscriberId && x.CompanyId == companyId)
            .OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct);
        var evidence = await db.RegistrationEvidence.Where(x => x.SubscriberId == subscriberId && x.CompanyId == companyId)
            .Select(x => new { x.Id, x.Name, x.UploadedAt }).ToListAsync(ct);
        return Results.Ok(new { company, taxpayer = new { taxpayer.Tin, taxpayer.VatStatus, taxpayer.RegistrationDetails, taxpayer.PermitReference,
            verificationStatus = taxpayer.VerificationStatus.ToString(), issuanceStatus = taxpayer.IssuanceStatus.ToString() },
            configurationStatus = company.ConfigurationStatus.ToString(), assignments, evidence });
    }

    private static async Task<IResult> References(Guid subscriberId, HttpContext http, EfiscalDbContext db, IWebHostEnvironment env, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.CompanyView, ct, existingRecords: true) is null) return TenantAccess.Missing();
        var development = env.IsDevelopment();
        return Results.Ok(await db.RdoReferences.Where(x => x.Active && (development || !x.DevelopmentFixture)).OrderBy(x => x.Code).ToListAsync(ct));
    }

    private static async Task<IResult> SetContext(Guid subscriberId, ContextRequest input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.CompanyAsync(db, http, subscriberId, input.CompanyId, Permissions.CompanyView, ct, existingRecords: true) is null ||
            (input.BranchId is Guid branch && !await BranchQuery(db, subscriberId, input.CompanyId).AnyAsync(x => x.Id == branch, ct)))
            return TenantAccess.Missing();
        var session = (await db.Sessions.FindAsync([http.SessionId()], ct))!;
        session.ActiveSubscriberId = subscriberId;
        session.ActiveCompanyId = input.CompanyId;
        session.ActiveBranchId = input.BranchId;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateCompany(Guid subscriberId, CreateCompanyRequest input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.CompanyManage, ct) is not { IsOwner: true }) return TenantAccess.Missing();
        if (ValidateCompany(input.Company) is { } companyError) return companyError;
        if (ValidateTax(input.Taxpayer) is { } taxError) return taxError;
        if (ValidateBranch(input.Branch) is { } branchError) return branchError;

        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        var access = await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.CompanyManage, ct);
        if (access is not { IsOwner: true }) return TenantAccess.Missing();
        if (!await new CreditEngine(db).AllowsOperations(subscriberId, DateTimeOffset.UtcNow, ct))
            return ApiSupport.Problem(409, "The subscription does not allow new company files");
        var reference = await ActiveRdo(db, input.Taxpayer.RdoCode, http, ct);
        var branchReference = input.Branch.RdoCode is { Length: > 0 } code ? await ActiveRdo(db, code, http, ct) : null;
        if (reference is null || (input.Branch.RdoCode is { Length: > 0 } && branchReference is null))
            return ApiSupport.Invalid("rdoCode", "Choose an active registered RDO from the reference list.");
        if (await db.Companies.CountAsync(x => x.SubscriberId == subscriberId, ct) >= access.Subscriber.CompanyLimit)
            return ApiSupport.Problem(409, "Company file allowance reached");

        var company = new Company { SubscriberId = subscriberId, CreatedAt = DateTimeOffset.UtcNow, ConfigurationStatus = ConfigurationStatus.Configured };
        ApplyCompany(company, input.Company);
        var taxpayer = new TaxpayerProfile { SubscriberId = subscriberId, CompanyId = company.Id };
        ApplyTax(taxpayer, input.Taxpayer);
        var branch = new Branch { SubscriberId = subscriberId, CompanyId = company.Id };
        ApplyBranch(branch, input.Branch);
        db.AddRange(company, taxpayer, branch, Assignment(company, null, reference, input.Taxpayer.EffectiveFrom));
        if (branchReference is not null) db.RdoAssignments.Add(Assignment(company, branch.Id, branchReference, input.Taxpayer.EffectiveFrom));
        db.Audit("CompanyConfigured", http.UserId(), subscriberId, company.Id);
        var session = (await db.Sessions.FindAsync([http.SessionId()], ct))!;
        session.ActiveSubscriberId = subscriberId;
        session.ActiveCompanyId = company.Id;
        session.ActiveBranchId = branch.Id;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (ApiSupport.IsUniqueViolation(error)) { return ApiSupport.Problem(409, "Company file code already exists"); }
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/workspace/{subscriberId}/companies/{company.Id}", new { company.Id, branchId = branch.Id });
    }

    private static async Task<IResult> UpdateCompany(Guid subscriberId, Guid companyId, CompanyInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        var company = await TenantAccess.CompanyAsync(db, http, subscriberId, companyId, Permissions.CompanyManage, ct);
        if (company is null) return TenantAccess.Missing();
        if (ValidateCompany(input) is { } error) return error;
        ApplyCompany(company, input);
        db.Audit("CompanyInformationUpdated", http.UserId(), subscriberId, companyId);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ApiSupport.IsUniqueViolation(ex)) { return ApiSupport.Problem(409, "Company file code already exists"); }
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateTaxpayer(Guid subscriberId, Guid companyId, TaxpayerInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        var company = await TenantAccess.CompanyAsync(db, http, subscriberId, companyId, Permissions.CompanyManage, ct);
        if (company is null) return TenantAccess.Missing();
        if (ValidateTax(input) is { } error) return error;
        var reference = await ActiveRdo(db, input.RdoCode, http, ct);
        if (reference is null) return ApiSupport.Invalid("rdoCode", "Choose an active registered RDO from the reference list.");
        var taxpayer = (await db.TaxpayerProfiles.FindAsync([subscriberId, companyId], ct))!;
        var current = await db.RdoAssignments.SingleOrDefaultAsync(x => x.SubscriberId == subscriberId && x.CompanyId == companyId && x.BranchId == null && x.EffectiveUntil == null, ct);
        if (current?.Code != reference.Code)
        {
            if (current is not null && input.EffectiveFrom <= current.EffectiveFrom) return ApiSupport.Invalid("effectiveFrom", "Choose a date after the previous assignment began.");
            if (current is not null)
            {
                current.EffectiveUntil = input.EffectiveFrom;
                await db.SaveChangesAsync(ct);
            }
            db.RdoAssignments.Add(Assignment(company, null, reference, input.EffectiveFrom));
        }
        ApplyTax(taxpayer, input);
        db.Audit("TaxpayerRegistrationUpdated", http.UserId(), subscriberId, companyId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Branches(Guid subscriberId, Guid companyId, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.CompanyAsync(db, http, subscriberId, companyId, Permissions.BranchView, ct, existingRecords: true) is null) return TenantAccess.Missing();
        return Results.Ok(await BranchQuery(db, subscriberId, companyId).OrderBy(x => x.Code).ToListAsync(ct));
    }

    private static async Task<IResult> GetBranch(Guid subscriberId, Guid companyId, Guid branchId, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.CompanyAsync(db, http, subscriberId, companyId, Permissions.BranchView, ct, existingRecords: true) is null) return TenantAccess.Missing();
        var branch = await BranchQuery(db, subscriberId, companyId).SingleOrDefaultAsync(x => x.Id == branchId, ct);
        return branch is null ? TenantAccess.Missing() : Results.Ok(branch);
    }

    private static async Task<IResult> SaveBranch(Guid subscriberId, Guid companyId, Guid? branchId, BranchInput input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        var company = await TenantAccess.CompanyAsync(db, http, subscriberId, companyId, Permissions.BranchManage, ct);
        if (company is null) return TenantAccess.Missing();
        if (ValidateBranch(input) is { } error) return error;
        var branch = branchId is null ? new Branch { SubscriberId = subscriberId, CompanyId = companyId }
            : await BranchQuery(db, subscriberId, companyId).SingleOrDefaultAsync(x => x.Id == branchId, ct);
        if (branch is null) return TenantAccess.Missing();

        var current = await db.RdoAssignments.SingleOrDefaultAsync(x => x.SubscriberId == subscriberId && x.CompanyId == companyId &&
            x.BranchId == branch.Id && x.EffectiveUntil == null, ct);
        var newCode = string.IsNullOrWhiteSpace(input.RdoCode) ? null : input.RdoCode.Trim();
        if (newCode != current?.Code)
        {
            var reference = newCode is null ? null : await ActiveRdo(db, newCode, http, ct);
            if (newCode is not null && reference is null) return ApiSupport.Invalid("rdoCode", "Choose an active registered RDO.");
            var effectiveDate = ManilaDate();
            if (current is not null)
            {
                if (effectiveDate <= current.EffectiveFrom) return ApiSupport.Invalid("rdoCode", "This RDO assignment cannot be replaced on its first effective day.");
                current.EffectiveUntil = effectiveDate;
                await db.SaveChangesAsync(ct);
            }
            if (reference is not null) db.RdoAssignments.Add(Assignment(company, branch.Id, reference, effectiveDate));
        }
        if (branchId is null) db.Branches.Add(branch);
        ApplyBranch(branch, input);
        db.Audit(branchId is null ? "BranchCreated" : "BranchUpdated", http.UserId(), subscriberId, branch.Id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ApiSupport.IsUniqueViolation(ex)) { return ApiSupport.Problem(409, "Branch code or head office already exists"); }
        await transaction.CommitAsync(ct);
        return branchId is null ? Results.Created($"/api/workspace/{subscriberId}/companies/{companyId}/branches/{branch.Id}", branch)
            : Results.NoContent();
    }

    private static async Task<IResult> UploadEvidence(Guid subscriberId, Guid companyId, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        if (await TenantAccess.CompanyAsync(db, http, subscriberId, companyId, Permissions.CompanyManage, ct) is null) return TenantAccess.Missing();
        if (!http.Request.HasFormContentType) return ApiSupport.Invalid("file", "Attach a PDF registration evidence file.");
        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length is < 6 or > 2_097_152 || form.Files.Count != 1)
            return ApiSupport.Invalid("file", "Attach one PDF no larger than 2 MB.");
        using var memory = new MemoryStream((int)file.Length);
        await file.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();
        if (!bytes.AsSpan().StartsWith("%PDF-"u8)) return ApiSupport.Invalid("file", "Attach a PDF registration evidence file.");
        if (await db.RegistrationEvidence.CountAsync(x => x.SubscriberId == subscriberId && x.CompanyId == companyId, ct) >= 10)
            return ApiSupport.Problem(409, "Registration evidence limit reached");
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        var evidence = new RegistrationEvidence { SubscriberId = subscriberId, CompanyId = companyId,
            Name = ApiSupport.Text(name, 120) ? name : "Registration evidence.pdf", Content = bytes, UploadedAt = DateTimeOffset.UtcNow };
        db.RegistrationEvidence.Add(evidence);
        db.Audit("RegistrationEvidenceAttached", http.UserId(), subscriberId, evidence.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { evidence.Id, evidence.Name });
    }

    private static IQueryable<Branch> BranchQuery(EfiscalDbContext db, Guid tenant, Guid company) =>
        db.Branches.Where(x => x.SubscriberId == tenant && x.CompanyId == company);
    private static Task<RdoReference?> ActiveRdo(EfiscalDbContext db, string code, HttpContext http, CancellationToken ct)
    {
        var development = http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        return db.RdoReferences.SingleOrDefaultAsync(x => x.Code == code.Trim() && x.Active && (development || !x.DevelopmentFixture), ct);
    }
    private static RdoAssignment Assignment(Company company, Guid? branch, RdoReference reference, DateOnly from) =>
        new() { SubscriberId = company.SubscriberId, CompanyId = company.Id, BranchId = branch, Code = reference.Code, Name = reference.Name, EffectiveFrom = from };

    private static IResult? ValidateCompany(CompanyInput? i)
    {
        if (i is null || !ValidCode(i.Code)) return ApiSupport.Invalid("code", "Enter a company file code using 2–20 letters, digits or hyphens.");
        if (!ApiSupport.Text(i.RegisteredName, 150) || !ApiSupport.Optional(i.TradeName, 150) || !ApiSupport.Text(i.Address, 300) || !ApiSupport.Text(i.BusinessClassification, 60))
            return ApiSupport.Invalid("registeredName", "Enter registered name, classification and address within the field limits.");
        if (!ApiSupport.Email(i.Email) || !ApiSupport.Text(i.Phone, 30)) return ApiSupport.Invalid("email", "Enter a valid business email and contact number.");
        return null;
    }

    private static IResult? ValidateTax(TaxpayerInput? i)
    {
        if (i is null || !ApiSupport.Text(i.Tin, 16) || !TinPattern().IsMatch(i.Tin.Trim())) return ApiSupport.Invalid("tin", "Enter a 9- or 12-digit TIN, with optional hyphens.");
        if (i.VatStatus is not ("VAT" or "NonVAT" or "Exempt") || !ApiSupport.Text(i.RdoCode, 10)) return ApiSupport.Invalid("rdoCode", "Select a VAT status and registered RDO.");
        if (i.EffectiveFrom < new DateOnly(1900, 1, 1) || i.EffectiveFrom > ManilaDate()) return ApiSupport.Invalid("effectiveFrom", "Choose a valid effective date through today in Manila.");
        if (!ApiSupport.Optional(i.RegistrationDetails, 500) || !ApiSupport.Optional(i.PermitReference, 120)) return ApiSupport.Invalid("registrationDetails", "Registration details exceed the field limits.");
        return null;
    }

    private static IResult? ValidateBranch(BranchInput? i) => i is null || !ValidCode(i.Code) || i.Type is not ("HeadOffice" or "Branch") ||
        !ApiSupport.Text(i.Name, 150) || !ApiSupport.Text(i.Address, 300) || !ApiSupport.Optional(i.TaxRegistration, 120) ||
        !ApiSupport.Optional(i.RdoCode, 10) || !ApiSupport.Optional(i.Contact, 80)
        ? ApiSupport.Invalid("branch", "Enter a branch type, valid code, name and address within the field limits.") : null;

    private static void ApplyCompany(Company c, CompanyInput i) =>
        (c.Code, c.RegisteredName, c.TradeName, c.BusinessClassification, c.Address, c.Email, c.Phone) =
        (i.Code.Trim().ToUpperInvariant(), i.RegisteredName.Trim(), i.TradeName?.Trim(), i.BusinessClassification.Trim(), i.Address.Trim(), i.Email.Trim(), i.Phone.Trim());
    private static void ApplyTax(TaxpayerProfile t, TaxpayerInput i) =>
        (t.Tin, t.VatStatus, t.RegistrationDetails, t.PermitReference, t.VerificationStatus, t.IssuanceStatus) =
        (i.Tin.Trim(), i.VatStatus, i.RegistrationDetails?.Trim(), i.PermitReference?.Trim(), VerificationStatus.NotVerified, IssuanceStatus.Disabled);
    private static void ApplyBranch(Branch b, BranchInput i) =>
        (b.Type, b.Code, b.Name, b.Address, b.TaxRegistration, b.Contact) =
        (i.Type, i.Code.Trim().ToUpperInvariant(), i.Name.Trim(), i.Address.Trim(), i.TaxRegistration?.Trim(), i.Contact?.Trim());

    private static bool ValidCode(string? code) => ApiSupport.Text(code, 20) && CodePattern().IsMatch(code!.Trim());
    internal static DateOnly ManilaDate() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Manila").DateTime);
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{1,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
    [GeneratedRegex("^[0-9]{3}-?[0-9]{3}-?[0-9]{3}(?:-?[0-9]{3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex TinPattern();
}
