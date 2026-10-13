using System.Text.Json;
using EventStore.Client;
using FusionOps.Domain.Events;
using FusionOps.Infrastructure.Outbox;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.Postgres.Models;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Projector;

/// <summary>Projection rows and source checkpoints commit together. Inclusive replay is safe by event identity.</summary>
public sealed class AuditProjector(
    EventStoreClient events, IDbContextFactory<FulfillmentContext> contexts,
    ILogger<AuditProjector> logger) : BackgroundService
{
    private const string CheckpointName = "allocation-history-v1";

    public async Task<int> ProjectBatchAsync(CancellationToken cancellationToken)
    {
        using var system = FusionOps.Application.Abstractions.TenantExecution.Enter(FusionOps.Application.Abstractions.TenantScope.System);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var checkpoint = await context.Set<ProjectionCheckpoint>().FindAsync([CheckpointName], cancellationToken);
        var position = checkpoint is null ? Position.Start : new Position((ulong)checkpoint.CommitPosition, (ulong)checkpoint.PreparePosition);
        var processed = 0;
        await foreach (var entry in events.ReadAllAsync(Direction.Forwards, position, maxCount: 100, cancellationToken: cancellationToken))
        {
            var sourcePosition = entry.OriginalPosition!.Value;
            if (checkpoint is not null && sourcePosition.CommitPosition == (ulong)checkpoint.CommitPosition
                && sourcePosition.PreparePosition == (ulong)checkpoint.PreparePosition) continue;
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            if ((entry.Event.EventStreamId.StartsWith("audit-event-", StringComparison.Ordinal) || entry.Event.EventStreamId.StartsWith("audit-tenant-", StringComparison.Ordinal)) && entry.Event.EventType == "v1.ResourceAllocated")
            {
                var envelope = JsonSerializer.Deserialize<AuditEnvelope<ResourceAllocated>>(entry.Event.Data.Span, DomainEventSerializer.Options)
                    ?? throw new InvalidOperationException("Invalid allocation audit envelope");
                var payload = envelope.Payload;
                var tenant = envelope.TenantId ?? FusionOps.Domain.Tenancy.TenantIdentity.Legacy;
                var expectedStream = envelope.TenantId is null ? $"audit-event-{envelope.Id:N}" : $"audit-tenant-{tenant:N}-event-{envelope.Id:N}";
                if (tenant == Guid.Empty || entry.Event.EventStreamId != expectedStream || payload.Id != envelope.Id || entry.Event.EventId != Uuid.FromGuid(envelope.Id))
                    throw new InvalidOperationException("Audit tenant, stream or event identity disagrees; checkpoint cannot advance");
                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO allocation_history (\"TenantId\", id, allocation_id, project_id, resource_id, from_ts, to_ts, recorded_at, qty) VALUES ({tenant}, {envelope.Id}, {payload.AllocationId.Value}, {payload.ProjectId}, {payload.ResourceId}, {payload.Period.Start.UtcDateTime}, {payload.Period.End.UtcDateTime}, {envelope.OccurredOn}, {0}) ON CONFLICT (id) DO NOTHING", cancellationToken);
                processed++;
            }
            if (checkpoint is null)
            {
                checkpoint = new ProjectionCheckpoint { Name = CheckpointName };
                context.Add(checkpoint);
            }
            checkpoint.CommitPosition = checked((long)sourcePosition.CommitPosition);
            checkpoint.PreparePosition = checked((long)sourcePosition.PreparePosition);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return processed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await ProjectBatchAsync(stoppingToken);
                if (count > 0) logger.LogInformation("Projected {Count} allocation events", count);
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogError(error, "Audit projection failed; checkpoint was not advanced beyond the failed event");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
