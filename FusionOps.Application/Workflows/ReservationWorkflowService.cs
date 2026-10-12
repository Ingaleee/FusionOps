using FusionOps.Application.Abstractions;
using FusionOps.Domain.Workflows;

namespace FusionOps.Application.Workflows;

public sealed class ReservationWorkflowService(IReservationWorkflowStore store, IExecutionContext execution,
    TimeProvider clock, WorkflowApprovalPolicy approvalPolicy)
{
    public Task<ReservationWorkflow> ProposeAsync(Guid id, ReservationSpecification specification, CancellationToken ct) =>
        store.CreateAsync(ReservationWorkflow.Propose(id, specification, execution.Actor, clock.GetUtcNow()), ct);
    public Task<ReservationWorkflow?> GetAsync(Guid id, CancellationToken ct) => store.GetAsync(id, ct);
    public Task<IReadOnlyList<ReservationWorkflow>> ListAsync(WorkflowState? state, int limit, CancellationToken ct) =>
        store.ListAsync(state, limit, ct);
    public Task<ReservationWorkflow?> MutateAsync(Guid id, long version, WorkflowMutation mutation, CancellationToken ct) =>
        store.MutateAsync(id, version, mutation, execution.Actor, approvalPolicy.RequireSeparateApprover, ct);
}
