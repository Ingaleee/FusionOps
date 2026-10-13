using System.Diagnostics;
using System.Diagnostics.Metrics;
using FusionOps.Domain.Execution;
using Microsoft.Extensions.Logging;

namespace FusionOps.Application.Execution;

public sealed class ExecutionActivationRunner(IExecutionActivationStore store, ITemporalWorkforceParticipant workforce,
    ITemporalLicenseParticipant licenses, ILogger<ExecutionActivationRunner> logger, IExecutionActivationCommitProbe? commitProbe = null)
{
    public const string MeterName = "FusionOps.Execution";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Steps = Meter.CreateCounter<long>("fusionops.execution.activation_steps");
    private static readonly Counter<long> Fenced = Meter.CreateCounter<long>("fusionops.execution.fenced_completions");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("fusionops.execution.activation_step_duration", "ms");
    private static readonly ActivitySource Traces = new("FusionOps");
    public async Task<bool> RunOnceAsync(CancellationToken ct, int maxAttempts = 5)
    {
        var claim = await store.ClaimAsync(TimeSpan.FromSeconds(45), ct); if (claim is null) return false;
        var a = claim.Activation; var step = a.State; var watch = Stopwatch.StartNew();
        using var activity = Traces.StartActivity("execution.activation-step", ActivityKind.Internal);
        activity?.SetTag("execution.plan_id", a.Id).SetTag("execution.step", step.ToString()).SetTag("execution.lease_epoch", claim.Epoch);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct); attempt.CancelAfter(TimeSpan.FromSeconds(20));
        ActivationStepResult result;
        try
        {
            switch (step)
            {
                case ActivationState.ReservingResources:
                    var resources = await workforce.ReserveAsync(a.Baseline, attempt.Token);
                    if (resources.Succeeded)
                    {
                        var expected = a.Baseline.ScenarioEvidence.Result.Tasks.SelectMany(t => t.Resources.Select(r => (t.TaskId, r.ResourceId))).ToHashSet();
                        if (resources.Allocations is null || resources.Allocations.Count != expected.Count
                            || resources.Allocations.Any(r => r.AllocationId == Guid.Empty)
                            || resources.Allocations.Select(r => r.AllocationId).Distinct().Count() != expected.Count
                            || !expected.SetEquals(resources.Allocations.Select(r => (r.TaskId, r.ResourceId)))) throw new InvalidDataException("Invalid original resource ownership result");
                    }
                    result = resources.Succeeded ? new(ActivationStepOutcome.Confirmed, Resources: resources.Allocations.Select(r => new ExecutionResourceOwnership(r.TaskId, r.ResourceId, r.AllocationId)).ToArray()) : new(ActivationStepOutcome.Rejected, resources.RejectionCode); break;
                case ActivationState.ReservingLicenses:
                    var seats = await licenses.ReserveAsync(a.Baseline, attempt.Token);
                    result = seats.Succeeded ? new(ActivationStepOutcome.Confirmed) : new(ActivationStepOutcome.Rejected, seats.RejectionCode); break;
                case ActivationState.ReleasingLicenses:
                    await licenses.ReleaseAsync(a.Baseline, attempt.Token); result = new(ActivationStepOutcome.Confirmed); break;
                case ActivationState.ReleasingResources:
                    await workforce.ReleaseAsync(a.Baseline, attempt.Token); result = new(ActivationStepOutcome.Confirmed); break;
                default: throw new InvalidDataException("Leased activation step is not executable");
            }
            if (result.Outcome == ActivationStepOutcome.Confirmed && step is ActivationState.ReservingResources or ActivationState.ReservingLicenses && commitProbe is not null)
                await commitProbe.ObservedAsync(a.Id, step, attempt.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "Execution effect unresolved for plan {PlanId}, step {Step}", a.Id, step);
            activity?.SetStatus(ActivityStatusCode.Error, error.GetType().Name); result = new(ActivationStepOutcome.Unresolved, error.GetType().Name);
        }
        var completed = await store.CompleteAsync(claim, result, maxAttempts, ct);
        var tag = new KeyValuePair<string, object?>("step", step.ToString());
        if (completed) Steps.Add(1, tag, new("outcome", result.Outcome.ToString())); else Fenced.Add(1, tag);
        Duration.Record(watch.Elapsed.TotalMilliseconds, tag); return true;
    }
}
