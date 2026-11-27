using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Billing;
using Embm.EFiscal.Api.Modules.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    [PostgresFact]
    public async Task BillingOnlyProvisionsOnceAfterVerifiedSuccessAndKeepsTaxAuthorizationSeparate()
    {
        using var guest = Browser();
        var catalog = await Json(guest, "/api/billing/catalog");
        Assert.True(catalog.GetProperty("checkoutAvailable").GetBoolean());
        Assert.Contains(catalog.GetProperty("plans").EnumerateArray(), x => x.GetProperty("id").GetString() == "business");
        await Expect(Mutate(guest, "/api/billing/checkout", Order("InitialSubscription")), HttpStatusCode.Unauthorized);
        using var buyer = await BillingBuyer();
        var request = Order("InitialSubscription");
        var order = await StartOrder(buyer, "/api/billing", request);
        Assert.Equal("Pending", order.GetProperty("status").GetString());
        var id = order.GetProperty("id").GetGuid();
        Assert.Equal(14990m, order.GetProperty("amount").GetDecimal());
        Assert.Equal(id, (await StartOrder(buyer, "/api/billing", request)).GetProperty("id").GetGuid());
        await Expect(guest.GetAsync($"/api/billing/checkout/{id}"), HttpStatusCode.Unauthorized);
        using var outsider = await BillingBuyer();
        await Expect(outsider.GetAsync($"/api/billing/checkout/{id}"), HttpStatusCode.NotFound);
        await Expect(Mutate(outsider, $"/api/billing/checkout/{id}/simulate/paid"), HttpStatusCode.NotFound);
        await Expect(outsider.GetAsync("/api/vendor/billing/catalog"), HttpStatusCode.Forbidden);
        await Expect(buyer.PostAsync($"/api/billing/checkout/{id}/simulate/paid", null), HttpStatusCode.BadRequest);

        await Db(async db =>
        {
            var row = await db.CheckoutOrders.Include(x => x.Payment).SingleAsync(x => x.Id == id);
            var provider = _app.Services.GetRequiredService<DevelopmentPaymentGateway>();
            var service = new CheckoutService(db, new CreditEngine(db), provider);
            var notification = provider.Sign(new("verify-mismatch", id, row.UserId, null, row.Payment.ProviderPaymentId, row.Amount + 1, "PHP", "paid"));
            await Assert.ThrowsAsync<CommercialConflict>(() => service.Accept(notification with { Signature = new byte[32] }, DateTimeOffset.UtcNow, default));
            await Assert.ThrowsAsync<CommercialConflict>(() => service.Accept(notification, DateTimeOffset.UtcNow, default));
            Assert.False(await db.SubscriberMemberships.AnyAsync(x => x.UserId == row.UserId));
        });

        var processing = await Simulate(buyer, id, "processing");
        Assert.Equal("Processing", processing.GetProperty("status").GetString());
        Assert.Empty((await Json(buyer, "/api/workspace")).GetProperty("subscribers").EnumerateArray());
        var results = await Task.WhenAll(Simulate(buyer, id, "duplicate"), Simulate(buyer, id, "paid"));
        Assert.All(results, result => Assert.Equal("Paid", result.GetProperty("status").GetString()));
        var tenant = results[0].GetProperty("subscriberId").GetGuid();
        var basePath = $"/api/billing/subscribers/{tenant}";
        var summary = await Json(buyer, basePath + "/summary");
        Assert.Equal(12000, summary.GetProperty("wallet").GetProperty("available").GetInt32());
        Assert.Single((await Json(buyer, "/api/workspace")).GetProperty("subscribers").EnumerateArray());
        await Expect(outsider.GetAsync(basePath + "/summary"), HttpStatusCode.NotFound);
        await Expect(outsider.GetAsync(basePath + "/credits?export=true"), HttpStatusCode.NotFound);
        var company = await CreateCompany(buyer, tenant, "BILLING");
        var taxpayer = (await Json(buyer, $"/api/workspace/{tenant}/companies/{company}")).GetProperty("taxpayer");
        Assert.Equal("NotVerified", taxpayer.GetProperty("verificationStatus").GetString());
        Assert.Equal("Disabled", taxpayer.GetProperty("issuanceStatus").GetString());

        await Db(async db =>
        {
            var payment = await db.Payments.SingleAsync(x => x.OrderId == id);
            Assert.Equal(2, await db.PaymentEvents.CountAsync(x => x.PaymentId == payment.Id));
            Assert.Equal(1, await db.CreditLots.CountAsync(x => x.PaymentId == payment.Id));
            var attempt = await Assert.ThrowsAsync<PostgresException>(() => db.CreditLedgerEntries.Where(x => x.PaymentId == payment.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.Quantity, 300)));
            Assert.Equal("P0001", attempt.SqlState);
        });
        using var production = Host("Production");
        using var prod = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await Expect(prod.PostAsync($"/api/billing/checkout/{id}/simulate/paid", null), HttpStatusCode.NotFound);
        await Expect(prod.GetAsync("/api/vendor/billing/catalog"), HttpStatusCode.NotFound);
        Assert.False((await Json(prod, "/api/billing/catalog")).GetProperty("checkoutAvailable").GetBoolean());
    }

    [PostgresFact]
    public async Task TopUpsRefundsRenewalsAndHistoryApplyConfirmedVersionedTerms()
    {
        using var buyer = await BillingBuyer();
        var failed = await StartOrder(buyer, "/api/billing", Order("InitialSubscription"));
        Assert.Equal("Failed", (await Simulate(buyer, failed.GetProperty("id").GetGuid(), "failed")).GetProperty("status").GetString());
        var cancelled = await StartOrder(buyer, "/api/billing", Order("InitialSubscription"));
        Assert.Equal("Cancelled", (await Simulate(buyer, cancelled.GetProperty("id").GetGuid(), "cancelled")).GetProperty("status").GetString());
        var expired = await StartOrder(buyer, "/api/billing", Order("InitialSubscription"));
        var expiredId = expired.GetProperty("id").GetGuid();
        await Db(async db => await db.CheckoutOrders.Where(x => x.Id == expiredId)
            .ExecuteUpdateAsync(x => x.SetProperty(v => v.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.Equal("Expired", (await Simulate(buyer, expiredId, "paid")).GetProperty("status").GetString());
        var initial = await StartOrder(buyer, "/api/billing", Order("InitialSubscription"));
        var confirmed = await Simulate(buyer, initial.GetProperty("id").GetGuid(), "paid");
        var tenant = confirmed.GetProperty("subscriberId").GetGuid();
        var path = $"/api/billing/subscribers/{tenant}";
        var data = await Json(buyer, path + "/summary");
        var package = data.GetProperty("packages")[0].GetProperty("versionId").GetGuid();
        var topUp = await StartOrder(buyer, path, Order("CreditTopUp", package));
        Assert.Equal(200m, topUp.GetProperty("amount").GetDecimal());
        var topUpId = topUp.GetProperty("id").GetGuid();
        Assert.Equal("Paid", (await Simulate(buyer, topUpId, "duplicate")).GetProperty("status").GetString());
        data = await Json(buyer, path + "/summary");
        Assert.Equal(12100, data.GetProperty("wallet").GetProperty("available").GetInt32());
        Assert.Equal("Refunded", (await Simulate(buyer, topUpId, "refund")).GetProperty("status").GetString());
        data = await Json(buyer, path + "/summary");
        Assert.Equal(12000, data.GetProperty("wallet").GetProperty("available").GetInt32());
        var history = await Json(buyer, path + "/payments?status=Refunded&purpose=CreditTopUp&page=1");
        Assert.Equal(topUpId, Assert.Single(history.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var ledger = await Json(buyer, path + "/credits?search=RefundReversal");
        Assert.Single(ledger.GetProperty("items").EnumerateArray());
        using var csv = await buyer.GetAsync(path + "/credits?export=true");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Contains("RefundReversal", await csv.Content.ReadAsStringAsync());

        await Expect(Mutate(buyer, path + "/subscription", new { autoRenew = true }, HttpMethod.Put), HttpStatusCode.BadRequest);
        await Expect(Mutate(buyer, path + "/subscription", new { storeDemoMethod = true, autoRenew = true }, HttpMethod.Put), HttpStatusCode.NoContent);
        await Expect(Mutate(buyer, path + "/auto-top-up", new { enabled = true, threshold = 10, packageVersionId = package,
            maximumAmount = 200m, periodMaximumSpend = 400m, consent = true }, HttpMethod.Put), HttpStatusCode.NoContent);
        var change = await Json(buyer, "/api/billing/catalog");
        var starter = change.GetProperty("plans").EnumerateArray().Single(x => x.GetProperty("id").GetString() == "starter");
        await Expect(Mutate(buyer, path + "/subscription", new { nextPlanVersionId = starter.GetProperty("versionId").GetGuid(), nextFrequency = "monthly" },
            HttpMethod.Put), HttpStatusCode.NoContent);
        await Expect(Mutate(buyer, path + "/checkout", Order("SubscriptionRenewal")), HttpStatusCode.Conflict);
        await CreateCompany(buyer, tenant, "HISTORY");
        await Db(async db => await db.SubscriberSubscriptions.Where(x => x.SubscriberId == tenant)
            .ExecuteUpdateAsync(x => x.SetProperty(v => v.PeriodEnd, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await Expect(Mutate(buyer, $"/api/workspace/{tenant}/companies", Form("EXPIRED")), HttpStatusCode.Conflict);
        Assert.Single((await Json(buyer, $"/api/workspace/{tenant}/companies")).GetProperty("companies").EnumerateArray());
        var renewal = await StartOrder(buyer, path, Order("SubscriptionRenewal"));
        Assert.Equal(499m, renewal.GetProperty("amount").GetDecimal());
        Assert.Equal("Paid", (await Simulate(buyer, renewal.GetProperty("id").GetGuid(), "duplicate")).GetProperty("status").GetString());
        data = await Json(buyer, path + "/summary");
        Assert.Equal("Starter", data.GetProperty("plan").GetProperty("name").GetString());
        Assert.Equal(12250, data.GetProperty("wallet").GetProperty("available").GetInt32());
        Assert.False(data.GetProperty("topUp").GetProperty("enabled").GetBoolean());
    }

    [PostgresFact]
    public async Task OneEligibleCreditCannotBeSpentTwiceAcrossCompanyFiles()
    {
        using var buyer = await BillingBuyer();
        var choice = await StartOrder(buyer, "/api/billing", Order("InitialSubscription"));
        var confirmed = await Simulate(buyer, choice.GetProperty("id").GetGuid(), "paid");
        var subscriber = confirmed.GetProperty("subscriberId").GetGuid();
        var companyA = await CreateCompany(buyer, subscriber, "CREDIT-A");
        var companyB = await CreateCompany(buyer, subscriber, "CREDIT-B");
        var actor = (await Json(buyer, "/api/auth/me")).GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow;
        // Simulate an expiring grant plus one remaining authorized credit, without changing immutable entries.
        await Db(async db =>
        {
            await db.CreditLots.Where(x => x.SubscriberId == subscriber).ExecuteUpdateAsync(x => x.SetProperty(v => v.ExpiresAt, now.AddMinutes(-1)));
            await new CreditEngine(db).Adjust(subscriber, actor, 1, "support-approval", "Synthetic approval for concurrency test", now, default);
        });
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        async Task<bool> Commit(Guid company, Guid doc, string key)
        {
            try
            {
                await Db(async db =>
                {
                    var engine = new CreditEngine(db);
                    await using var tx = await db.Database.BeginTransactionAsync();
                    var reservation = await engine.Reserve(subscriber, company, actor, doc, "SalesInvoice", "CommitAndSend", key, now, default);
                    Assert.NotNull(reservation);
                    await engine.Complete(reservation, true, now, default);
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
                return true;
            }
            catch (CommercialConflict error) when (error.Message.Contains("No Save & Send Credits", StringComparison.Ordinal)) { return false; }
        }
        await Db(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var engine = new CreditEngine(db);
            await Assert.ThrowsAsync<CommercialConflict>(() => engine.Reserve(subscriber, companyA, actor, docA,
                "CreditMemo", "CommitOnly", "wrong-action", now, default));
            var reserved = await engine.Reserve(subscriber, companyA, actor, docA, "CreditMemo", "CommitAndSend", "released", now, default);
            Assert.NotNull(reserved);
            await engine.Complete(reserved, false, now, default);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        });
        var outcomes = await Task.WhenAll(Commit(companyA, docA, "concurrent-a"), Commit(companyB, docB, "concurrent-b"));
        Assert.Single(outcomes, x => x);
        var winner = outcomes[0] ? (companyA, docA, "concurrent-a") : (companyB, docB, "concurrent-b");
        Assert.True(await Commit(winner.Item1, winner.Item2, winner.Item3));
        await Db(async db =>
        {
            var entries = await db.CreditLedgerEntries.Where(x => x.SubscriberId == subscriber).ToListAsync();
            Assert.Equal(12000, Assert.Single(entries, x => x.EventType == "Expiry").Quantity);
            Assert.Single(entries, x => x.EventType == "Consumption");
            Assert.Single(entries, x => x.EventType == "ReservationRelease");
            await using var tx = await db.Database.BeginTransactionAsync();
            Assert.Null(await new CreditEngine(db).Reserve(subscriber, companyA, actor, Guid.NewGuid(),
                "SalesOrder", "CommitOnly", "order-free", now, default));
            await tx.CommitAsync();
        });
    }

    [PostgresFact]
    public async Task VendorPublishesNewTermsAndAnnualCreditsFollowThePaidSchedule()
    {
        using var buyer = await BillingBuyer();
        var oldCheckout = await StartOrder(buyer, "/api/billing", Order("InitialSubscription"));
        var email = Email("vendor-billing");
        await Db(async db =>
        {
            var vendor = NewIdentity(email);
            vendor.Kind = IdentityKind.Vendor;
            db.Users.Add(vendor);
            await db.SaveChangesAsync();
        });
        using var vendorBrowser = Browser();
        await Expect(Mutate(vendorBrowser, "/api/auth/vendor/login", new { email, password = _password }), HttpStatusCode.NoContent);
        await Expect(vendorBrowser.GetAsync("/api/workspace"), HttpStatusCode.Forbidden);
        var code = "annual-" + Guid.NewGuid().ToString("N")[..12];
        var plan = new PublishPlan(code, "Annual allocation test", "Synthetic plan for monthly allocation.", 20.01m, 199.99m, 10, 125,
            "monthly", 2, 3, 1, ["autocount"], ["Accounting"], ["Invoice"], 40, true, true, false, 5, true, DateTimeOffset.UtcNow);
        using var published = await Mutate(vendorBrowser, "/api/vendor/billing/plans", plan);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var v1 = (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var amended = plan with { AnnualPrice = 250.13m, EffectiveAt = DateTimeOffset.UtcNow.AddDays(1) };
        await Expect(Mutate(vendorBrowser, "/api/vendor/billing/plans", amended), HttpStatusCode.OK);
        var catalog = await Json(buyer, "/api/billing/catalog");
        var active = catalog.GetProperty("plans").EnumerateArray().Single(x => x.GetProperty("id").GetString() == code);
        Assert.Equal(v1, active.GetProperty("versionId").GetGuid());
        Assert.Equal(199.99m, active.GetProperty("billing").GetProperty("annual").GetProperty("price").GetDecimal());
        using var annual = await BillingBuyer(code);
        var checkout = await StartOrder(annual, "/api/billing", Order("InitialSubscription"));
        Assert.Equal(199.99m, checkout.GetProperty("amount").GetDecimal());
        var paid = await Simulate(annual, checkout.GetProperty("id").GetGuid(), "paid");
        var subscriber = paid.GetProperty("subscriberId").GetGuid();
        var path = $"/api/billing/subscribers/{subscriber}";
        var summary = await Json(annual, path + "/summary");
        Assert.Equal(11, summary.GetProperty("wallet").GetProperty("available").GetInt32());
        var future = DateTimeOffset.UtcNow.AddMonths(1).AddDays(2);
        var maintenance = new BillingMaintenance(_app.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BillingMaintenance>.Instance);
        await maintenance.ProcessWallets(future, default);
        await maintenance.ProcessWallets(future, default);
        await Db(async db =>
        {
            var wallet = await db.CreditWallets.SingleAsync(x => x.SubscriberId == subscriber);
            Assert.Equal(2, await db.CreditLots.CountAsync(x => x.SubscriberId == subscriber));
            Assert.False(await new CreditEngine(db).HasDiscrepancy(wallet.Id, default));
            Assert.True(await new CreditEngine(db).AllowsConnector(subscriber, "autocount", "Accounting", 0, DateTimeOffset.UtcNow, default));
            Assert.False(await new CreditEngine(db).AllowsConnector(subscriber, "autocount", "Accounting", 1, DateTimeOffset.UtcNow, default));
        });
        var adjustPath = $"/api/vendor/billing/wallets/{subscriber}/adjustments";
        var adjustment = new WalletAdjustment(3, "test-approval", "Synthetic approved correction.");
        await Expect(Mutate(annual, adjustPath, adjustment), HttpStatusCode.Forbidden);
        await Expect(Mutate(vendorBrowser, adjustPath, adjustment), HttpStatusCode.NoContent);
        await Expect(Mutate(vendorBrowser, adjustPath, adjustment), HttpStatusCode.NoContent);
        await Expect(Mutate(vendorBrowser, adjustPath, adjustment with { Quantity = 5 }), HttpStatusCode.Conflict);
        Assert.Contains((await Json(vendorBrowser, "/api/vendor/billing/monitor")).GetProperty("wallets").EnumerateArray(),
            x => x.GetProperty("subscriberId").GetGuid() == subscriber && !x.GetProperty("projectionMismatch").GetBoolean());
        Assert.Equal(14990m, (await Simulate(buyer, oldCheckout.GetProperty("id").GetGuid(), "paid")).GetProperty("amount").GetDecimal());
    }

    private async Task<HttpClient> BillingBuyer(string plan = "business")
    {
        var buyer = Browser();
        var email = Email("billing");
        await Expect(Mutate(buyer, "/api/auth/register", Registration(email) with { PlanId = plan }), HttpStatusCode.Accepted);
        await Expect(Mutate(buyer, "/api/auth/verify-email", new { token = Mail(email, "Verify your email") }), HttpStatusCode.NoContent);
        await Login(buyer, email, _password);
        return buyer;
    }

    private static object Order(string purpose, Guid? package = null) => new { purpose, operationKey = Guid.NewGuid().ToString("N"), packageVersionId = package };

    private static async Task<JsonElement> StartOrder(HttpClient buyer, string path, object request)
    {
        using var response = await Mutate(buyer, path + "/checkout", request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Simulate(HttpClient buyer, Guid order, string outcome)
    {
        using var response = await Mutate(buyer, $"/api/billing/checkout/{order}/simulate/{outcome}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

