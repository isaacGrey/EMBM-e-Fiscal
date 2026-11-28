using System.Security.Claims;
using System.Threading.RateLimiting;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Billing;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Embm.EFiscal.Api.Modules.Reporting;
using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Platform;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var development = builder.Environment.IsDevelopment();
var secure = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Logging.AddJsonConsole();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 3 * 1024 * 1024);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-EFiscal-CSRF";
    options.Cookie.Name = development ? "efiscal-dev-csrf" : "__Host-efiscal-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = secure;
    options.Cookie.Path = "/";
    options.SuppressReadingTokenFromFormBody = true;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = development ? "efiscal-dev-session" : "__Host-efiscal-session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secure;
    options.Cookie.Path = "/";
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = IdentitySecurity.ValidateSessionAsync;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy("customer", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_kind", nameof(IdentityKind.Customer)));
    options.AddPolicy("regulator", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_kind", nameof(IdentityKind.Regulator)));
    options.AddPolicy("vendor", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_kind", nameof(IdentityKind.Vendor)));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("identity", http => RateLimitPartition.GetFixedWindowLimiter(
        (http.Connection.RemoteIpAddress?.ToString() ?? "unknown") + ":" + (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("company-email-preview", http => RateLimitPartition.GetFixedWindowLimiter(
        (http.Connection.RemoteIpAddress?.ToString() ?? "unknown") + ":" + (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("connector-operation", http => RateLimitPartition.GetFixedWindowLimiter(
        (http.Connection.RemoteIpAddress?.ToString() ?? "unknown") + ":" + (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 45, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("csrf", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
builder.Services.AddScoped<IIdentityEmail, IdentityEmail>();
builder.Services.AddScoped<ICompanyMailSampleSink, CompanyMailSampleSink>();
builder.Services.AddScoped<CreditEngine>();
builder.Services.AddScoped<MasterRegistry>();
builder.Services.AddScoped<MasterImport>();
builder.Services.AddSingleton<MasterImportProof>();
builder.Services.AddScoped<FinancialPreview>();
builder.Services.AddScoped<DocumentAssembly>();
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Efiscal")))
    builder.Services.AddHostedService<ReportingOperationWorker>();
builder.Services.AddScoped<DocumentCommitService>();
builder.Services.AddScoped<DocumentRelationships>();
builder.Services.AddScoped<HistoricalCreditSource>();
builder.Services.AddScoped<DocumentSettings>();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<DevelopmentAdapters>();
builder.Services.AddScoped<ConnectionRuntime>();
builder.Services.AddScoped<AccountingRouting>();
if (development)
{
    builder.Services.AddHostedService<AccountingWorker>();
    builder.Services.AddHostedService<ReconciliationWorker>();
    builder.Services.AddScoped<DevelopmentAccountingRead>();
    builder.Services.AddScoped<ReconciliationEngine>();
}
builder.Services.AddScoped<DevelopmentSalesOrders>();
builder.Services.AddScoped<IExternalSalesOrderProvider, ConnectedSalesOrders>();
builder.Services.AddSingleton<DocumentPdf>();
builder.Services.AddSingleton<IDocumentPdfRenderer>(services => services.GetRequiredService<DocumentPdf>());
builder.Services.AddSingleton<IDocumentDeliveryAdapter, DevelopmentDocumentMailbox>();
if (development && !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Efiscal")))
    builder.Services.AddHostedService<DocumentOutboxWorker>();
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Efiscal")))
    builder.Services.AddHostedService<BillingMaintenance>();
if (development)
{
    builder.Services.AddSingleton<DevelopmentPaymentGateway>();
    builder.Services.AddSingleton<IPaymentGateway>(services => services.GetRequiredService<DevelopmentPaymentGateway>());
    builder.Services.AddScoped<CheckoutService>();
}
builder.Services.AddDbContext<EfiscalDbContext>(options =>
{
    var database = builder.Configuration.GetConnectionString("Efiscal");
    if (!string.IsNullOrWhiteSpace(database)) options.UseNpgsql(database);
});

var app = builder.Build();
if (args.Length > 0 && (args[0] is "--provision-dev" or "--bootstrap-local-vendor" or "--import-rdos" || RegulatorFixtureCommands.IsCommand(args[0])))
{
    await LocalCommands.RunAsync(app, args);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (http, next) =>
{
    http.Response.Headers["Referrer-Policy"] = "no-referrer";
    http.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (development) app.MapOpenApi().AllowAnonymous();
app.MapPlatform();
app.MapIdentity();
app.MapTenants();
app.MapCompanyConfiguration();
app.MapTeam();
app.MapBilling();
app.MapMasterData();
app.MapDocuments();
app.MapReporting();
app.MapRegulatorAccess();
app.MapTaxpayerPortability();
app.MapTaxpayerPreservation();
app.MapReportingContinuity();
app.MapRegulatoryAuthorizations();
app.MapVendorRegulatoryReview();
app.MapRegulatoryConnector();
app.MapReportingOperations();
app.MapVendorAccreditation();
app.MapRegulatorySchemas();
app.MapVendorConnect();
app.MapClientConnect();
app.MapFallback("/{**path}", () => Results.NotFound()).AllowAnonymous();
app.Run();

public partial class Program;
