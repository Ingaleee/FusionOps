using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Workflows;

internal static class WorkflowPersistence
{
    public static Task<int> LockAsync(DbContext context, Guid id, string boundary, CancellationToken ct)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"fusionops:{boundary}:{id:N}"));
        var key = BinaryPrimitives.ReadInt64BigEndian(digest.AsSpan(0, 8));
        return context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key});", ct);
    }

    public static IHasDomainEvents[] StageEvents(DbContext context, string actor, Guid workflowId)
    {
        var sources = context.ChangeTracker.Entries().Select(e => e.Entity).OfType<IHasDomainEvents>()
            .Where(e => e.DomainEvents.Count > 0).ToArray();
        foreach (var domainEvent in sources.SelectMany(e => e.DomainEvents))
            context.Set<OutboxMessage>().Add(DomainEventSerializer.Serialize(domainEvent, actor, $"workflow.{workflowId:N}"));
        return sources;
    }
}
