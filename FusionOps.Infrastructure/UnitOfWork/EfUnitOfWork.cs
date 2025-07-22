using System.Threading.Tasks;
using FusionOps.Domain.Interfaces;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
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
} 