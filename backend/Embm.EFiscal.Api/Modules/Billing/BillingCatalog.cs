using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Billing;

internal static class BillingCatalog
{
    private static readonly DateTimeOffset SeedDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Seed(ModelBuilder model)
    {
        string[] core = ["Electronic invoice issuance and delivery", "Philippine tax configuration", "Secure invoice records and audit history"];
        string[] extended = [..core, "Accounting field mapping", "Electronic invoice reconciliation", "BIR reporting readiness"];
        model.Entity<PlanVersion>().HasData(
            Plan(1, "starter", "Starter", "Electronic invoicing for a single company.", 499m, 4990m, 250, 3000, 1, 2, 2, ["autocount", "qbo"], core),
            Plan(2, "business", "Business", "Invoice issuance and reconciliation across company files.", 1499m, 14990m, 1000, 12000, 3, 10, 5, ["autocount", "qbd", "qbo", "accountingsuite", "shopee"], extended),
            Plan(3, "enterprise", "Enterprise", "Larger invoice volumes and broader team access.", 4999m, 49990m, 10000, 120000, 20, 50, 6, ["autocount", "qbd", "qbo", "accountingsuite", "shopee", "tiktok"], extended));
        model.Entity<PackageVersion>().HasData(Package(11, "credits-100", 100, 200m), Package(12, "credits-500", 500, 900m), Package(13, "credits-1000", 1000, 1700m));
    }

    private static PlanVersion Plan(int id, string code, string name, string description, decimal monthly, decimal annual, int credits, int annualCredits,
        int files, int users, int connectors, string[] integrations, string[] features) =>
        new() { Id = Id(id), Code = code, Name = name, Description = description, Version = 1, MonthlyPrice = monthly, AnnualPrice = annual,
            MonthlyCredits = credits, AnnualCredits = annualCredits, CompanyLimit = files, UserLimit = users, ConnectorLimit = connectors,
            Integrations = integrations, ConnectorCategories = id == 1 ? ["Accounting"] : ["Accounting", "Commerce"], Features = features,
            AllocationFrequency = "period", Rollover = true, RenewalAllowed = true, AutoTopUpAllowed = id != 1, Active = true, EffectiveAt = SeedDate };
    private static PackageVersion Package(int id, string code, int credits, decimal amount) =>
        new() { Id = Id(id), Code = code, Version = 1, Name = $"{credits} Save & Send Credits", Credits = credits, Price = amount, Active = true, EffectiveAt = SeedDate };
    private static Guid Id(int n) => Guid.Parse($"e415ca10-0000-4000-8000-{n:D12}");

    public static IQueryable<PlanVersion> PublishedPlans(EfiscalDbContext db, DateTimeOffset now) =>
        db.PlanVersions.AsNoTracking().Where(x => x.EffectiveAt <= now && x.Active &&
            !db.PlanVersions.Any(next => next.Code == x.Code && next.Version > x.Version && next.EffectiveAt <= now));
    public static IQueryable<PackageVersion> PublishedPackages(EfiscalDbContext db, DateTimeOffset now) =>
        db.PackageVersions.AsNoTracking().Where(x => x.EffectiveAt <= now && x.Active &&
            !db.PackageVersions.Any(next => next.Code == x.Code && next.Version > x.Version && next.EffectiveAt <= now));

    public static object PublicPlan(PlanVersion plan) => new { id = plan.Code, versionId = plan.Id, plan.Name, plan.Description, plan.Version,
        billing = new { monthly = new { price = plan.MonthlyPrice, credits = plan.MonthlyCredits }, annual = new { price = plan.AnnualPrice, credits = plan.AnnualCredits } },
        companyFiles = plan.CompanyLimit, users = plan.UserLimit, accountingConnections = plan.ConnectorLimit, plan.Integrations, plan.ConnectorCategories,
        capabilities = plan.Features, renewal = plan.RenewalAllowed, autoTopUps = plan.AutoTopUpAllowed, plan.AllocationFrequency,
        plan.CreditValidityDays, plan.Rollover, plan.GraceDays };
}
