using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EFISCAL_TEST_DB")))
            Skip = "Set EFISCAL_TEST_DB to a dedicated loopback efiscal_test PostgreSQL database.";
    }
}

public sealed partial class IdentityTenantTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _host = new();
    private WebApplicationFactory<Program> _app = null!;
    private readonly string _mail = Path.Combine(Path.GetTempPath(), "efiscal-test-mail", Guid.NewGuid().ToString("N"));
    private readonly string _password = Guid.NewGuid().ToString("N");
    private string _database = "";

    public async Task InitializeAsync()
    {
        _database = Environment.GetEnvironmentVariable("EFISCAL_TEST_DB")!;
        var settings = new NpgsqlConnectionStringBuilder(_database);
        Assert.Contains(settings.Host, new[] { "localhost", "::1", "127.0.0.1" });
        Assert.Equal("efiscal_test", settings.Database);
        _app = Host("Development");
        await Db(async db =>
        {
            await db.Database.MigrateAsync();
            if (!await db.RdoReferences.AnyAsync(x => x.Code == "001"))
            {
                db.RdoReferences.AddRange(new RdoReference { Code = "001", Name = "Fictional Test Office A", Active = true, DevelopmentFixture = true, Source = "Test" },
                    new RdoReference { Code = "009", Name = "Fictional Test Office B", Active = true, DevelopmentFixture = true, Source = "Test" });
                await db.SaveChangesAsync();
            }
        });
    }

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        await _host.DisposeAsync();
    }

    [PostgresFact]
    public async Task RegistrationVerificationRecoveryAndRevocableSessions()
    {
        using var browser = Browser();
        var email = Email("identity");
        var registration = Registration(email);
        await Expect(browser.PostAsJsonAsync("/api/auth/register", registration), HttpStatusCode.BadRequest); // Missing CSRF.
        await Expect(Mutate(browser, "/api/auth/register", registration with { BusinessName = null }), HttpStatusCode.BadRequest);
        await Expect(Mutate(browser, "/api/auth/register", registration), HttpStatusCode.Accepted);
        var expired = Mail(email, "Verify your email");
        await Expect(Mutate(browser, "/api/auth/register", registration with { Email = email.ToUpperInvariant() }), HttpStatusCode.Accepted);
        var userId = Guid.Empty;
        await Db(async db =>
        {
            var row = await db.UserIdentities.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
            userId = row.UserId;
            Assert.NotEqual(_password, row.PasswordHash);
            Assert.Equal(0, await db.SubscriberMemberships.CountAsync(m => m.UserId == row.UserId));
            await db.IdentityTokens.Where(x => x.UserId == userId).ExecuteUpdateAsync(p => p.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        });
        await Expect(Mutate(browser, "/api/auth/verify-email", new { token = expired }), HttpStatusCode.BadRequest);
        await Expect(Mutate(browser, "/api/auth/login", new { email, password = _password }), HttpStatusCode.Forbidden);
        await Expect(Mutate(browser, "/api/auth/resend-verification", new { email }), HttpStatusCode.Accepted);
        var verify = Mail(email, "Verify your email");
        Assert.NotEqual(expired, verify);
        await Expect(Mutate(browser, "/api/auth/verify-email", new { token = verify }), HttpStatusCode.NoContent);
        await Expect(Mutate(browser, "/api/auth/verify-email", new { token = verify }), HttpStatusCode.BadRequest);

        for (var i = 0; i < 5; i++) await Expect(Mutate(browser, "/api/auth/login", new { email, password = "Invalid password" }), HttpStatusCode.Unauthorized);
        await Expect(Mutate(browser, "/api/auth/login", new { email, password = _password }), HttpStatusCode.Unauthorized);
        await Db(async db => Assert.True((await db.UserIdentities.FindAsync(userId))!.LockedUntil > DateTimeOffset.UtcNow));

        await Expect(Mutate(browser, "/api/auth/forgot-password", new { email }), HttpStatusCode.Accepted);
        await Expect(Mutate(browser, "/api/auth/forgot-password", new { email = Email("unknown") }), HttpStatusCode.Accepted);
        var reset = Mail(email, "Reset your password");
        await Db(async db => await db.IdentityTokens.Where(x => x.UserId == userId && x.Purpose == TokenPurpose.ResetPassword)
            .ExecuteUpdateAsync(change => change.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await Expect(Mutate(browser, "/api/auth/reset-password", new { token = reset, password = _password, confirmation = _password }), HttpStatusCode.BadRequest);
        await Expect(Mutate(browser, "/api/auth/forgot-password", new { email }), HttpStatusCode.Accepted);
        var firstPassword = Guid.NewGuid().ToString("N");
        await Expect(Mutate(browser, "/api/auth/reset-password", new { token = Mail(email, "Reset your password"), password = firstPassword, confirmation = firstPassword }), HttpStatusCode.NoContent);

        using var login = await Mutate(browser, "/api/auth/login", new { email, password = firstPassword, rememberMe = true });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.Contains("efiscal-dev-session") &&
            value.Contains("httponly", StringComparison.OrdinalIgnoreCase) && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        var me = await Json(browser, "/api/auth/me");
        Assert.Equal("Verified", me.GetProperty("status").GetString());
        Assert.Equal("business", me.GetProperty("pending").GetProperty("planId").GetString());
        Assert.DoesNotContain("hash", me.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var workspace = await Json(browser, "/api/workspace");
        Assert.Equal("PendingPayment", workspace.GetProperty("pending").GetProperty("status").GetString());
        Assert.Empty(workspace.GetProperty("subscribers").EnumerateArray());
        await Expect(Mutate(browser, $"/api/workspace/{Guid.NewGuid()}/companies", Form("WAIT")), HttpStatusCode.NotFound);

        using var recovery = Browser();
        await Expect(Mutate(recovery, "/api/auth/forgot-password", new { email }), HttpStatusCode.Accepted);
        var newPassword = Guid.NewGuid().ToString("N");
        await Expect(Mutate(recovery, "/api/auth/reset-password", new { token = Mail(email, "Reset your password"), password = newPassword, confirmation = newPassword }), HttpStatusCode.NoContent);
        await Expect(browser.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized);
        await Login(browser, email, newPassword);
        await Expect(Mutate(browser, "/api/auth/logout"), HttpStatusCode.NoContent);
        await Expect(browser.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized);
        await Login(browser, email, newPassword);
        await Db(async db => await db.Sessions.Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(change => change.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await Expect(browser.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized);
    }

    [PostgresFact]
    public async Task CompanyBranchAndInvitationWritesAreScopedByTenantAndRole()
    {
        using var owner = Browser();
        using var other = Browser();
        var (tenant, ownerId, email) = await SeedSubscriber();
        var (otherTenant, _, otherEmail) = await SeedSubscriber();
        await Login(owner, email, _password);
        await Login(other, otherEmail, _password);
        await Expect(Mutate(other, $"/api/workspace/{tenant}", new { name = "Foreign Edit" }, HttpMethod.Put), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, $"/api/workspace/{tenant}", new { name = "Updated Synthetic Subscriber" }, HttpMethod.Put), HttpStatusCode.NoContent);
        Assert.Equal("Updated Synthetic Subscriber", (await Json(owner, $"/api/workspace/{tenant}")).GetProperty("name").GetString());
        var company = await CreateCompany(owner, tenant, "MNL");
        var secondCompany = await CreateCompany(owner, tenant, "CEB");
        var outsider = await CreateCompany(other, otherTenant, "OTH");
        var ownerPath = $"/api/workspace/{tenant}/companies/{company}";
        var foreignPath = $"/api/workspace/{otherTenant}/companies/{outsider}";
        await Expect(owner.GetAsync(foreignPath), HttpStatusCode.NotFound);
        await Expect(other.GetAsync(ownerPath), HttpStatusCode.NotFound);
        await Expect(other.GetAsync($"/api/workspace/{tenant}/team"), HttpStatusCode.NotFound);
        await Expect(Mutate(other, ownerPath, Form("EDIT").Company, HttpMethod.Put), HttpStatusCode.NotFound);
        await Expect(Mutate(other, ownerPath + "/taxpayer", Form("EDIT").Taxpayer, HttpMethod.Put), HttpStatusCode.NotFound);
        await Expect(Mutate(other, ownerPath + "/branches", Branch("SEC")), HttpStatusCode.NotFound);
        await Expect(Mutate(other, $"/api/workspace/{tenant}/team/invitations", new { email = Email("bad"), roleId = "readonly", companyIds = new[] { company } }), HttpStatusCode.NotFound);

        using var branchResponse = await Mutate(owner, ownerPath + "/branches", Branch("SUB"));
        Assert.Equal(HttpStatusCode.Created, branchResponse.StatusCode);
        var branch = (await branchResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Expect(other.GetAsync(ownerPath + "/branches/" + branch), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, $"/api/workspace/{tenant}/context", new { companyId = secondCompany, branchId = branch }), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, $"/api/workspace/{tenant}/context", new { companyId = company, branchId = branch }), HttpStatusCode.NoContent);
        Assert.Equal(branch, (await Json(owner, "/api/workspace")).GetProperty("active").GetProperty("branchId").GetGuid());
        var tax = Form("MNL").Taxpayer with { RdoCode = "009", EffectiveFrom = ManilaToday() };
        await Expect(Mutate(owner, ownerPath + "/taxpayer", tax, HttpMethod.Put), HttpStatusCode.NoContent);
        var details = await Json(owner, ownerPath);
        Assert.Equal("NotVerified", details.GetProperty("taxpayer").GetProperty("verificationStatus").GetString());
        Assert.Equal("Disabled", details.GetProperty("taxpayer").GetProperty("issuanceStatus").GetString());
        Assert.Contains(details.GetProperty("assignments").EnumerateArray(), x => x.GetProperty("code").GetString() == "001" &&
            x.GetProperty("effectiveUntil").ValueKind == JsonValueKind.String);

        var invitePath = $"/api/workspace/{tenant}/team/invitations";
        var invited = Email("invited");
        await Expect(Mutate(owner, invitePath, new { email = invited, roleId = "owner", companyIds = new[] { company } }), HttpStatusCode.Forbidden);
        await Expect(Mutate(owner, invitePath, new { email = invited, roleId = "readonly", companyIds = new[] { outsider } }), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, invitePath, new { email = invited, roleId = "company-admin", companyIds = new[] { company } }), HttpStatusCode.Accepted);
        await Db(async db => await db.Invitations.Where(x => x.Email == invited).ExecuteUpdateAsync(change => change.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        using var member = Browser();
        var signup = new InvitationRegistration(Mail(invited, "Your eFiscal invitation"), "Test Member", "09000000000", _password, _password, true, true);
        await Expect(Mutate(member, "/api/invitations/register", signup), HttpStatusCode.BadRequest);
        await Expect(Mutate(owner, invitePath, new { email = invited, roleId = "company-admin", companyIds = new[] { company } }), HttpStatusCode.Accepted);
        signup = signup with { Token = Mail(invited, "Your eFiscal invitation") };
        await Expect(Mutate(other, "/api/invitations/accept", new { signup.Token }), HttpStatusCode.BadRequest);
        await Expect(Mutate(member, "/api/invitations/register", signup), HttpStatusCode.NoContent);
        await Expect(Mutate(member, "/api/invitations/register", signup), HttpStatusCode.BadRequest);
        await Login(member, invited, _password);
        await Expect(member.GetAsync(ownerPath), HttpStatusCode.OK);
        await Expect(member.GetAsync($"/api/workspace/{tenant}/companies/{secondCompany}"), HttpStatusCode.NotFound);
        var list = (await Json(member, $"/api/workspace/{tenant}/companies")).GetProperty("companies");
        Assert.Equal(company, Assert.Single(list.EnumerateArray()).GetProperty("id").GetGuid());
        await Expect(Mutate(member, invitePath, new { email = Email("escalation"), roleId = "company-admin", companyIds = new[] { company } }), HttpStatusCode.Forbidden);
        await Expect(Mutate(member, invitePath, new { email = Email("escalation"), roleId = "readonly", companyIds = new[] { secondCompany } }), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, $"/api/workspace/{tenant}/team/members/{ownerId}", method: HttpMethod.Delete), HttpStatusCode.Conflict);

        var memberId = (await Json(member, "/api/auth/me")).GetProperty("id").GetGuid();
        await Expect(Mutate(owner, $"/api/workspace/{tenant}/team/members/{memberId}", new { roleId = "readonly", companyIds = new[] { secondCompany } }, HttpMethod.Put), HttpStatusCode.NoContent);
        await Expect(member.GetAsync(ownerPath), HttpStatusCode.NotFound);
        await Expect(Mutate(member, invitePath, new { email = Email("readonly"), roleId = "readonly", companyIds = new[] { secondCompany } }), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, $"/api/workspace/{tenant}/team/members/{memberId}", method: HttpMethod.Delete), HttpStatusCode.NoContent);
        await Expect(member.GetAsync($"/api/workspace/{tenant}/companies/{secondCompany}"), HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task ProductionRejectsLocalIdentitiesAndRegulatorAccess()
    {
        var (_, _, email) = await SeedSubscriber();
        var vendor = Email("vendor");
        await Db(async db =>
        {
            var id = NewIdentity(vendor);
            id.Kind = IdentityKind.Vendor;
            db.Users.Add(id);
            await db.SaveChangesAsync();
        });
        using var development = Browser();
        await Expect(development.PostAsJsonAsync("/api/auth/bir/login", new { email, password = _password, rdo = "001" }), HttpStatusCode.Forbidden);
        await Expect(Mutate(development, "/api/auth/vendor/login", new { email = vendor, password = _password }), HttpStatusCode.NoContent);
        await Expect(development.GetAsync("/api/workspace"), HttpStatusCode.Forbidden);
        using var production = Host("Production");
        using var isolated = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await Expect(Mutate(isolated, "/api/auth/login", new { email, password = _password }), HttpStatusCode.Unauthorized);
        await Expect(Mutate(isolated, "/api/auth/vendor/login", new { email = vendor, password = _password }), HttpStatusCode.Forbidden);
        await Expect(isolated.PostAsJsonAsync("/api/auth/bir/login", new { email, password = _password }), HttpStatusCode.Forbidden);
        await Expect(Mutate(isolated, "/api/auth/register", Registration(Email("production"))), HttpStatusCode.ServiceUnavailable);
        var companyPath = $"/api/workspace/{Guid.NewGuid()}/companies/{Guid.NewGuid()}";
        await Expect(Mutate(isolated, companyPath + "/configuration/sender/test", new { }), HttpStatusCode.NotFound);
        await Expect(Mutate(isolated, companyPath + "/documents/SalesInvoice/" + Guid.NewGuid() + "/resend", new { }), HttpStatusCode.NotFound);
        Assert.Null(typeof(Program).Assembly.GetType("Embm.EFiscal.Api.Infrastructure.PreviewAuthenticationHandler"));
    }

    [PostgresFact]
    public async Task CompanySenderAndTemplatesAreScopedValidatedAndVersioned()
    {
        using var owner = Browser();
        using var delegateBrowser = Browser();
        var (tenant, _, email) = await SeedSubscriber();
        await Login(owner, email, _password);
        var first = await CreateCompany(owner, tenant, "MAIL");
        var second = await CreateCompany(owner, tenant, "LAYOUT");
        var (foreign, _, foreignEmail) = await SeedSubscriber();
        using var other = Browser();
        await Login(other, foreignEmail, _password);

        string PathFor(Guid company) => $"/api/workspace/{tenant}/companies/{company}/configuration";
        var a = PathFor(first);
        var b = PathFor(second);
        var delegatedEmail = Email("company-admin");
        await Db(async db =>
        {
            var delegated = NewIdentity(delegatedEmail);
            db.AddRange(delegated, new SubscriberMembership { SubscriberId = tenant, UserId = delegated.Id, RoleId = "company-admin", CreatedAt = DateTimeOffset.UtcNow },
                new CompanyMembership { SubscriberId = tenant, UserId = delegated.Id, CompanyId = first });
            await db.SaveChangesAsync();
        });
        await Login(delegateBrowser, delegatedEmail, _password);
        await Expect(delegateBrowser.GetAsync(a + "/sender"), HttpStatusCode.OK);
        await Expect(delegateBrowser.GetAsync(b + "/sender"), HttpStatusCode.NotFound);
        await Expect(other.GetAsync(a + "/sender"), HttpStatusCode.NotFound);

        var profile = new SenderProfile("Sample sender", "sender@example.test", "reply@example.test", "platform", null, null, null, null,
            ["copy@example.test"], []);
        await Expect(Mutate(owner, a + "/sender/validate", profile with
        {
            Provider = "custom-smtp", SmtpHost = "127.0.0.1", SmtpPort = 587, TlsMode = "starttls", Authentication = "password"
        }), HttpStatusCode.BadRequest);
        await Expect(Mutate(owner, a + "/sender/validate", profile with
        {
            Provider = "custom-smtp", SmtpHost = "smtp.example.test", SmtpPort = 587, TlsMode = "implicit-tls", Authentication = "oauth2"
        }), HttpStatusCode.BadRequest);
        using var validation = await Mutate(owner, a + "/sender/validate", profile with
        {
            Provider = "custom-smtp", SmtpHost = "smtp.example.test", SmtpPort = 587, TlsMode = "starttls", Authentication = "oauth2"
        });
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        Assert.False((await validation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("connectivityChecked").GetBoolean());

        await Expect(Mutate(owner, a + "/sender", new SenderUpdate(0, profile), HttpMethod.Put), HttpStatusCode.OK);
        await Expect(Mutate(owner, a + "/sender", new SenderUpdate(0, profile), HttpMethod.Put), HttpStatusCode.Conflict);
        await Expect(Mutate(delegateBrowser, b + "/sender", new SenderUpdate(0, profile), HttpMethod.Put), HttpStatusCode.NotFound);
        Assert.Equal(JsonValueKind.Null, (await Json(owner, b + "/sender")).GetProperty("profile").ValueKind);
        var sender = await Json(owner, a + "/sender");
        Assert.False(sender.GetProperty("senderVerified").GetBoolean());
        Assert.DoesNotContain("password", sender.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var emailTemplate = new EmailTemplate("Invoice {{DocumentNumber}}", "Hello {{CustomerName}}", "{{CompanyName}}", null, null, true);
        var emails = a + "/templates/email/invoice";
        await Expect(Mutate(owner, emails, new TemplateInput("Unsafe markup", emailTemplate with { Body = "<script>alert(1)</script>" }, null)), HttpStatusCode.BadRequest);
        await Expect(Mutate(owner, emails, new TemplateInput("Unknown", emailTemplate with { Subject = "{{Secret}}" }, null)), HttpStatusCode.BadRequest);

        using var created = await Mutate(owner, emails, new TemplateInput("Invoice email", emailTemplate, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var item = a + "/templates/" + id;
        await Expect(owner.GetAsync(b + "/templates/" + id), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, b + "/templates/email/invoice/default", new SelectTemplateDefault(id), HttpMethod.Put), HttpStatusCode.NotFound);
        await Expect(Mutate(owner, emails + "/default", new SelectTemplateDefault(id), HttpMethod.Put), HttpStatusCode.NoContent);
        await Expect(Mutate(owner, item, new TemplateInput("Updated email", emailTemplate with { Body = "Second version" }, null, 1), HttpMethod.Put), HttpStatusCode.OK);
        await Expect(Mutate(owner, item, new TemplateInput("Stale edit", emailTemplate, null, 1), HttpMethod.Put), HttpStatusCode.Conflict);
        await Expect(Mutate(owner, item + "/restore", new ChangeVersion(1, 2)), HttpStatusCode.OK);
        var detail = await Json(owner, item);
        Assert.Equal("Hello {{CustomerName}}", detail.GetProperty("content").GetProperty("body").GetString());
        Assert.Equal(3, detail.GetProperty("history").GetArrayLength());
        Assert.True(detail.GetProperty("template").GetProperty("isDefault").GetBoolean());

        using var preview = await Mutate(owner, a + "/email-preview/invoice", new PreviewInput(emailTemplate));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("Hello Sample Customer", (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("body").GetString());
        await Expect(Mutate(owner, a + "/email-preview/invoice/test", new PreviewInput(emailTemplate)), HttpStatusCode.OK);
        var mailPath = Path.Combine(_mail, "company-previews");
        var sample = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Assert.Single(Directory.GetFiles(mailPath, "*.json"))));
        Assert.Equal("customer@example.test", sample.GetProperty("recipient").GetString());
        Assert.Equal("preview@example.test", sample.GetProperty("sender").GetString());
        Assert.Equal(1, (await Json(owner, a + "/sender")).GetProperty("sampleHistory").GetArrayLength());

        var layouts = a + "/templates/pdf/credit-memo";
        var layout = (await Json(owner, layouts)).GetProperty("defaultContent").Deserialize<PdfLayout>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await Expect(Mutate(owner, layouts, new TemplateInput("Missing columns", null, layout with { Columns = ["description", "amount"] })), HttpStatusCode.BadRequest);
        await Expect(Mutate(owner, layouts, new TemplateInput("Remote logo", null, layout with { Logo = "https://example.test/a.png" })), HttpStatusCode.BadRequest);
        using var savedPdf = await Mutate(owner, layouts, new TemplateInput("Company PDF", null, layout));
        Assert.Equal(HttpStatusCode.Created, savedPdf.StatusCode);
        var pdfId = (await savedPdf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Expect(Mutate(owner, a + "/templates/" + pdfId + "/duplicate", new DuplicateTemplate("Copied layout")), HttpStatusCode.Created);
        await Expect(Mutate(owner, layouts + "/default", new SelectTemplateDefault(pdfId), HttpMethod.Put), HttpStatusCode.NoContent);
        Assert.Equal(2, (await Json(owner, layouts)).GetProperty("templates").GetArrayLength());
        Assert.Empty((await Json(owner, a + "/templates/pdf/invoice")).GetProperty("templates").EnumerateArray());
        Assert.Empty((await Json(owner, b + "/templates/pdf/credit-memo")).GetProperty("templates").EnumerateArray());
    }

    private WebApplicationFactory<Program> Host(string environment) => _host.WithWebHostBuilder(builder => builder.UseEnvironment(environment)
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:Efiscal"] = _database, ["Identity:DevelopmentMailbox"] = _mail,
            ["Identity:EnableLocalVendorLogin"] = "true"
        })).ConfigureServices(services =>
        {
            services.AddSingleton<IDocumentDeliveryAdapter>(provider => new FaultInjectingDelivery(
                provider.GetRequiredService<IWebHostEnvironment>(), provider.GetRequiredService<IConfiguration>(), this));
            services.AddSingleton<IDocumentPdfRenderer>(provider => new FaultInjectingRenderer(
                provider.GetRequiredService<DocumentPdf>(), this));
        }));

    private HttpClient Browser() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private async Task Db(Func<EfiscalDbContext, Task> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<EfiscalDbContext>());
    }

    private string Mail(string email, string subject) => Directory.GetFiles(_mail, "*.json").OrderDescending().Select(File.ReadAllText)
        .Select(value => JsonSerializer.Deserialize<JsonElement>(value)).Where(item => item.GetProperty("recipient").GetString() == email &&
            item.GetProperty("subject").GetString() == subject)
        .Select(item => new Uri(item.GetProperty("url").GetString()!).Fragment.Split('=')[1]).First();

    private static async Task<HttpResponseMessage> Mutate(HttpClient client, string path, object? body = null, HttpMethod? method = null)
    {
        var csrf = (await Json(client, "/api/auth/csrf")).GetProperty("token").GetString()!;
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path);
        request.Headers.Add("X-EFiscal-CSRF", csrf);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> Json(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Expect(Task<HttpResponseMessage> call, HttpStatusCode code)
    {
        using var response = await call;
        Assert.Equal(code, response.StatusCode);
    }
    private static Task Login(HttpClient client, string email, string password) =>
        Expect(Mutate(client, "/api/auth/login", new { email, password }), HttpStatusCode.NoContent);

    private RegisterRequest Registration(string email) =>
        new(email, "Synthetic Owner", "09000000000", "b2b", "Synthetic Company", _password, _password, true, true, "business", "annual");
    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";
    private User NewIdentity(string email)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User { FullName = "Synthetic Owner", Mobile = "09000000000", Status = IdentityStatus.Verified, VerifiedAt = now,
            CreatedAt = now, DevelopmentFixture = true };
        user.Identity = new UserIdentity { UserId = user.Id, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = new PasswordHasher<User>().HashPassword(user, _password) };
        return user;
    }
    private async Task<(Guid Tenant, Guid Owner, string Email)> SeedSubscriber()
    {
        var email = Email("owner");
        var user = NewIdentity(email);
        var subscriber = new Subscriber { Name = "Synthetic Subscriber", Status = CommercialStatus.Active, PlanId = "business", Billing = "annual",
            CompanyLimit = 3, UserLimit = 10, DevelopmentFixture = true, CreatedAt = DateTimeOffset.UtcNow };
        await Db(async db =>
        {
            db.AddRange(user, subscriber, new SubscriberMembership { SubscriberId = subscriber.Id, UserId = user.Id, RoleId = Permissions.Owner, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        return (subscriber.Id, user.Id, email);
    }
    private static DateOnly ManilaToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Manila").DateTime);
    private static CreateCompanyRequest Form(string code) => new(
        new CompanyInput(code, code + " Synthetic Company", null, "Corporation", "Test location", "company@example.test", "09000000000"),
        new TaxpayerInput("000000000", "VAT", "001", ManilaToday().AddDays(-1), null, null),
        new BranchInput("HeadOffice", "HQ", "Test Head Office", "Test location", null, null, null));
    private static BranchInput Branch(string code) => new("Branch", code, "Synthetic branch", "Test location", null, null, null);
    private static async Task<Guid> CreateCompany(HttpClient client, Guid subscriber, string code)
    {
        using var response = await Mutate(client, $"/api/workspace/{subscriber}/companies", Form(code));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
