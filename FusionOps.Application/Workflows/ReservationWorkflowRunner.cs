using System.Diagnostics;
using System.Diagnostics.Metrics;
using FusionOps.Domain.Workflows;
using Microsoft.Extensions.Logging;

namespace FusionOps.Application.Workflows;

public sealed class ReservationWorkflowRunner(IReservationWorkflowStore store, IWorkforceWorkflowParticipant workforce,
    ILicenseWorkflowParticipant licenses, ILogger<ReservationWorkflowRunner> logger)
{
    public const string MeterName = "FusionOps.Workflows";
    private static readonly ActivitySource Traces = new("FusionOps");
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Steps = Meter.CreateCounter<long>("fusionops.workflow.steps");
    private static readonly Counter<long> Fenced = Meter.CreateCounter<long>("fusionops.workflow.fenced_completions");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("fusionops.workflow.step_duration", "ms");

    public async Task<bool> RunOnceAsync(CancellationToken ct, int maxAttempts = 5)
    {
        var claim = await store.ClaimAsync(TimeSpan.FromSeconds(45), ct);
        if (claim is null) return false;
        var workflow = claim.Workflow;
        var step = workflow.State;
        using var activity = Traces.StartActivity("reservation-workflow.step", ActivityKind.Internal);
        activity?.SetTag("workflow.id", workflow.Id).SetTag("workflow.step", step.ToString()).SetTag("workflow.lease_epoch", claim.Epoch);
        var watch = Stopwatch.StartNew();
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(TimeSpan.FromSeconds(20));
        WorkflowStepResult result;
        try
        {
            result = step switch
            {
                WorkflowState.ReservingResources => await RunResourceReserve(workflow, attempt.Token),
                WorkflowState.ReservingLicenses => await RunLicenseReserve(workflow, attempt.Token),
                WorkflowState.ReleasingLicenses => await RunLicenseRelease(workflow, attempt.Token),
                WorkflowState.ReleasingResources => await RunResourceRelease(workflow, attempt.Token),
                _ => throw new InvalidOperationException("The store leased a non-executable step")
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (WorkflowStepRejectedException rejection) { result = WorkflowStepResult.Rejected(rejection.Code); }
        catch (Exception error)
        {
            logger.LogWarning(error, "Workflow step outcome unresolved: {WorkflowId}, {Step}", workflow.Id, step);
            activity?.SetStatus(ActivityStatusCode.Error, error.GetType().Name);
            result = WorkflowStepResult.Retry(error.GetType().Name);
        }
        var committed = await store.CompleteAsync(claim, result, maxAttempts, ct);
        var stepTag = new KeyValuePair<string, object?>("step", step.ToString());
        if (committed) Steps.Add(1, stepTag, new("outcome", result.Kind.ToString()));
        else Fenced.Add(1, stepTag);
        Duration.Record(watch.Elapsed.TotalMilliseconds, stepTag);
        return true;
    }

    private async Task<WorkflowStepResult> RunLicenseReserve(ReservationWorkflow workflow, CancellationToken ct)
    { await licenses.ReserveAsync(workflow, ct); return WorkflowStepResult.Success(); }
    private async Task<WorkflowStepResult> RunLicenseRelease(ReservationWorkflow workflow, CancellationToken ct)
    { await licenses.ReleaseAsync(workflow, ct); return WorkflowStepResult.Success(); }
    private async Task<WorkflowStepResult> RunResourceRelease(ReservationWorkflow workflow, CancellationToken ct)
    { await workforce.ReleaseAsync(workflow, ct); return WorkflowStepResult.Success(); }
    private async Task<WorkflowStepResult> RunResourceReserve(ReservationWorkflow workflow, CancellationToken ct)
    {
        var ids = await workforce.ReserveAsync(workflow, ct);
        if (ids is null || ids.Count != workflow.ResourceIds.Count || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            throw new InvalidDataException("The resource participant returned an invalid receipt");
        return WorkflowStepResult.Success(ids);
    }
}
