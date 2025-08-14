using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace FusionOps.Infrastructure.UnitOfWork;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly WorkforceContext _workforce;
    private readonly FulfillmentContext _fulfillment;
    private IDbContextTransaction? _txn;

    public EfUnitOfWork(WorkforceContext workforce, FulfillmentContext fulfillment)
    {
        _workforce = workforce;
        _fulfillment = fulfillment;
    }

    public async Task CommitAsync()
    {
        _txn ??= await _workforce.Database.BeginTransactionAsync();
        try
        {
            await _workforce.SaveChangesAsync();
            await _fulfillment.SaveChangesAsync();
            await _txn.CommitAsync();
        }
        finally
        {
            await _txn.DisposeAsync();
            _txn = null;
        }
    }

    public async Task RollbackAsync()
    {
        if (_txn != null)
        {
            await _txn.RollbackAsync();
            await _txn.DisposeAsync();
            _txn = null;
        }
    }

    public IReadOnlyCollection<IDomainEvent> GetDomainEventsAndClear()
    {
        var events = new List<IDomainEvent>();

        void CollectFrom(ChangeTracker tracker)
        {
            var sources = tracker.Entries()
                .Where(e => e.Entity is IHasDomainEvents)
                .Select(e => (IHasDomainEvents)e.Entity);

            foreach (var source in sources)
            {
                events.AddRange(source.DomainEvents);
                source.ClearDomainEvents();
            }
        }

        CollectFrom(_workforce.ChangeTracker);
        CollectFrom(_fulfillment.ChangeTracker);

        return events;
    }
}