using System.Data;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Events;
using FusionOps.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FusionOps.Infrastructure.Outbox;

public sealed class OutboxProcessor(
    IEventBus bus, IAuditWriter audit, IPublisher notifications,
    TimeProvider clock, ILogger<OutboxProcessor> logger)
{
    public async Task<int> DispatchAsync(DbContext context, int batchSize, int maxAttempts, CancellationToken cancellationToken)
    {
        var dispatched = 0;
        for (var index = 0; index < batchSize && !cancellationToken.IsCancellationRequested; index++)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var now = clock.GetUtcNow().UtcDateTime;
            var query = context.Database.ProviderName switch
            {
                "Microsoft.EntityFrameworkCore.SqlServer" => context.Set<OutboxMessage>().FromSqlInterpolated($"SELECT TOP (1) * FROM [Outbox] WITH (UPDLOCK, READPAST, ROWLOCK) WHERE [ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL AND ([NextAttemptAt] IS NULL OR [NextAttemptAt] <= {now}) ORDER BY [OccurredOn], [Id]"),
                "Npgsql.EntityFrameworkCore.PostgreSQL" => context.Set<OutboxMessage>().FromSqlInterpolated($"SELECT * FROM \"Outbox\" WHERE \"ProcessedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL AND (\"NextAttemptAt\" IS NULL OR \"NextAttemptAt\" <= {now}) ORDER BY \"OccurredOn\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED"),
                _ => throw new NotSupportedException("Dispatch locking requires SQL Server or PostgreSQL")
            };
            var message = (await query.ToListAsync(cancellationToken)).SingleOrDefault();
            if (message is null) break;

            using var delivery = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            delivery.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                using var tenant = TenantExecution.Enter(TenantScope.For(message.TenantId));
                var domainEvent = DomainEventSerializer.Deserialize(message);
                var aggregateId = domainEvent switch
                {
                    ResourceAllocated e => e.AllocationId.Value,
                    ResourceAllocationCancelled e => e.AllocationId.Value,
                    ReservationWorkflowChanged e => e.WorkflowId,
                    CatalogResourceChanged e => e.ResourceId,
                    ProjectChanged e => e.ProjectId,
                    TemporalScenarioCalculated e => e.ScenarioId,
                    ExecutionPlanChanged e => e.PlanId,
                    TemporalLicenseCapacityChanged e => e.PoolId,
                    ExecutionActivationChanged e => e.PlanId,
                    ExecutionTaskCommandRecorded e => e.PlanId,
                    StockReplenished e => e.WarehouseId.Value,
                    ReorderPointReached e => e.WarehouseId.Value,
                    _ => Guid.Empty
                };
                await audit.WriteAsync(new AuditEnvelope<object>(message.Id, message.Type, aggregateId,
                    message.Actor, message.CorrelationId, domainEvent, domainEvent.OccurredOn.UtcDateTime,
                    message.TenantId == FusionOps.Domain.Tenancy.TenantIdentity.Legacy ? null : message.TenantId), delivery.Token);
                await bus.PublishAsync(domainEvent, delivery.Token);
                await notifications.Publish(domainEvent, delivery.Token);
                message.ProcessedAt = clock.GetUtcNow().UtcDateTime;
                message.LastError = null;
                dispatched++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                message.RecordFailure(error, clock.GetUtcNow().UtcDateTime, maxAttempts);
                logger.LogWarning(error, "Outbox delivery failed: {EventId}, attempt {Attempt}", message.Id, message.Attempts);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            OutboxMetrics.RecordCommitted(message, context.Database.ProviderName, clock.GetUtcNow().UtcDateTime);
            context.ChangeTracker.Clear();
        }
        return dispatched;
    }
}
