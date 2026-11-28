using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Billing;

internal static class ProviderBilling
{
    internal static async Task<IResult> Records(Guid subscriber, int? page, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriber, "Billing.View", ct, existingRecords: true) is not { } grant)
            return TenantAccess.Missing();
        if (page is < 1 or > 10000) return ApiSupport.Invalid("page", "Choose a valid page.");
        var currentPage = page ?? 1;
        var paid = db.CheckoutOrders.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.Payment.ConfirmedAt != null);
        var total = await paid.CountAsync(ct);
        var rows = await paid.OrderByDescending(x => x.Payment.ConfirmedAt).ThenBy(x => x.Id)
            .Skip((currentPage - 1) * 25).Take(25)
            .Select(x => new { x.Id, x.Purpose, x.BillingFrequency, x.PlanVersionId, x.PackageVersionId, x.Amount, x.Currency,
                x.Payment.ConfirmedAt, x.Payment.RefundedAmount, x.Payment.RefundedAt, x.Payment.ProviderTransactionReference,
                x.Payment.Status }).ToArrayAsync(ct);
        return Results.Ok(new
        {
            provider = new { registeredName = "EMBM Services OPC", taxpayerVerification = "AwaitingVerifiedProviderProfile" },
            subscriber = new { grant.Subscriber.Id, billingName = grant.Subscriber.Name },
            page = currentPage, total, productionIssuanceEnabled = false,
            items = rows.Select(x => new { x.Id, x.Purpose, x.BillingFrequency, x.PlanVersionId, x.PackageVersionId, x.Amount,
                x.Currency, x.ConfirmedAt, x.RefundedAmount, x.RefundedAt, x.ProviderTransactionReference,
                paymentStatus = x.Status.ToString(), invoiceReference = (string?)null, tax = (decimal?)null, discount = (decimal?)null,
                preparationStatus = "AwaitingProviderTaxAndIssuanceReview" })
        });
    }
}
