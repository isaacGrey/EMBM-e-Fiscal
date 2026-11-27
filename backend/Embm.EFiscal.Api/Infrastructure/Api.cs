using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Embm.EFiscal.Api.Modules.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Embm.EFiscal.Api.Infrastructure;

internal static class ApiSupport
{
    public static IResult Problem(int status, string title) => Results.Problem(statusCode: status, title: title);
    public static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    public static Guid UserId(this HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static Guid SessionId(this HttpContext http) => Guid.Parse(http.User.FindFirstValue("sid")!);
    public static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max;
    public static bool Optional(string? value, int max) => value is null || value.Trim().Length <= max;
    public static bool Email(string? value) => Text(value, 254) && new EmailAddressAttribute().IsValid(value!.Trim());
    public static string NormalizeEmail(string value) => value.Trim().ToUpperInvariant();
    public static bool IsUniqueViolation(DbUpdateException error) =>
        error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static void Audit(this EfiscalDbContext db, string action, Guid? actor = null, Guid? subscriber = null, Guid? target = null) =>
        db.SecurityAudits.Add(new SecurityAudit { Action = action, ActorId = actor, SubscriberId = subscriber, TargetId = target, At = DateTimeOffset.UtcNow });

    public static async Task<IDbContextTransaction> LockSubscriberAsync(this EfiscalDbContext db, Guid subscriber, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.LockSubscriberRowAsync(subscriber, ct);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public static Task<List<int>> LockSubscriberRowAsync(this EfiscalDbContext db, Guid subscriber, CancellationToken ct) =>
        db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"Subscribers\" WHERE \"Id\" = {subscriber} FOR UPDATE").ToListAsync(ct);

    public static async ValueTask<object?> Protect(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(http.RequestServices.GetRequiredService<IConfiguration>().GetConnectionString("Efiscal")))
            return Problem(503, "Account database is unavailable");
        if (!HttpMethods.IsGet(http.Request.Method))
        {
            var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
            try { await antiforgery.ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException) { return Problem(400, "Invalid request verification token"); }
        }
        return await next(context);
    }
}
