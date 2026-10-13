using FusionOps.Domain.Catalog;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Planning;

public static class CatalogLocks
{
    public static async Task<CatalogResource?> FindAsync(WorkforceContext context, Guid id, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Catalog locks require a transaction");
        await context.Database.SqlQuery<Guid>($"SELECT Id AS Value FROM ResourceCatalog WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {context.TenantId} AND Id = {id}").ToListAsync(ct);
        return await context.ResourceCatalog.Include(r => r.Skills).SingleOrDefaultAsync(r => r.Id == id, ct);
    }
    public static async Task<CatalogResource> RequireAsync(WorkforceContext context, Guid id, bool requireActive, CancellationToken ct)
    {
        var resource = await FindAsync(context, id, ct) ?? throw new ResourceAdmissionException("resource_unknown", id);
        if (requireActive && !resource.IsActive) throw new ResourceAdmissionException("resource_inactive", id);
        return resource;
    }
}
