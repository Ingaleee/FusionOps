using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Events;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Projects;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Execution;
public sealed class PostgresExecutionTaskStore(IDbContextFactory<FulfillmentContext> factory, IExecutionContext execution, IExecutionTaskResourceGuard resources) : IExecutionTaskStore
{
    public async Task<ExecutionTaskFact> ExecuteAsync(Guid projectId, Guid planId, Guid taskId, long version, ExecutionTaskMutation mutation, ExecutionTaskCommand command, CancellationToken ct)
    {
        if (version < 1 || command.CommandId == Guid.Empty || !Enum.IsDefined(mutation)
            || mutation == ExecutionTaskMutation.Start && command.EffectiveAt is not null || mutation == ExecutionTaskMutation.Complete && command.EffectiveAt is null)
            throw new ExecutionPlanRuleException("execution_task_command_invalid");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { projectId, planId, taskId, version, mutation, command, actor = execution.Actor })));
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM ops_projects WHERE \"TenantId\"={db.TenantId} AND \"Id\"={projectId} FOR UPDATE").ToListAsync(ct);
        var project = await db.Set<OperationalProject>().Include(p => p.Members).SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new ProjectAccessException(); project.Require(execution.Actor, ProjectPermission.Execute);
        var rows = await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM ops_execution_activations WHERE \"TenantId\"={db.TenantId} AND \"ProjectId\"={projectId} AND \"Id\"={planId} FOR UPDATE").ToListAsync(ct);
        if (rows.Count == 0) throw new ProjectAccessException();
        var activation = await db.Set<ExecutionActivation>().Include(a => a.Baseline).SingleAsync(a => a.Id == planId, ct); activation.Baseline.VerifyEvidence();
        var existing = await db.Set<ExecutionTaskFact>().FindAsync([planId, taskId, command.CommandId], ct);
        if (existing is not null)
        { if (existing.Actor != execution.Actor || existing.RequestHash != hash) throw new ExecutionActivationConflictException(); return existing; }
        var tasks = await db.Set<ExecutionTask>().Where(t => t.PlanId == planId).ToListAsync(ct);
        var task = tasks.SingleOrDefault(t => t.TaskId == taskId) ?? throw new ProjectAccessException();
        if (task.Version != version) throw new ExecutionPlanVersionException();
        var now = await Now(db, ct); string? rejection;
        if (mutation == ExecutionTaskMutation.Start)
        {
            rejection = project.DefinitionVersion != activation.Baseline.DefinitionVersion || project.DefinitionDigest != activation.Baseline.DefinitionDigest
                ? "execution_definition_changed" : task.StartRejection(activation, tasks, now);
            if (rejection is null) rejection = await CheckLicenses(db, activation, taskId, ct);
            if (rejection is null) rejection = await resources.CheckAsync(activation, taskId, ct);
            now = await Now(db, ct); if (rejection is null) rejection = task.StartRejection(activation, tasks, now);
            if (rejection is null) task.Start(now);
        }
        else
        {
            // Retain true late completion observations, including interval overruns; never pretend the work did not occur.
            rejection = activation.State != ActivationState.Active ? "execution_task_completion_activation" : task.CompletionRejection(command.EffectiveAt!.Value, now);
            if (rejection is null) task.Complete(command.EffectiveAt!.Value, now, activation.Baseline.ScenarioEvidence.Result.Tasks.Single(t => t.TaskId == taskId).Period.End);
        }
        var fact = ExecutionTaskFact.Capture(task, command.CommandId, hash, execution.Actor, mutation, rejection, now); db.Add(fact);
        task.AddDomainEvent(new ExecutionTaskCommandRecorded(planId, projectId, taskId, command.CommandId, mutation, fact.Accepted, rejection, task.Version, execution.Actor) { OccurredOn = fact.RecordedAt });
        if (mutation == ExecutionTaskMutation.Complete && fact.Accepted && tasks.All(t => t.State == ExecutionTaskState.Completed))
        { activation.FinishReportedWork(execution.Actor, tasks, now); db.Add(ExecutionActivationDecision.Capture(activation)); }
        var sources = WorkflowPersistence.StageEvents(db, execution.Actor, planId); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); foreach (var source in sources) source.ClearDomainEvents(); return fact;
    }
    public async Task<IReadOnlyList<ExecutionTaskDetails>?> ListAsync(Guid projectId, Guid planId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var project = await db.Set<OperationalProject>().Include(p => p.Members).SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null || !project.Can(execution.Actor, ProjectPermission.Read) || !await db.Set<ExecutionActivation>().AnyAsync(a => a.Id == planId && a.ProjectId == projectId, ct)) return null;
        var tasks = await db.Set<ExecutionTask>().AsNoTracking().Where(t => t.PlanId == planId).OrderBy(t => t.TaskId).ToListAsync(ct);
        var facts = await db.Set<ExecutionTaskFact>().AsNoTracking().Where(f => f.PlanId == planId).OrderBy(f => f.RecordedAt).ThenBy(f => f.CommandId).ToListAsync(ct);
        return tasks.Select(t => new ExecutionTaskDetails(t, facts.Where(f => f.TaskId == t.TaskId).ToArray())).ToArray();
    }
    private static async Task<string?> CheckLicenses(FulfillmentContext db, ExecutionActivation a, Guid taskId, CancellationToken ct)
    {
        var task = a.Baseline.ScenarioEvidence.Input.Definition.Tasks.Single(t => t.Id == taskId); var scheduled = a.Baseline.ScenarioEvidence.Result.Tasks.Single(t => t.TaskId == taskId);
        foreach (var demand in task.Licenses.OrderBy(l => l.Product, StringComparer.Ordinal))
        {
            var ids = await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM license_pools WHERE \"TenantId\"={db.TenantId} AND \"Product\"={demand.Product} FOR UPDATE").ToListAsync(ct);
            if (ids.Count == 0) return "execution_task_license_missing";
            var pool = await db.Set<LicensePool>().SingleAsync(p => p.Product == demand.Product, ct);
            if (pool.Id != a.Baseline.ScenarioEvidence.Source.Licenses.Single(l => l.Product == demand.Product).PoolId
                || !pool.TemporalReservations.Any(r => r.PlanId == a.Id && r.TaskId == taskId && r.ProjectId == a.ProjectId && r.Seats == demand.Seats && r.Period == scheduled.Period)) return "execution_task_license_ownership_changed";
        }
        return null;
    }
    private static async Task<DateTimeOffset> Now(FulfillmentContext db, CancellationToken ct) => (await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").ToListAsync(ct)).Single();
}
