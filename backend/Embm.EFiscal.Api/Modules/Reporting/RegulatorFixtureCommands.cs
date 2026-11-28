using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal static class RegulatorFixtureCommands
{
    internal sealed record GrantInput(string Email, string Scope, Guid? SubscriberId, Guid? CompanyId, Guid? BranchId,
        string? RdoCode, string[] Categories, bool CanExport, string Reference, DateTimeOffset StartsAt, DateTimeOffset ExpiresAt);

    internal static bool IsCommand(string command) =>
        command is "--bootstrap-local-regulator" or "--grant-local-regulator" or "--revoke-local-regulator";

    internal static async Task Run(WebApplication app, IServiceProvider services, EfiscalDbContext db, string command, string argument)
    {
        if (!RegulatorAccess.Enabled(app.Environment, app.Configuration))
            throw new InvalidOperationException("Enable the local regulator fixture explicitly.");

        switch (command)
        {
            case "--bootstrap-local-regulator":
                var password = app.Configuration["Identity:LocalRegulatorPassword"];
                if (!SyntheticEmail(argument) || !IdentitySecurity.ValidPassword(password))
                    throw new InvalidOperationException("Supply a .test address and an operator-selected fixture password.");
                if (await db.UserIdentities.AnyAsync(x => x.NormalizedEmail == ApiSupport.NormalizeEmail(argument)))
                    throw new InvalidOperationException("This address is already registered.");
                var user = IdentitySecurity.CreateUser(services.GetRequiredService<IPasswordHasher<User>>(), argument,
                    "Synthetic Regulator", "", password!, verified: true);
                user.Kind = IdentityKind.Regulator;
                user.DevelopmentFixture = true;
                db.Add(user);
                db.Audit("Regulator.FixtureBootstrapped", target: user.Id);
                await db.SaveChangesAsync();
                break;
            case "--grant-local-regulator":
                var input = JsonSerializer.Deserialize<GrantInput>(await File.ReadAllTextAsync(argument), DocumentRules.Json)
                    ?? throw new ArgumentException("Supply a fixture grant JSON file.");
                Console.WriteLine(await Grant(db, input));
                break;
            case "--revoke-local-regulator":
                if (!Guid.TryParse(argument, out var grantId)) throw new ArgumentException("Supply a fixture grant ID.");
                await Revoke(db, grantId);
                break;
        }
    }

    internal static async Task<Guid> Grant(EfiscalDbContext db, GrantInput input)
    {
        var now = DateTimeOffset.UtcNow;
        if (!SyntheticEmail(input.Email) || input.Categories is not { Length: > 0 and <= 5 } ||
            input.Categories.Distinct().Count() != input.Categories.Length || !input.Categories.All(RegulatorAccess.Categories.Contains) ||
            !ApiSupport.Text(input.Reference, 160) || input.StartsAt >= input.ExpiresAt ||
            input.ExpiresAt <= now || input.ExpiresAt > now.AddDays(90))
            throw new ArgumentException("Use a synthetic identity, explicit record categories, reference and expiration within 90 days.");

        var scopeValid = input.Scope switch
        {
            "Company" => input.SubscriberId.HasValue && input.CompanyId.HasValue && input.RdoCode is null &&
                await db.Subscribers.AnyAsync(x => x.Id == input.SubscriberId && x.DevelopmentFixture) &&
                await db.TaxpayerProfiles.AnyAsync(x => x.SubscriberId == input.SubscriberId && x.CompanyId == input.CompanyId) &&
                (!input.BranchId.HasValue || await db.Branches.AnyAsync(x => x.Id == input.BranchId &&
                    x.SubscriberId == input.SubscriberId && x.CompanyId == input.CompanyId)),
            "Rdo" => input.SubscriberId is null && input.CompanyId is null && input.BranchId is null &&
                await db.RdoReferences.AnyAsync(x => x.Code == input.RdoCode && x.DevelopmentFixture && x.Active),
            "Nationwide" => input.SubscriberId is null && input.CompanyId is null && input.BranchId is null && input.RdoCode is null,
            _ => false
        };
        if (!scopeValid) throw new ArgumentException("Choose a valid synthetic company, RDO or explicit nationwide scope.");
        var email = ApiSupport.NormalizeEmail(input.Email);
        var user = await db.UserIdentities.SingleOrDefaultAsync(x =>
            x.NormalizedEmail == email && x.User.Kind == IdentityKind.Regulator &&
            x.User.Status == IdentityStatus.Verified && x.User.DevelopmentFixture)
            ?? throw new ArgumentException("The synthetic regulator identity does not exist.");
        var grant = new RegulatorGrant { UserId = user.UserId, Scope = input.Scope, SubscriberId = input.SubscriberId,
            CompanyId = input.CompanyId, BranchId = input.BranchId, RdoCode = input.RdoCode, Categories = input.Categories,
            CanExport = input.CanExport, Reference = input.Reference.Trim(), StartsAt = input.StartsAt.ToUniversalTime(),
            ExpiresAt = input.ExpiresAt.ToUniversalTime() };
        db.Add(grant);
        db.Audit("Regulator.FixtureGrantIssued", subscriber: grant.SubscriberId, target: grant.Id);
        await db.SaveChangesAsync();
        return grant.Id;
    }

    internal static async Task Revoke(EfiscalDbContext db, Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var count = await db.Set<RegulatorGrant>().Where(x => x.Id == id && x.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        if (count == 0) throw new ArgumentException("An unrevoked fixture grant was not found.");
        db.Audit("Regulator.FixtureGrantRevoked", target: id);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static bool SyntheticEmail(string? value) =>
        ApiSupport.Email(value) && value!.Trim().EndsWith(".test", StringComparison.OrdinalIgnoreCase);
}
