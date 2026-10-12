using FusionOps.Infrastructure.Outbox;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Presentation.BackgroundServices;

public sealed class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            // Isolated scopes let one unavailable store fail without starving the other store.
            await Task.WhenAll(DispatchStoreAsync<WorkforceContext>(stoppingToken), DispatchStoreAsync<FulfillmentContext>(stoppingToken));
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchStoreAsync<TContext>(CancellationToken cancellationToken) where TContext : DbContext
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().DispatchAsync(
                scope.ServiceProvider.GetRequiredService<TContext>(), 25, 5, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { logger.LogError(error, "Outbox store unavailable: {Store}", typeof(TContext).Name); }
    }
}
