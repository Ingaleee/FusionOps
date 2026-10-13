using FusionOps.Domain.Execution;

namespace FusionOps.Application.Execution;

public sealed record ExecutionPlanRequest(Guid Id, Guid ScenarioId, long ExpectedProjectVersion, Guid? PreviousPlanId = null);
public sealed record ExecutionPlanSummary(Guid Id, long Version, ExecutionPlanState State, string Digest, Guid ScenarioId,
    long DefinitionVersion, string CreatedBy, DateTimeOffset CreatedAt, decimal TotalCost, string Currency, DateTimeOffset? ApprovalExpiresAt);
public sealed record ExecutionPlanDetails(ExecutionPlan Plan, IReadOnlyList<ExecutionPlanDecision> Decisions);
public interface IExecutionPlanStore
{
    Task<ExecutionPlan> CreateAsync(Guid projectId, ExecutionPlanRequest request, CancellationToken ct);
    Task<ExecutionPlanDetails?> GetAsync(Guid projectId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<ExecutionPlanSummary>> ListAsync(Guid projectId, Guid? after, int limit, CancellationToken ct);
    Task<ExecutionPlan?> MutateAsync(Guid projectId, Guid id, long expectedVersion, ExecutionPlanMutation mutation,
        string reviewedDigest, string? reason, CancellationToken ct);
}
