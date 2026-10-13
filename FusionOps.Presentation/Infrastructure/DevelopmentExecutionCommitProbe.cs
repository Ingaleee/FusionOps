using FusionOps.Application.Execution;
using FusionOps.Domain.Execution;
namespace FusionOps.Presentation.Infrastructure;
/// <summary>Local failure-lab pause. No route can enable it; production refuses the configuration.</summary>
public sealed class DevelopmentExecutionCommitProbe : IExecutionActivationCommitProbe
{
    private readonly ActivationState _step;
    private readonly ILogger<DevelopmentExecutionCommitProbe> _logger;
    public DevelopmentExecutionCommitProbe(IConfiguration configuration, IHostEnvironment environment, ILogger<DevelopmentExecutionCommitProbe> logger)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Execution failure lab requires Development");
        if (!Enum.TryParse<ActivationState>(configuration["Execution:FailureLab:PauseAfter"], out _step)
            || _step is not (ActivationState.ReservingResources or ActivationState.ReservingLicenses)) throw new InvalidOperationException("Select an explicit reservation commit boundary");
        _logger = logger;
    }
    public async Task ObservedAsync(Guid planId, ActivationState step, CancellationToken ct)
    {
        if (step != _step) return;
        _logger.LogWarning("FUSIONOPS_FAILURE_LAB_PARTICIPANT_COMMITTED {PlanId} {Step}; coordinator progress is not yet recorded", planId, step);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }
}
