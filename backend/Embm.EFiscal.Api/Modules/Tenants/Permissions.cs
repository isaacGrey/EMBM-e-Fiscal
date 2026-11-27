using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Embm.EFiscal.Api.Modules.Reporting;

namespace Embm.EFiscal.Api.Modules.Tenants;

public static class Permissions
{
    public const string Owner = "owner";
    public const string Administrator = "company-admin";
    public const string CompanyView = "Company.View";
    public const string CompanyManage = "Company.Manage";
    public const string BranchView = "Branch.View";
    public const string BranchManage = "Branch.Manage";
    public const string UsersInvite = "Users.Invite";
    public const string UsersManage = "Users.Manage";

    private static readonly string[] Reader = [CompanyView, BranchView];
    private static readonly string[] FinancialView = ["Invoice.View", "CreditMemo.View", "SalesOrder.View"];
    private static readonly string[] Encoder = ["Invoice.Create", "Invoice.SaveAndSend", "CreditMemo.SaveAndSend", "SalesOrder.Save"];
    private static readonly string[] Reports = ["Reports.View", Coverage.View];
    private static readonly string[] ReconcileWork = [Coverage.Run, Coverage.Batch, Coverage.Review, Coverage.Export, Coverage.References];
    private static readonly string[] ReconcileAdmin = [Coverage.Manage, Coverage.Approve, Coverage.Policy, Coverage.Schedule, Coverage.Assign, Coverage.Match];
    private static readonly string[] CompanyAdmin = [CompanyManage, BranchManage, UsersInvite, "Integration.Manage", "Mapping.Manage",
        ..RegulatoryAuthorizations.ReaderGrants, RegulatoryAuthorizations.Submit];

    private static readonly string[] MasterRead = [MasterSchema.View, "Customers.View", "Items.View", "TaxGroups.View", "DiscountGroups.View", "PriceLists.View"];
    private static readonly string[] MasterManage = ["Customers.Manage", "Customers.Import", "Items.Manage", "Items.Import", "PriceLists.Manage", MasterSchema.Export];
    private static readonly string[] FinancialManage = ["TaxGroups.Manage", "DiscountGroups.Manage", MasterSchema.Configure, MasterSchema.Custom];

    public static readonly (string Id, string Name, string[] Grants)[] Roles =
    [
        (Owner, "Account Owner", [..Reader, ..FinancialView, ..MasterRead, ..Encoder, ..Reports, ..ReconcileWork, ..ReconcileAdmin, ..CompanyAdmin, ..MasterManage, ..FinancialManage, DocumentEndpoints.ExportPermission, DocumentEndpoints.ResendPermission, MasterSchema.Calculate, UsersManage, "Billing.View", "Billing.Manage", "Credits.View", "Credits.Purchase"]),
        (Administrator, "Company Administrator", [..Reader, ..FinancialView, ..MasterRead, ..Encoder, ..Reports, ..ReconcileWork, ..ReconcileAdmin, ..CompanyAdmin, ..MasterManage, ..FinancialManage, DocumentEndpoints.ExportPermission, DocumentEndpoints.ResendPermission, MasterSchema.Calculate]),
        ("accountant", "Accountant", [..Reader, ..FinancialView, ..MasterRead, ..Encoder, ..Reports, ..ReconcileWork, ..MasterManage, ..FinancialManage, DocumentEndpoints.ExportPermission, DocumentEndpoints.ResendPermission, MasterSchema.Calculate]),
        ("encoder", "Transaction Encoder", [..Reader, ..FinancialView, ..MasterRead, ..Encoder, MasterSchema.Calculate]),
        ("auditor", "Auditor", [..Reader, ..FinancialView, ..MasterRead, ..Reports, ..RegulatoryAuthorizations.ReaderGrants]),
        ("regulatory-reviewer", "Regulatory Reviewer", [..Reader, ..RegulatoryAuthorizations.ReviewerGrants]),
        ("readonly", "Read-Only User", [..Reader, ..FinancialView, ..MasterRead])
    ];

    public static bool CanAssign(string inviter, string target) =>
        target != Owner && Roles.Any(role => role.Id == target) &&
        (inviter == Owner || (inviter == Administrator && target != Administrator && target != "regulatory-reviewer"));
}
