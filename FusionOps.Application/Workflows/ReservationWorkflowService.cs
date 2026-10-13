using FusionOps.Application.Abstractions;
using FusionOps.Domain.Workflows;
using FusionOps.Application.Planning;
using FusionOps.Domain.Services.Planning;

namespace FusionOps.Application.Workflows;

public sealed class ReservationWorkflowService(IReservationWorkflowStore store, IExecutionContext execution,
    TimeProvider clock, WorkflowApprovalPolicy approvalPolicy, IPlanningQuoteStore quotes, FusionOps.Application.Projects.IProjectAccess access)
{
    public async Task<ReservationWorkflow> ProposeAsync(Guid id, ReservationSpecification specification, CancellationToken ct)
    {
        await access.RequireAsync(specification.ProjectId, FusionOps.Domain.Projects.ProjectPermission.Plan, ct);
        return await store.CreateAsync(ReservationWorkflow.Propose(id, specification, execution.Actor, clock.GetUtcNow(), execution.TenantId), ct);
    }
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
    public async Task<ReservationWorkflow?> GetAsync(Guid id, CancellationToken ct)
    {
        var workflow = await store.GetAsync(id, ct);
        if (workflow is not null) await access.RequireAsync(workflow.ProjectId, FusionOps.Domain.Projects.ProjectPermission.Read, ct);
        return workflow;
    }
    public async Task<IReadOnlyList<ReservationWorkflow>> ListAsync(WorkflowState? state, int limit, CancellationToken ct)
    {
        var visible = new List<ReservationWorkflow>();
        foreach (var workflow in await store.ListAsync(state, limit, ct))
            if (await access.CanAsync(workflow.ProjectId, FusionOps.Domain.Projects.ProjectPermission.Read, ct)) visible.Add(workflow);
        return visible;
    }
    public async Task<ReservationWorkflow?> MutateAsync(Guid id, long version, WorkflowMutation mutation, CancellationToken ct)
    {
        var workflow = await store.GetAsync(id, ct); if (workflow is null) return null;
        await access.RequireAsync(workflow.ProjectId, mutation == WorkflowMutation.Approve ? FusionOps.Domain.Projects.ProjectPermission.Approve
            : FusionOps.Domain.Projects.ProjectPermission.Execute, ct);
        return await store.MutateAsync(id, version, mutation, execution.Actor, approvalPolicy.RequireSeparateApprover, ct);
    }
}
