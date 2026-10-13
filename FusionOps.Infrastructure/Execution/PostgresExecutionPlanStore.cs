using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FusionOps.Infrastructure.Execution;

public sealed class PostgresExecutionPlanStore(IDbContextFactory<FulfillmentContext> factory, IExecutionContext execution,
    TimeProvider clock, ExecutionApprovalPolicy policy) : IExecutionPlanStore
{
    public async Task<ExecutionPlan> CreateAsync(Guid projectId, ExecutionPlanRequest request, CancellationToken ct)
    {
        if (request.Id == Guid.Empty || request.ScenarioId == Guid.Empty || request.ExpectedProjectVersion < 1 || request.PreviousPlanId == Guid.Empty)
            throw new ExecutionPlanRuleException("execution_request_invalid");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { ProjectId = projectId, Request = request }, TemporalInputCodec.Json)));
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await WorkflowPersistence.LockAsync(db, request.Id, "execution-proposal", ct);
        var project = await ProjectAsync(db, projectId, true, ct) ?? throw new ProjectAccessException();
        project.Require(execution.Actor, ProjectPermission.Plan); project.VerifyEvidence();
        var existing = await Plans(db).SingleOrDefaultAsync(p => p.Id == request.Id, ct);
        if (existing is not null)
        {
            if (existing.ProjectId != projectId || existing.Baseline.CreatedBy != execution.Actor || existing.Baseline.RequestHash != hash)
                throw new ExecutionPlanSubmissionConflictException();
            existing.Baseline.VerifyEvidence(); return existing;
        }
        if (project.Version != request.ExpectedProjectVersion) throw new ProjectVersionException();
        var scenario = await db.Set<TemporalScenario>().SingleOrDefaultAsync(s => s.Id == request.ScenarioId && s.ProjectId == projectId, ct)
            ?? throw new ExecutionPlanRuleException("execution_scenario_missing");
        if (scenario.ProjectVersion != project.Version || scenario.DefinitionVersion != project.DefinitionVersion
            || scenario.Evidence.Source.DefinitionDigest != project.DefinitionDigest) throw new ProjectVersionException();
        ExecutionBaseline? previous = null;
        if (request.PreviousPlanId is { } previousId)
            previous = await db.Set<ExecutionBaseline>().SingleOrDefaultAsync(b => b.Id == previousId && b.ProjectId == projectId, ct)
                ?? throw new ExecutionPlanRuleException("execution_previous_plan_missing");
        var baseline = ExecutionBaseline.Capture(request.Id, scenario, execution.Actor, hash, policy, previous, clock.GetUtcNow());
        var (plan, decision) = ExecutionPlan.Propose(baseline);
        db.Add(plan); db.Add(decision); WorkflowPersistence.StageEvents(db, execution.Actor, plan.Id);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" }) { throw new ExecutionPlanSubmissionConflictException(); }
        plan.ClearDomainEvents(); return plan;
    }
    public async Task<ExecutionPlanDetails?> GetAsync(Guid projectId, Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var project = await ProjectAsync(db, projectId, false, ct);
        if (project is null || !project.Can(execution.Actor, ProjectPermission.Read)) return null;
        var plan = await Plans(db).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.ProjectId == projectId, ct);
        if (plan is null) return null;
        plan.Baseline.VerifyEvidence();
        var decisions = await db.Set<ExecutionPlanDecision>().AsNoTracking().Where(d => d.PlanId == id).OrderBy(d => d.Version).ToListAsync(ct);
        return new(plan, decisions);
    }
    public async Task<IReadOnlyList<ExecutionPlanSummary>> ListAsync(Guid projectId, Guid? after, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new ExecutionPlanRuleException("execution_page_invalid");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var project = await ProjectAsync(db, projectId, false, ct) ?? throw new ProjectAccessException();
        project.Require(execution.Actor, ProjectPermission.Read);
        return await db.Set<ExecutionPlan>().AsNoTracking().Where(p => p.ProjectId == projectId && (after == null || p.Id.CompareTo(after.Value) > 0))
            .OrderBy(p => p.Id).Take(limit).Select(p => new ExecutionPlanSummary(p.Id, p.Version, p.State, p.Baseline.Digest, p.Baseline.ScenarioId,
                p.Baseline.DefinitionVersion, p.Baseline.CreatedBy, p.Baseline.CreatedAt, p.Baseline.TotalCost, p.Baseline.Currency, p.ApprovalExpiresAt)).ToListAsync(ct);
    }
    public async Task<ExecutionPlan?> MutateAsync(Guid projectId, Guid id, long expectedVersion, ExecutionPlanMutation mutation,
        string reviewedDigest, string? reason, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Every project mutation uses parent-first order; role changes cannot race admission.
        var project = await ProjectAsync(db, projectId, true, ct) ?? throw new ProjectAccessException();
        project.Require(execution.Actor, mutation == ExecutionPlanMutation.Withdraw ? ProjectPermission.Plan : ProjectPermission.Approve);
        var rows = await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM ops_execution_plans WHERE \"TenantId\" = {db.TenantId} AND \"ProjectId\" = {projectId} AND \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
        if (rows.Count == 0) return null;
        var plan = await Plans(db).SingleAsync(p => p.Id == id && p.ProjectId == projectId, ct);
        if (plan.Version != expectedVersion) throw new ExecutionPlanVersionException();
        project.VerifyEvidence();
        if (mutation != ExecutionPlanMutation.Withdraw && (project.DefinitionVersion != plan.Baseline.DefinitionVersion || project.DefinitionDigest != plan.Baseline.DefinitionDigest))
            throw new ProjectVersionException();
        var decision = plan.Decide(mutation, reviewedDigest, execution.Actor, reason, clock.GetUtcNow());
        db.Add(decision); WorkflowPersistence.StageEvents(db, execution.Actor, id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); plan.ClearDomainEvents(); return plan;
    }
    private static IQueryable<ExecutionPlan> Plans(FulfillmentContext db) => db.Set<ExecutionPlan>().Include(p => p.Baseline);
    private static async Task<OperationalProject?> ProjectAsync(FulfillmentContext db, Guid projectId, bool locked, CancellationToken ct)
    {
        if (locked)
            await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM ops_projects WHERE \"TenantId\" = {db.TenantId} AND \"Id\" = {projectId} FOR UPDATE").ToListAsync(ct);
        return await db.Set<OperationalProject>().Include(p => p.Members).AsSplitQuery().SingleOrDefaultAsync(p => p.Id == projectId, ct);
    }
}
