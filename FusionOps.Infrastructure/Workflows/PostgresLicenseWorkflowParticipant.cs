using FusionOps.Application.Workflows;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Shared;
using FusionOps.Domain.Workflows;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using FusionOps.Application.Abstractions;

namespace FusionOps.Infrastructure.Workflows;

public sealed class PostgresLicenseWorkflowParticipant(IDbContextFactory<FulfillmentContext> factory,
    TimeProvider clock) : ILicenseWorkflowParticipant
{
    private const string ReserveStep = "reserve-licenses/v1";
    private const string ReleaseStep = "release-licenses/v1";
    public Task ReserveAsync(ReservationWorkflow workflow, CancellationToken ct) => ExecuteAsync(workflow, false, ct);
    public Task ReleaseAsync(ReservationWorkflow workflow, CancellationToken ct) => ExecuteAsync(workflow, true, ct);

    private async Task ExecuteAsync(ReservationWorkflow workflow, bool release, CancellationToken ct)
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(workflow.TenantId));
        await using var context = await factory.CreateDbContextAsync(ct);
        context.BindScope(TenantScope.For(workflow.TenantId));
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        // The same transaction-owned lock serializes every license action for this workflow.
        await WorkflowPersistence.LockAsync(context, workflow.Id, "license-participant", ct);
        var step = release ? ReleaseStep : ReserveStep;
        var receipt = await context.Set<WorkflowEffectReceipt>().FindAsync([workflow.Id, step], ct);
        if (receipt is not null)
        {
            CheckFingerprint(receipt, workflow);
            if (!receipt.Succeeded) throw new WorkflowStepRejectedException(receipt.RejectionCode!);
            return;
        }
        if (release)
        {
            var reservation = await context.Set<WorkflowEffectReceipt>().FindAsync([workflow.Id, ReserveStep], ct)
                ?? throw new DomainException("The license reservation receipt is missing");
            CheckFingerprint(reservation, workflow);
            if (!reservation.Succeeded) throw new DomainException("The license participant recorded a rejected reservation");
        }
        string? rejection = null;
        try
        {
            foreach (var requirement in workflow.Licenses.OrderBy(l => l.Product, StringComparer.Ordinal))
            {
                var product = requirement.Product;
                var ids = await context.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM license_pools WHERE \"TenantId\" = {context.TenantId} AND \"Product\" = {product} FOR UPDATE").ToListAsync(ct);
                if (ids.Count == 0)
                {
                    if (!release) throw new WorkflowStepRejectedException("license_pool_missing");
                    throw new DomainException("A reserved license pool is missing");
                }
                var pool = await context.Set<LicensePool>().SingleAsync(p => p.Product == product, ct);
                if (release)
                {
                    var owned = pool.Allocations.SingleOrDefault(a => a.ReservationId == workflow.Id);
                    if (owned is null || owned.ProjectId != workflow.ProjectId || owned.Seats != requirement.Seats)
                        throw new DomainException("The workflow's license ownership changed outside the participant protocol");
                    pool.ReleaseReservation(workflow.Id);
                }
                else
                {
                    try { pool.Reserve(workflow.Id, workflow.ProjectId, requirement.Seats); }
                    catch (LicenseCapacityException) { throw new WorkflowStepRejectedException("license_capacity"); }
                }
            }
        }
        catch (WorkflowStepRejectedException error) when (!release)
        {
            // No changes have been saved. Discard every staged pool/event but keep the locks.
            // A durable negative receipt prevents a delayed worker from succeeding later.
            context.ChangeTracker.Clear();
            rejection = error.Code;
        }
        context.Add(new WorkflowEffectReceipt
        {
            WorkflowId = workflow.Id, Step = step, SpecificationHash = workflow.SpecificationHash,
            CompletedAt = clock.GetUtcNow(), Succeeded = rejection is null, RejectionCode = rejection
        });
        var events = WorkflowPersistence.StageEvents(context, new WorkflowExecutionContext(workflow.Id).Actor, workflow.Id);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var source in events) source.ClearDomainEvents();
        if (rejection is not null) throw new WorkflowStepRejectedException(rejection);
    }

    private static void CheckFingerprint(WorkflowEffectReceipt receipt, ReservationWorkflow workflow)
    {
        if (receipt.SpecificationHash != workflow.SpecificationHash) throw new IdempotencyConflictException();
    }
}
