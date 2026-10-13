using FusionOps.Application.Abstractions;
using FusionOps.Domain.Workflows;
using FusionOps.Application.Planning;
using FusionOps.Domain.Services.Planning;

namespace FusionOps.Application.Workflows;

public sealed class ReservationWorkflowService(IReservationWorkflowStore store, IExecutionContext execution,
    TimeProvider clock, WorkflowApprovalPolicy approvalPolicy, IPlanningQuoteStore quotes)
{
    public Task<ReservationWorkflow> ProposeAsync(Guid id, ReservationSpecification specification, CancellationToken ct) =>
        store.CreateAsync(ReservationWorkflow.Propose(id, specification, execution.Actor, clock.GetUtcNow(), execution.TenantId), ct);
    public async Task<ReservationWorkflow> ProposeQuoteAsync(Guid id, Guid quoteId, IReadOnlyList<LicenseRequirement> licenses,
        int completionTimeoutSeconds, CancellationToken ct)
    {
        var quote = await quotes.GetAsync(quoteId, ct) ?? throw new QuoteAdmissionException("quote_missing");
        var evidence = quote.Evidence;
        if (!evidence.Result.IsComplete || evidence.Result.Assignments.Count == 0) throw new QuoteAdmissionException("quote_incomplete");
        var specification = new ReservationSpecification(evidence.Request.ProjectId,
            evidence.Result.Assignments.Select(a => a.ResourceId).ToArray(), evidence.Request.Period, licenses, completionTimeoutSeconds, quote.Binding);
        return await ProposeAsync(id, specification, ct);
    }
    public Task<ReservationWorkflow?> GetAsync(Guid id, CancellationToken ct) => store.GetAsync(id, ct);
    public Task<IReadOnlyList<ReservationWorkflow>> ListAsync(WorkflowState? state, int limit, CancellationToken ct) =>
        store.ListAsync(state, limit, ct);
    public Task<ReservationWorkflow?> MutateAsync(Guid id, long version, WorkflowMutation mutation, CancellationToken ct) =>
        store.MutateAsync(id, version, mutation, execution.Actor, approvalPolicy.RequireSeparateApprover, ct);
}
