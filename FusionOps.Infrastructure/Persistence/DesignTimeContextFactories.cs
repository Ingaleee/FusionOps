using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FusionOps.Infrastructure.Persistence;

// No host, broker or database connection is required to generate reviewed migration scripts.
public sealed class WorkforceDesignTimeFactory : IDesignTimeDbContextFactory<WorkforceContext>
{
    public WorkforceContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<WorkforceContext>()
        .UseSqlServer("Server=localhost;Database=FusionOpsWorkforce;Integrated Security=true;TrustServerCertificate=true").Options);
}

public sealed class FulfillmentDesignTimeFactory : IDesignTimeDbContextFactory<FulfillmentContext>
{
    public FulfillmentContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<FulfillmentContext>()
        .UseNpgsql("Host=localhost;Database=fusionops;Username=fusionops").Options);
}
