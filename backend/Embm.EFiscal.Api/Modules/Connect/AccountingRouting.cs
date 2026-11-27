using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed class AccountingRouting(EfiscalDbContext db, ConnectionRuntime runtime)
{
    public static readonly string[] Scenarios = ["Normal", "Transient", "TimeoutAfterWrite", "UncertainAfterWrite", "Variance"];

    public async Task Pin(FiscalDocument document, DocumentOutboxMessage message, DocumentProcessing state, bool automatic, CancellationToken ct)
    {
        if (!document.DevelopmentFixture) return;
        var route = await db.SyncDestinations.AsNoTracking().Include(x => x.Profile).ThenInclude(x => x.Connection)
            .Where(x => x.SubscriberId == document.SubscriberId && x.CompanyId == document.CompanyId && x.DocumentType == document.Type
                && (x.BranchId == null || x.BranchId == document.BranchId))
            .OrderByDescending(x => x.BranchId.HasValue).FirstOrDefaultAsync(ct);
        if (route is null) return;
        if (!route.Enabled || automatic && !route.Automatic) { Update("NotConfigured"); return; }
        var profile = route.Profile;
        if (profile.Disabled || profile.ActiveVersion is not int number) { Update("AwaitingMapping"); return; }
        var provider = await runtime.Active(document.SubscriberId, document.CompanyId, profile.ConnectionId, [profile.Operation], false, ct);
        if (provider is null || provider.Connection.ResourceType != "CompanyFile" || provider.Connection.BranchId is Guid branch && branch != document.BranchId)
        { Update("AwaitingConnection"); return; }
        if (!await ClientConnectEndpoints.Versions(db, profile).AnyAsync(x => x.Number == number && x.Validated &&
            x.ConnectorVersionId == profile.Connection.ConnectorVersionId && x.ActivatedAt != null, ct))
        { Update("AwaitingMapping"); return; }
        var response = await db.MappingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == document.SubscriberId &&
            x.CompanyId == document.CompanyId && x.ConnectionId == profile.ConnectionId && x.DocumentType == document.Type
            && x.Direction == FieldMapper.Response && x.ActiveVersion != null && !x.Disabled, ct);
        db.SyncOperations.Add(new SyncOperation { Id = message.Id, SubscriberId = document.SubscriberId, CompanyId = document.CompanyId,
            DocumentId = document.Id, ConnectionId = profile.ConnectionId, ConnectorVersionId = profile.Connection.ConnectorVersionId,
            ProfileId = profile.Id, MappingNumber = number, Scenario = route.Scenario,
            ResponseProfileId = response?.Id, ResponseMappingNumber = response?.ActiveVersion });
        message.State = "Pending";
        message.AvailableAt = DateTimeOffset.UtcNow;
        state.Accounting = "Queued";

        void Update(string status) { message.State = status; state.Accounting = status; }
    }
}

