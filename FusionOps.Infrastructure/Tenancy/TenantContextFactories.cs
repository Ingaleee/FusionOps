using FusionOps.Application.Abstractions;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Tenancy;

public sealed class TenantWorkforceFactory(string appConnection, string workerConnection) : IDbContextFactory<WorkforceContext>
{
    public WorkforceContext CreateDbContext() => new(new DbContextOptionsBuilder<WorkforceContext>()
        .UseSqlServer(TenantExecution.Current?.IsSystem == true ? workerConnection : appConnection).Options);
}
public sealed class TenantFulfillmentFactory(string appConnection, string workerConnection) : IDbContextFactory<FulfillmentContext>
{
    public FulfillmentContext CreateDbContext() => new(new DbContextOptionsBuilder<FulfillmentContext>()
        .UseNpgsql(TenantExecution.Current?.IsSystem == true ? workerConnection : appConnection).Options);
}
