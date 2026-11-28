using System.Text.Json;
using System.Text.RegularExpressions;
using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.Reporting;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Embm.EFiscal.Api.Infrastructure;

internal static class LocalCommands
{
    private sealed record RdoImport(string Source, bool DevelopmentFixture, RdoEntry[] Entries);
    private sealed record RdoEntry(string Code, string Name, bool Active);

    public static async Task RunAsync(WebApplication app, string[] args)
    {
        if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Local maintenance commands require Development.");
        var connection = new NpgsqlConnectionStringBuilder(app.Configuration.GetConnectionString("Efiscal"));
        if (connection.Host is not ("127.0.0.1" or "localhost" or "::1") || connection.Database?.StartsWith("efiscal_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Use a dedicated loopback eFiscal development database.");
        if (args.Length != 2) throw new ArgumentException("Supply a single command argument.");
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfiscalDbContext>();
        if (args[0] == "--import-rdos")
        {
            await ImportRdos(db, args[1]);
            return;
        }
        if (RegulatorFixtureCommands.IsCommand(args[0]))
        {
            await RegulatorFixtureCommands.Run(app, scope.ServiceProvider, db, args[0], args[1]);
            return;
        }
        if (!ApiSupport.Email(args[1])) throw new ArgumentException("Supply a valid development email.");
        if (args[0] == "--bootstrap-local-vendor")
        {
            if (!app.Configuration.GetValue<bool>("Identity:EnableLocalVendorLogin"))
                throw new InvalidOperationException("Enable local vendor login explicitly before bootstrap.");
            var password = app.Configuration["Identity:LocalVendorPassword"];
            if (!IdentitySecurity.ValidPassword(password)) throw new InvalidOperationException("Configure an operator-selected local vendor password.");
            if (await db.UserIdentities.AnyAsync(x => x.NormalizedEmail == ApiSupport.NormalizeEmail(args[1])))
                throw new InvalidOperationException("This address is already registered.");
            var user = IdentitySecurity.CreateUser(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
                args[1], "Local Vendor Administrator", "", password!, verified: true);
            user.Kind = IdentityKind.Vendor;
            user.DevelopmentFixture = true;
            db.Users.Add(user);
            var profile = app.Configuration["Identity:LocalVendorProfile"] ?? "Administrator";
            await ConnectorRules.GrantLocal(db, user.Id, profile);
            db.Audit(profile == "Administrator" ? "LocalVendorBootstrapped" : "LocalVendorBootstrapped." + profile, user.Id);
            await db.SaveChangesAsync();
            return;
        }

        var identity = await db.UserIdentities.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.NormalizedEmail == ApiSupport.NormalizeEmail(args[1]) && x.User.Kind == IdentityKind.Customer && x.User.Status == IdentityStatus.Verified);
        if (identity is null) throw new InvalidOperationException("Register and verify a synthetic customer before provisioning a fixture.");
        var intent = await db.Registrations.FindAsync(identity.UserId);
        if (intent is null || await db.SubscriberMemberships.AnyAsync(x => x.UserId == identity.UserId))
            throw new InvalidOperationException("This fixture requires a verified registration without a subscriber.");
        var (companies, users) = intent.PlanId switch { "starter" => (1, 2), "business" => (3, 10), _ => (20, 50) };
        var subscriber = new Subscriber { Name = intent.BusinessName ?? identity.User.FullName, PlanId = intent.PlanId, Billing = intent.Billing,
            Status = CommercialStatus.Active, CreatedAt = DateTimeOffset.UtcNow, DevelopmentFixture = true,
            CompanyLimit = companies, UserLimit = users };
        identity.User.DevelopmentFixture = true;
        db.Subscribers.Add(subscriber);
        db.SubscriberMemberships.Add(new SubscriberMembership { SubscriberId = subscriber.Id, UserId = identity.UserId, RoleId = Permissions.Owner, CreatedAt = DateTimeOffset.UtcNow });
        db.Audit("LocalSubscriberFixtureProvisioned", identity.UserId, subscriber.Id);
        await db.SaveChangesAsync();
    }

    private static async Task ImportRdos(EfiscalDbContext db, string file)
    {
        var input = JsonSerializer.Deserialize<RdoImport>(await File.ReadAllTextAsync(file), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (input is null || !ApiSupport.Text(input.Source, 250) || input.Entries is not { Length: > 0 and <= 500 } ||
            input.Entries.Any(x => x.Code is null || !Regex.IsMatch(x.Code, "^[0-9]{3}[A-Z]?$") || !ApiSupport.Text(x.Name, 160)) ||
            input.Entries.Select(x => x.Code).Distinct().Count() != input.Entries.Length)
            throw new InvalidOperationException("Invalid RDO reference file.");
        if (!input.DevelopmentFixture && (!Uri.TryCreate(input.Source, UriKind.Absolute, out var source) ||
            source.Scheme != "https" || !(source.Host == "bir.gov.ph" || source.Host.EndsWith(".bir.gov.ph", StringComparison.Ordinal))))
            throw new InvalidOperationException("Non-fixture imports require verified BIR source provenance.");
        foreach (var entry in input.Entries)
        {
            var row = await db.RdoReferences.FindAsync(entry.Code);
            if (row is null)
            {
                row = new RdoReference { Code = entry.Code };
                db.RdoReferences.Add(row);
            }
            else if (!row.DevelopmentFixture && input.DevelopmentFixture)
                throw new InvalidOperationException("A fictional import cannot replace an official reference.");
            row.Name = entry.Name.Trim();
            row.Source = input.Source;
            row.DevelopmentFixture = input.DevelopmentFixture;
            row.Active = entry.Active;
        }
        db.Audit("RdoReferencesImported");
        await db.SaveChangesAsync();
    }
}
