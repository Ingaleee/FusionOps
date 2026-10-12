using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FusionOps.Presentation.Infrastructure;

public sealed class DatabaseReadinessHealthCheck(
    IDbContextFactory<WorkforceContext> workforce, IDbContextFactory<FulfillmentContext> fulfillment) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await using var sql = await workforce.CreateDbContextAsync(timeout.Token);
            await using var postgres = await fulfillment.CreateDbContextAsync(timeout.Token);
            await sql.OutboxMessages.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.OutboxMessages.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            return HealthCheckResult.Healthy("Both command stores and their outbox schemas are available");
        }
        catch (Exception error) { return HealthCheckResult.Unhealthy("A command store or schema is unavailable", error); }
    }
}
