using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;

namespace FusionOps.Presentation.BackgroundServices;

public sealed class ExecutionActivationWorker(IServiceScopeFactory scopes, ILogger<ExecutionActivationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                using var tenant = TenantExecution.Enter(TenantScope.System); using var scope = scopes.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ExecutionActivationRunner>();
                for (var i = 0; i < 8 && await runner.RunOnceAsync(stoppingToken); i++) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Execution activation store unavailable; original participant decisions remain recoverable"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
