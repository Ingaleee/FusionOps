using FusionOps.Domain.Execution;
namespace FusionOps.Application.Execution;
public sealed record ExecutionTaskCommand(Guid CommandId, DateTimeOffset? EffectiveAt = null);
public sealed record ExecutionTaskDetails(ExecutionTask Task, IReadOnlyList<ExecutionTaskFact> Facts);
public interface IExecutionTaskStore
{
    Task<ExecutionTaskFact> ExecuteAsync(Guid projectId, Guid planId, Guid taskId, long version, ExecutionTaskMutation mutation, ExecutionTaskCommand command, CancellationToken ct);
    Task<IReadOnlyList<ExecutionTaskDetails>?> ListAsync(Guid projectId, Guid planId, CancellationToken ct);
}
/// <summary>Current owning-store observation, not authority for physical completion or an external effect.</summary>
public interface IExecutionTaskResourceGuard
{
    Task<string?> CheckAsync(ExecutionActivation activation, Guid taskId, CancellationToken ct);
}
