using System.Data;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Infrastructure.Outbox;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FusionOps.Infrastructure.UnitOfWork;

public sealed class EfUnitOfWork(
    WorkforceContext workforce, FulfillmentContext fulfillment, IExecutionContext executionContext) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;
    private DbContext? _activeContext;

    public async Task BeginAsync(PersistenceBoundary boundary, CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Nested write transactions are not supported");
        _activeContext = boundary switch
        {
            PersistenceBoundary.Workforce => workforce,
            PersistenceBoundary.Fulfillment => fulfillment,
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        _transaction = await _activeContext.Database.BeginTransactionAsync(
            boundary == PersistenceBoundary.Workforce ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        var context = _activeContext ?? throw new InvalidOperationException("Begin a transaction before committing");
        var transaction = _transaction ?? throw new InvalidOperationException("No active transaction");
        var otherContext = ReferenceEquals(context, workforce) ? (DbContext)fulfillment : workforce;
        if (otherContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("A command cannot write across database boundaries. Use a compensated workflow.");

        var sources = context.ChangeTracker.Entries().Select(e => e.Entity)
            .OfType<IHasDomainEvents>().Where(e => e.DomainEvents.Count > 0).ToArray();
        foreach (var domainEvent in sources.SelectMany(e => e.DomainEvents))
            context.Set<OutboxMessage>().Add(DomainEventSerializer.Serialize(
                domainEvent, executionContext.Actor, executionContext.CorrelationId));

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var source in sources) source.ClearDomainEvents();
        await transaction.DisposeAsync();
        _transaction = null;
        _activeContext = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_transaction is not null)
                await _transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            if (_transaction is not null) await _transaction.DisposeAsync();
            _transaction = null;
            _activeContext = null;
            workforce.ChangeTracker.Clear();
            fulfillment.ChangeTracker.Clear();
        }
    }

}
