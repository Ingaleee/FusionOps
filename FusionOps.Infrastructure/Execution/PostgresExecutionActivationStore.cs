using System.Data;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Projects;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FusionOps.Infrastructure.Execution;

public sealed class PostgresExecutionActivationStore(IDbContextFactory<FulfillmentContext> factory, IExecutionContext execution) : IExecutionActivationStore
{
    public async Task<ExecutionActivation> RequestAsync(Guid projectId, Guid planId, long planVersion, string digest, CancellationToken ct)
    {
        if (planVersion < 1 || planId == Guid.Empty) throw new ExecutionPlanRuleException("execution_request_invalid");
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var project = await ProjectAsync(db, projectId, true, ct) ?? throw new ProjectAccessException(); project.Require(execution.Actor, ProjectPermission.Execute);
        var existing = await Query(db).SingleOrDefaultAsync(a => a.Id == planId && a.ProjectId == projectId, ct);
        if (existing is not null)
        {
            if (existing.Digest != digest || existing.ApprovedPlanVersion != planVersion || existing.RequestedBy != execution.Actor) throw new ExecutionActivationConflictException();
            existing.Baseline.VerifyEvidence(); return existing;
        }
        if (await db.Set<ExecutionActivation>().AnyAsync(a => a.ProjectId == projectId && a.State != ActivationState.Rejected && a.State != ActivationState.Compensated && a.State != ActivationState.Completed, ct))
            throw new ExecutionActivationConflictException();
        var plan = await db.Set<ExecutionPlan>().Include(p => p.Baseline).SingleOrDefaultAsync(p => p.Id == planId && p.ProjectId == projectId, ct)
            ?? throw new ProjectAccessException();
        if (plan.Version != planVersion) throw new ExecutionPlanVersionException();
        var now = await NowAsync(db, ct); project.VerifyEvidence();
        RequireAuthority(project, plan, execution.Actor, now);
        var activation = ExecutionActivation.Request(plan, digest, execution.Actor, now); db.Add(activation); Stage(db, activation);
        db.AddRange(ExecutionTask.PlanAll(activation.Baseline));
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" }) { throw new ExecutionActivationConflictException(); }
        activation.ClearDomainEvents(); return activation;
    }
    public async Task<ActivationDetails?> GetAsync(Guid projectId, Guid planId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var project = await ProjectAsync(db, projectId, false, ct); if (project is null || !project.Can(execution.Actor, ProjectPermission.Read)) return null;
        var activation = await Query(db).AsNoTracking().SingleOrDefaultAsync(a => a.Id == planId && a.ProjectId == projectId, ct); if (activation is null) return null;
        activation.Baseline.VerifyEvidence();
        var decisions = await db.Set<ExecutionActivationDecision>().AsNoTracking().Where(d => d.ActivationId == planId).OrderBy(d => d.Version).ToListAsync(ct);
        return new(activation, decisions);
    }
    public async Task<ExecutionActivation?> MutateAsync(Guid projectId, Guid planId, long version, ActivationMutation mutation, string? reason, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var project = await ProjectAsync(db, projectId, true, ct) ?? throw new ProjectAccessException(); project.Require(execution.Actor, ProjectPermission.Execute);
        var ids = await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM ops_execution_activations WHERE \"TenantId\"={db.TenantId} AND \"ProjectId\"={projectId} AND \"Id\"={planId} FOR UPDATE").ToListAsync(ct);
        if (ids.Count == 0) return null;
        var activation = await Query(db).SingleAsync(a => a.Id == planId, ct); if (activation.Version != version) throw new ExecutionPlanVersionException();
        var now = await NowAsync(db, ct);
        switch (mutation)
        {
            case ActivationMutation.Cancel:
                if (activation.WorkCompletionRequested || await db.Set<ExecutionTask>().AnyAsync(t => t.PlanId == planId && t.State != ExecutionTaskState.Planned, ct))
                    throw new ExecutionPlanRuleException("execution_started_work_requires_forward_recovery");
                activation.RequestCancellation(execution.Actor, reason ?? string.Empty, now); break;
            case ActivationMutation.Resume: if (reason is not null) throw new ExecutionPlanRuleException("execution_resume_reason_not_supported"); activation.Resume(execution.Actor, now); break;
            default: throw new ExecutionPlanRuleException("execution_decision_invalid");
        }
        if (activation.DomainEvents.Count > 0) Stage(db, activation);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); activation.ClearDomainEvents(); return activation;
    }
    public async Task<ActivationClaim?> ClaimAsync(TimeSpan lease, CancellationToken ct)
    {
        if (lease < TimeSpan.FromSeconds(5) || lease > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(lease));
        using var tenant = TenantExecution.Enter(TenantScope.System); await using var db = await factory.CreateDbContextAsync(ct); db.BindScope(TenantScope.System);
        await using var tx = await db.Database.BeginTransactionAsync(ct); var token = Guid.NewGuid();
        var ids = await db.Database.SqlQuery<Guid>($"""
            WITH candidate AS (
              SELECT "Id" FROM ops_execution_activations
              WHERE "State" IN ('ReservingResources','ReservingLicenses','ReleasingLicenses','ReleasingResources')
                AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= clock_timestamp())
                AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= clock_timestamp())
              ORDER BY "NextAttemptAt" NULLS FIRST, "CreatedAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            ) UPDATE ops_execution_activations a SET "LeaseToken"={token}, "LeaseEpoch"=a."LeaseEpoch"+1,
                "LeaseExpiresAt"=clock_timestamp()+make_interval(secs => {lease.TotalSeconds})
              FROM candidate c WHERE a."Id"=c."Id" RETURNING a."Id" AS "Value"
            """).ToListAsync(ct);
        if (ids.Count == 0) return null;
        var activation = await Query(db).SingleAsync(a => a.Id == ids[0], ct); activation.Baseline.VerifyEvidence();
        var epoch = db.Entry(activation).Property<long>("LeaseEpoch").CurrentValue; await tx.CommitAsync(ct); return new(activation, token, epoch);
    }
    public async Task<bool> CompleteAsync(ActivationClaim claim, ActivationStepResult result, int maxAttempts, CancellationToken ct)
    {
        using var tenant = TenantExecution.Enter(TenantScope.System); await using var db = await factory.CreateDbContextAsync(ct); db.BindScope(TenantScope.System);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Parent-first, like project membership/definition and reviewed-plan mutations.
        var project = await ProjectCoreAsync(db, claim.Activation.ProjectId, true, ct, claim.Activation.TenantId) ?? throw new InvalidDataException("Activation project missing");
        var id = claim.Activation.Id; var step = claim.Activation.State.ToString();
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM ops_execution_activations WHERE "TenantId"={claim.Activation.TenantId}
              AND "ProjectId"={claim.Activation.ProjectId} AND "Id"={id} AND "State"={step}
              AND "LeaseToken"={claim.Token} AND "LeaseEpoch"={claim.Epoch} AND "LeaseExpiresAt">clock_timestamp() FOR UPDATE
            """).ToListAsync(ct);
        if (ids.Count == 0 || project.TenantId != claim.Activation.TenantId) return false;
        var activation = await Query(db).SingleAsync(a => a.Id == id, ct); var now = await NowAsync(db, ct);
        if (db.Entry(activation).Property<DateTimeOffset?>("LeaseExpiresAt").CurrentValue <= now) return false;
        if (activation.Digest != claim.Activation.Digest) throw new InvalidDataException("Activation evidence changed");
        string? admissionFailure = null;
        if (result.Outcome == ActivationStepOutcome.Confirmed && activation.State is ActivationState.ReservingResources or ActivationState.ReservingLicenses && !activation.CancellationRequested)
        {
            var plan = await db.Set<ExecutionPlan>().Include(p => p.Baseline).SingleAsync(p => p.Id == id && p.TenantId == activation.TenantId, ct);
            try
            {
                RequireAuthority(project, plan, activation.RequestedBy, now);
                if (plan.Version != activation.ApprovedPlanVersion) throw new ExecutionPlanRuleException("execution_approval_changed");
                if (now >= activation.Baseline.ScenarioEvidence.Result.Tasks.Min(t => t.Period.Start)) throw new ExecutionPlanRuleException("execution_activation_window_closed");
            }
            catch (ExecutionPlanRuleException e) { admissionFailure = e.Code; }
            catch (ProjectVersionException) { admissionFailure = "execution_definition_changed"; }
            catch (ProjectAccessException) { admissionFailure = "execution_authority_revoked"; }
        }
        activation.CompleteStep(claim.Activation.State, result, now, maxAttempts, admissionFailure);
        db.Entry(activation).Property<Guid?>("LeaseToken").CurrentValue = null; db.Entry(activation).Property<DateTimeOffset?>("LeaseExpiresAt").CurrentValue = null;
        Stage(db, activation, claim.Epoch); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); activation.ClearDomainEvents(); return true;
    }
    private static void RequireAuthority(OperationalProject project, ExecutionPlan plan, string actor, DateTimeOffset now)
    {
        project.VerifyEvidence(); plan.Baseline.VerifyEvidence(); project.Require(actor, ProjectPermission.Execute); plan.RequireValidApproval(now);
        if (plan.TenantId != project.TenantId || plan.ProjectId != project.Id || project.DefinitionVersion != plan.Baseline.DefinitionVersion || project.DefinitionDigest != plan.Baseline.DefinitionDigest)
            throw new ProjectVersionException();
        if (plan.ApprovedBy is null || !project.Can(plan.ApprovedBy, ProjectPermission.Approve)) throw new ProjectAccessException();
    }
    private static IQueryable<ExecutionActivation> Query(FulfillmentContext db) => db.Set<ExecutionActivation>().Include(a => a.Baseline);
    private static Task<OperationalProject?> ProjectAsync(FulfillmentContext db, Guid id, bool locked, CancellationToken ct) => ProjectCoreAsync(db, id, locked, ct);
    private static async Task<OperationalProject?> ProjectCoreAsync(FulfillmentContext db, Guid id, bool locked, CancellationToken ct, Guid? ownerTenant = null)
    {
        var tenantId = ownerTenant ?? db.TenantId;
        if (locked) await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM ops_projects WHERE \"TenantId\"={tenantId} AND \"Id\"={id} FOR UPDATE").ToListAsync(ct);
        return await db.Set<OperationalProject>().Include(p => p.Members).SingleOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);
    }
    private static async Task<DateTimeOffset> NowAsync(FulfillmentContext db, CancellationToken ct) => (await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").ToListAsync(ct)).Single();
    private static void Stage(FulfillmentContext db, ExecutionActivation activation, long epoch = 0)
    {
        db.Add(ExecutionActivationDecision.Capture(activation, epoch)); WorkflowPersistence.StageEvents(db, epoch == 0 ? activation.DomainEvents.OfType<FusionOps.Domain.Events.ExecutionActivationChanged>().Single().Actor : "execution-worker", activation.Id);
    }
}
