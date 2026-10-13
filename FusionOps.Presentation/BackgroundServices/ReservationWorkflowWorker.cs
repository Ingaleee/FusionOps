using FusionOps.Application.Workflows;

namespace FusionOps.Presentation.BackgroundServices;

public sealed class ReservationWorkflowWorker(IServiceScopeFactory scopes, ILogger<ReservationWorkflowWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                using var tenant = FusionOps.Application.Abstractions.TenantExecution.Enter(FusionOps.Application.Abstractions.TenantScope.System);
                using var scope = scopes.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ReservationWorkflowRunner>();
                for (var i = 0; i < 8 && await runner.RunOnceAsync(stoppingToken); i++) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Reservation workflow store unavailable; committed participant receipts remain replayable"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
