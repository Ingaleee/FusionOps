using FusionOps.Application.Pipelines;
using FusionOps.Application.UseCases.AllocateResource;
using FusionOps.Application.Workflows;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared;
using FusionOps.Domain.Workflows;
using FusionOps.Infrastructure.Idempotency;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Repositories;
using FusionOps.Infrastructure.UnitOfWork;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FusionOps.Domain.Catalog;
using FusionOps.Domain.Services.Planning;
using FusionOps.Infrastructure.Planning;

namespace FusionOps.Infrastructure.Workflows;

public sealed class SqlWorkforceWorkflowParticipant(IDbContextFactory<WorkforceContext> workforceFactory,
    IDbContextFactory<FulfillmentContext> fulfillmentFactory, TimeProvider clock) : IWorkforceWorkflowParticipant
{
    public async Task<IReadOnlyList<Guid>> ReserveAsync(ReservationWorkflow workflow, CancellationToken ct)
    {
        var command = new ReserveWorkflowResources(workflow.Id, workflow.SpecificationHash);
        var result = await ExecuteAsync<ReserveWorkflowResources, WorkflowStepResult>(workflow.Id, command,
            async (context, token) =>
            {
                try
                {
                    var quote = workflow.Quote is null ? null : await SqlQuoteAcceptance.ValidateAsync(context, workflow, token);
                    var ids = await new AllocateHandler(new AllocationRepository(context)).Handle(
                        new AllocateCommand(workflow.ProjectId, workflow.ResourceIds, workflow.Period.Start, workflow.Period.End), token);
                    if (quote is not null) quote.Consume(workflow.Id, await SqlQuoteAcceptance.DatabaseTimeAsync(context, token));
                    return WorkflowStepResult.Success(ids.ToArray());
                }
                catch (AllocationConflictException)
                {
                    // The allocation handler only stages changes. Discard the entire staged batch;
                    // retain the transaction-owned logical/calendar locks and persist the rejection.
                    context.ChangeTracker.Clear();
                    return WorkflowStepResult.Rejected("resource_conflict");
                }
                catch (ResourceAdmissionException error)
                {
                    context.ChangeTracker.Clear();
                    return WorkflowStepResult.Rejected(error.Code);
                }
                catch (QuoteAdmissionException error)
                {
                    context.ChangeTracker.Clear();
                    return WorkflowStepResult.Rejected(error.Code);
                }
            }, ct);
        if (result.Kind == StepResultKind.Rejected) throw new WorkflowStepRejectedException(result.Code!);
        return result.AllocationIds ?? throw new InvalidOperationException("The resource receipt is missing its allocation IDs");
    }

    public async Task ReleaseAsync(ReservationWorkflow workflow, CancellationToken ct)
    {
        var command = new CancelWorkflowResourcesCommand(workflow.ProjectId, workflow.ResourceIds, workflow.AllocationIds,
            workflow.Period, $"workflow.{workflow.Id:N}.release");
        await ExecuteAsync<CancelWorkflowResourcesCommand, bool>(workflow.Id, command,
            (context, token) => new CancelWorkflowResourcesHandler(new AllocationRepository(context)).Handle(command, token), ct);
    }

    private async Task<TResponse> ExecuteAsync<TCommand, TResponse>(Guid id, TCommand command,
        Func<WorkforceContext, CancellationToken, Task<TResponse>> handler, CancellationToken ct)
        where TCommand : IRequest<TResponse>
    {
        await using var workforce = await workforceFactory.CreateDbContextAsync(ct);
        await using var fulfillment = await fulfillmentFactory.CreateDbContextAsync(ct);
        var execution = new WorkflowExecutionContext(id);
        var unit = new EfUnitOfWork(workforce, fulfillment, execution);
        var transaction = new TransactionBehavior<TCommand, TResponse>(unit);
        var idempotency = new IdempotencyBehavior<TCommand, TResponse>(new SqlCommandReceiptStore(workforce, execution, clock));
        return await transaction.Handle(command, () => idempotency.Handle(command,
            () => handler(workforce, ct), ct), ct);
    }

    private sealed record ReserveWorkflowResources(Guid WorkflowId, string SpecificationHash) : ICommand<WorkflowStepResult>, IIdempotentCommand
    {
        public PersistenceBoundary Boundary => PersistenceBoundary.Workforce;
        public string IdempotencyKey => $"workflow.{WorkflowId:N}.reserve";
        public string Operation => "workforce.workflow-reserve/v1";
        public string Fingerprint => SpecificationHash;
    }
}
