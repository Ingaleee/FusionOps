using System.Data;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Planning;
using FusionOps.Domain.Catalog;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Outbox;
using FusionOps.Domain.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Planning;

public sealed class SqlResourceCatalogStore(IDbContextFactory<WorkforceContext> factory, IExecutionContext execution) : IResourceCatalogStore
{
    public async Task<CatalogResource> RegisterAsync(Guid id, ResourceKind kind, CatalogEdit edit, CancellationToken ct)
    {
        var resource = CatalogResource.Register(id, edit.Name, kind, edit.HourRate, edit.Skills, edit.EquipmentType);
        if (!edit.IsActive) resource.Revise(edit.Name, edit.HourRate, edit.Skills, edit.EquipmentType, false);
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await CatalogLocks.FindAsync(context, id, ct) is not null) throw new ResourceAdmissionException("resource_exists", id);
        context.Add(resource);
        await SaveAsync(context, ct);
        await tx.CommitAsync(ct);
        resource.ClearDomainEvents();
        return resource;
    }
    public async Task<CatalogResource?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.ResourceCatalog.AsNoTracking().Include(r => r.Skills).SingleOrDefaultAsync(r => r.Id == id, ct);
    }
    public async Task<IReadOnlyList<CatalogResource>> ListAsync(Guid? after, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new DomainException("Catalog pages support 1–100 records");
        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.ResourceCatalog.AsNoTracking().Include(r => r.Skills).Where(r => after == null || r.Id.CompareTo(after.Value) > 0)
            .OrderBy(r => r.Id).Take(limit).ToListAsync(ct);
    }
    public async Task<CatalogResource?> ReviseAsync(Guid id, long expectedRevision, CatalogEdit edit, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var resource = await CatalogLocks.FindAsync(context, id, ct);
        if (resource is null) return null;
        if (resource.Revision != expectedRevision) throw new CatalogVersionException();
        resource.Revise(edit.Name, edit.HourRate, edit.Skills, edit.EquipmentType, edit.IsActive);
        await SaveAsync(context, ct);
        await tx.CommitAsync(ct);
        resource.ClearDomainEvents();
        return resource;
    }
    private async Task SaveAsync(WorkforceContext context, CancellationToken ct)
    {
        foreach (var domainEvent in context.ChangeTracker.Entries().Select(e => e.Entity).OfType<IHasDomainEvents>().SelectMany(e => e.DomainEvents).ToArray())
            context.OutboxMessages.Add(DomainEventSerializer.Serialize(domainEvent, execution.Actor, execution.CorrelationId));
        await context.SaveChangesAsync(ct);
    }
}
