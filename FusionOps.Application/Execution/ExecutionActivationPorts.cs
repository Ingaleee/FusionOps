using FusionOps.Domain.Execution;

namespace FusionOps.Application.Execution;

public sealed record ActivationClaim(ExecutionActivation Activation, Guid Token, long Epoch);
public sealed record ActivationDetails(ExecutionActivation Activation, IReadOnlyList<ExecutionActivationDecision> Decisions);
/// <summary>Optional controlled verification boundary after an owning-store decision and before coordinator completion.</summary>
public interface IExecutionActivationCommitProbe
{
    Task ObservedAsync(Guid planId, ActivationState step, CancellationToken ct);
}
public interface IExecutionActivationStore
{
    Task<ExecutionActivation> RequestAsync(Guid projectId, Guid planId, long planVersion, string digest, CancellationToken ct);
    Task<ActivationDetails?> GetAsync(Guid projectId, Guid planId, CancellationToken ct);
    Task<ExecutionActivation?> MutateAsync(Guid projectId, Guid planId, long version, ActivationMutation mutation, string? reason, CancellationToken ct);
    Task<ActivationClaim?> ClaimAsync(TimeSpan lease, CancellationToken ct);
    Task<bool> CompleteAsync(ActivationClaim claim, ActivationStepResult result, int maxAttempts, CancellationToken ct);
}
