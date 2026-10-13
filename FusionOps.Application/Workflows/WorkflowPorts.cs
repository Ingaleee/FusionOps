using FusionOps.Domain.Workflows;

namespace FusionOps.Application.Workflows;

public enum WorkflowMutation { Approve, Cancel, Resume }
public sealed record WorkflowClaim(ReservationWorkflow Workflow, Guid Token, long Epoch);
public interface IReservationWorkflowStore
{
    Task<ReservationWorkflow> CreateAsync(ReservationWorkflow workflow, CancellationToken ct);
    Task<ReservationWorkflow?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ReservationWorkflow>> ListAsync(WorkflowState? state, int limit, CancellationToken ct);
    Task<ReservationWorkflow?> MutateAsync(Guid id, long expectedVersion, WorkflowMutation mutation,
        string actor, bool requireSeparateApprover, CancellationToken ct);
    Task<WorkflowClaim?> ClaimAsync(TimeSpan leaseDuration, CancellationToken ct);
    Task<bool> CompleteAsync(WorkflowClaim claim, WorkflowStepResult result, int maxAttempts, CancellationToken ct);
}

public interface IWorkforceWorkflowParticipant
{
    Task<IReadOnlyList<Guid>> ReserveAsync(ReservationWorkflow workflow, CancellationToken ct);
    Task ReleaseAsync(ReservationWorkflow workflow, CancellationToken ct);
}

public interface ILicenseWorkflowParticipant
{
    Task ReserveAsync(ReservationWorkflow workflow, CancellationToken ct);
    Task ReleaseAsync(ReservationWorkflow workflow, CancellationToken ct);
}

/// <summary>Only a participant's confirmed rolled-back business rejection may use this exception.</summary>
public sealed class WorkflowStepRejectedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record WorkflowApprovalPolicy(bool RequireSeparateApprover);
