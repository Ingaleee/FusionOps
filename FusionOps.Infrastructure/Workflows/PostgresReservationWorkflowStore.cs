using System.Data;
using FusionOps.Application.Workflows;
using FusionOps.Domain.Workflows;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Workflows;

public sealed class PostgresReservationWorkflowStore(IDbContextFactory<FulfillmentContext> factory, TimeProvider clock)
    : IReservationWorkflowStore
{
    public async Task<ReservationWorkflow> CreateAsync(ReservationWorkflow workflow, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await WorkflowPersistence.LockAsync(context, workflow.Id, "workflow-proposal", ct);
        // Hold an existing aggregate stable while loading its split-query collections.
        await context.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM reservation_workflows WHERE \"Id\" = {workflow.Id} FOR UPDATE").ToListAsync(ct);
        var existing = await Query(context).SingleOrDefaultAsync(w => w.Id == workflow.Id, ct);
        if (existing is not null)
        {
            if (existing.SpecificationHash != workflow.SpecificationHash || existing.SubmittedBy != workflow.SubmittedBy)
                throw new WorkflowSubmissionConflictException();
            return existing;
        }
        context.Add(workflow);
        var events = WorkflowPersistence.StageEvents(context, workflow.SubmittedBy, workflow.Id);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var source in events) source.ClearDomainEvents();
        return workflow;
    }

    public async Task<ReservationWorkflow?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var snapshot = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        return await Query(context).AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<IReadOnlyList<ReservationWorkflow>> ListAsync(WorkflowState? state, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100 || state is { } value && !Enum.IsDefined(value)) throw new ArgumentException("Invalid workflow page");
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var snapshot = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        return await Query(context).AsNoTracking().Where(w => state == null || w.State == state)
            .OrderByDescending(w => w.CreatedAt).ThenBy(w => w.Id).Take(limit).ToListAsync(ct);
    }

    public async Task<ReservationWorkflow?> MutateAsync(Guid id, long expectedVersion, WorkflowMutation mutation,
        string actor, bool requireSeparateApprover, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var ids = await context.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM reservation_workflows WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
        if (ids.Count == 0) return null;
        var workflow = await Query(context).SingleAsync(w => w.Id == id, ct);
        if (workflow.Version != expectedVersion) throw new WorkflowVersionException();
        switch (mutation)
        {
            case WorkflowMutation.Approve: workflow.Approve(actor, requireSeparateApprover, clock.GetUtcNow()); break;
            case WorkflowMutation.Cancel: workflow.RequestCancellation(actor, clock.GetUtcNow()); break;
            case WorkflowMutation.Resume: workflow.Resume(actor, clock.GetUtcNow()); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var events = WorkflowPersistence.StageEvents(context, actor, id);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var source in events) source.ClearDomainEvents();
        return workflow;
    }

    public async Task<WorkflowClaim?> ClaimAsync(TimeSpan leaseDuration, CancellationToken ct)
    {
        if (leaseDuration < TimeSpan.FromSeconds(5) || leaseDuration > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var token = Guid.NewGuid();
        var ids = await context.Database.SqlQuery<Guid>($"""
            WITH candidate AS (
                SELECT "Id" FROM reservation_workflows
                WHERE "State" IN ('ReservingResources','ReservingLicenses','ReleasingLicenses','ReleasingResources')
                  AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= clock_timestamp())
                  AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= clock_timestamp())
                ORDER BY "NextAttemptAt" NULLS FIRST, "CreatedAt", "Id"
                LIMIT 1 FOR UPDATE SKIP LOCKED
            )
            UPDATE reservation_workflows w
            SET "LeaseToken" = {token}, "LeaseEpoch" = w."LeaseEpoch" + 1,
                "LeaseExpiresAt" = clock_timestamp() + make_interval(secs => {leaseDuration.TotalSeconds})
            FROM candidate c WHERE w."Id" = c."Id"
            RETURNING w."Id" AS "Value"
            """).ToListAsync(ct);
        if (ids.Count == 0) return null;
        var id = ids.Single();
        var workflow = await Query(context).SingleAsync(w => w.Id == id, ct);
        workflow.ExpireIfDue(clock.GetUtcNow());
        var epoch = context.Entry(workflow).Property<long>("LeaseEpoch").CurrentValue;
        var events = WorkflowPersistence.StageEvents(context, "workflow-worker", id);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var source in events) source.ClearDomainEvents();
        return new(workflow, token, epoch);
    }

    public async Task<bool> CompleteAsync(WorkflowClaim claim, WorkflowStepResult result, int maxAttempts, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var id = claim.Workflow.Id;
        var step = claim.Workflow.State.ToString();
        var ids = await context.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM reservation_workflows
            WHERE "Id" = {id} AND "LeaseToken" = {claim.Token} AND "LeaseEpoch" = {claim.Epoch}
              AND "LeaseExpiresAt" > clock_timestamp() AND "State" = {step}
            FOR UPDATE
            """).ToListAsync(ct);
        if (ids.Count == 0) return false;
        var workflow = await Query(context).SingleAsync(w => w.Id == id, ct);
        var databaseNow = (await context.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").ToListAsync(ct)).Single();
        if (context.Entry(workflow).Property<DateTimeOffset?>("LeaseExpiresAt").CurrentValue <= databaseNow) return false;
        workflow.CompleteStep(claim.Workflow.State, result, clock.GetUtcNow(), maxAttempts);
        context.Entry(workflow).Property<Guid?>("LeaseToken").CurrentValue = null;
        context.Entry(workflow).Property<DateTimeOffset?>("LeaseExpiresAt").CurrentValue = null;
        var events = WorkflowPersistence.StageEvents(context, "workflow-worker", id);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var source in events) source.ClearDomainEvents();
        return true;
    }

    private static IQueryable<ReservationWorkflow> Query(FulfillmentContext context) => context.Set<ReservationWorkflow>()
        .Include(w => w.Licenses).Include(w => w.Transitions).Include(w => w.Quote).AsSplitQuery();
}
