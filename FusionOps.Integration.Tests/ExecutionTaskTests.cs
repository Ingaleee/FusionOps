using System.Net;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Projects;
using FusionOps.Infrastructure.Execution;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FusionOps.Integration.Tests;
public sealed partial class ReliabilityTests
{
    private PostgresExecutionTaskStore TaskStore(string actor = "operator") => new(new TestContextFactory(_fulfillmentOptions), new TestExecutionContext(actor), new SqlExecutionTaskResourceGuard(new WorkforceContextFactory(_workforceOptions)));
    private static DateTimeOffset TaskNow() { var now = DateTimeOffset.UtcNow; return new(now.UtcTicks / 10 * 10, TimeSpan.Zero); }
    private async Task<ExecutionPlan> TaskFixture()
    {
        var f = await ActivationFixture(); var start = TaskNow().AddSeconds(10); var original = f.Plan.Baseline.ScenarioEvidence.Input.Definition;
        var definition = original with { Horizon = new(start, start.AddMinutes(4)), Sites = original.Sites.Select(s => s with { OpenWindows = [new(start, start.AddMinutes(4))] }).ToArray(),
            Tasks = original.Tasks.Select(t => t with { DurationMinutes = 1, NotBefore = start, DueAt = start.AddMinutes(4), Licenses = [new("compiler", 1)] }).ToArray() };
        var project = (await ProjectStore().ReplaceAsync(f.Plan.ProjectId, f.Plan.Baseline.ProjectVersion, definition, default))!;
        var scenario = await ScenarioStore().CreateAsync(project.Id, TemporalRequest(f.Engineer, f.Equipment, version: project.Version) with { GridMinutes = 1 }, default);
        var plan = await PlanStore().CreateAsync(project.Id, new(Guid.NewGuid(), scenario.Id, project.Version), default);
        plan = (await PlanStore("approver").MutateAsync(project.Id, plan.Id, 1, ExecutionPlanMutation.Approve, plan.Baseline.Digest, null, default))!;
        await Activate(plan); await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default); return plan;
    }
    private static async Task AwaitTaskWindow(DateTimeOffset start)
    { while (DateTimeOffset.UtcNow < start) await Task.Delay(100); }
    [Test]
    public async Task TaskLifecycleHonorsDependenciesRetainsOverrunAndClosesOriginalOwnership()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var plan = await TaskFixture(); var store = TaskStore();
        var schedule = plan.Baseline.ScenarioEvidence.Result.Tasks.OrderBy(t => t.Period.Start).ToArray(); var first = schedule[0]; var second = schedule[1];
        var early = await store.ExecuteAsync(plan.ProjectId, plan.Id, first.TaskId, 1, ExecutionTaskMutation.Start, new(Guid.NewGuid()), default);
        Assert.That(early.Accepted, Is.False); Assert.That(early.RejectionCode, Is.EqualTo("execution_task_start_window"));
        await AwaitTaskWindow(first.Period.Start);
        var startCommand = new ExecutionTaskCommand(Guid.NewGuid());
        var race = await Task.WhenAll(StartAttempt(startCommand), StartAttempt(new(Guid.NewGuid())));
        Assert.That(race.Count(r => r is not null), Is.EqualTo(1)); var started = race.Single(r => r is not null)!;
        async Task<ExecutionTaskFact?> StartAttempt(ExecutionTaskCommand command)
        { try { return await store.ExecuteAsync(plan.ProjectId, plan.Id, first.TaskId, 1, ExecutionTaskMutation.Start, command, default); } catch (ExecutionPlanVersionException) { return null; } }
        var winnerCommand = new ExecutionTaskCommand(started.CommandId); var replay = await store.ExecuteAsync(plan.ProjectId, plan.Id, first.TaskId, 1, ExecutionTaskMutation.Start, winnerCommand, default);
        Assert.That(replay.RecordedAt, Is.EqualTo(started.RecordedAt));
        Assert.ThrowsAsync<ExecutionActivationConflictException>(() => store.ExecuteAsync(plan.ProjectId, plan.Id, first.TaskId, 2, ExecutionTaskMutation.Start, winnerCommand, default));
        Assert.That(Assert.ThrowsAsync<ExecutionPlanRuleException>(() => ActivationStore().MutateAsync(plan.ProjectId, plan.Id, 3, ActivationMutation.Cancel, "operator_cancelled", default))!.Code, Is.EqualTo("execution_started_work_requires_forward_recovery"));
        Assert.That(Assert.ThrowsAsync<ProjectRuleException>(() => ProjectStore().ReplaceAsync(plan.ProjectId, plan.Baseline.ProjectVersion, plan.Baseline.ScenarioEvidence.Input.Definition with { BudgetAmount = 2000 }, default))!.Code, Is.EqualTo("project_started_work_requires_adaptive_revision"));
        await AwaitTaskWindow(second.Period.Start);
        var dependency = await store.ExecuteAsync(plan.ProjectId, plan.Id, second.TaskId, 1, ExecutionTaskMutation.Start, new(Guid.NewGuid()), default);
        Assert.That(dependency.RejectionCode, Is.EqualTo("execution_task_dependencies_incomplete"));
        var complete = new ExecutionTaskCommand(Guid.NewGuid(), TaskNow());
        var finished = await store.ExecuteAsync(plan.ProjectId, plan.Id, first.TaskId, 2, ExecutionTaskMutation.Complete, complete, default);
        Assert.That(finished.Accepted, Is.True); Assert.That(finished.OutsideReservation, Is.True);
        var completionReplay = await store.ExecuteAsync(plan.ProjectId, plan.Id, first.TaskId, 2, ExecutionTaskMutation.Complete, complete, default);
        Assert.That(completionReplay.EffectiveAt, Is.EqualTo(finished.EffectiveAt));
        Assert.That((await store.ExecuteAsync(plan.ProjectId, plan.Id, second.TaskId, 1, ExecutionTaskMutation.Start, new(Guid.NewGuid()), default)).Accepted, Is.True);
        Assert.That((await store.ExecuteAsync(plan.ProjectId, plan.Id, second.TaskId, 2, ExecutionTaskMutation.Complete, new(Guid.NewGuid(), TaskNow()), default)).Accepted, Is.True);
        await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default);
        Assert.That((await ActivationStore().GetAsync(plan.ProjectId, plan.Id, default))!.Activation.State, Is.EqualTo(ActivationState.Completed));
        var tasks = (await store.ListAsync(plan.ProjectId, plan.Id, default))!; Assert.That(tasks.All(t => t.Task.State == ExecutionTaskState.Completed), Is.True);
        Assert.That(tasks.Sum(t => t.Facts.Count), Is.EqualTo(6)); Assert.That(tasks.SelectMany(t => t.Facts).Count(f => f.Accepted), Is.EqualTo(4));
        await using var pg = TenantPg(TenantA); await using var sql = TenantSql(TenantA);
        Assert.That((await pg.Set<LicensePool>().SingleAsync()).TemporalReservations, Is.Empty); Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "v1.ExecutionTaskCommandRecorded"), Is.EqualTo(6));
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task TaskStartValidatesCurrentAuthoritativeResourceAndSeatOwnership(bool resource)
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var plan = await TaskFixture(); var first = plan.Baseline.ScenarioEvidence.Result.Tasks.OrderBy(t => t.Period.Start).First();
        if (resource)
        {
            var activation = (await ActivationStore().GetAsync(plan.ProjectId, plan.Id, default))!.Activation; var id = activation.ResourceOwnership.First(r => r.TaskId == first.TaskId).AllocationId;
            await using var sql = TenantSql(TenantA); var owned = (await sql.Allocations.ToListAsync()).Single(a => a.Id.Value == id); owned.Cancel(); await sql.SaveChangesAsync();
        }
        else { await using var pg = TenantPg(TenantA); (await pg.Set<LicensePool>().SingleAsync()).ReleaseTemporal(plan.Id, plan.ProjectId); await pg.SaveChangesAsync(); }
        await AwaitTaskWindow(first.Period.Start); var command = new ExecutionTaskCommand(Guid.NewGuid());
        var fact = await TaskStore().ExecuteAsync(plan.ProjectId, plan.Id, first.TaskId, 1, ExecutionTaskMutation.Start, command, default);
        Assert.That(fact.Accepted, Is.False); Assert.That(fact.RejectionCode, Is.EqualTo(resource ? "execution_task_resource_ownership_changed" : "execution_task_license_ownership_changed"));
        var replay = await TaskStore().ExecuteAsync(plan.ProjectId, plan.Id, first.TaskId, 1, ExecutionTaskMutation.Start, command, default); Assert.That(replay.RecordedAt, Is.EqualTo(fact.RecordedAt));
        Assert.That((await TaskStore().ListAsync(plan.ProjectId, plan.Id, default))!.All(t => t.Task.State == ExecutionTaskState.Planned), Is.True);
    }
    [Test]
    public async Task TaskProductionHttpRequiresOperatorTaskVersionAndRetainsNegativeDecision()
    {
        ExecutionPlan plan;
        using (TenantExecution.Enter(TenantScope.For(TenantA))) { plan = (await ActivationFixture()).Plan; await Activate(plan); await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default); }
        await using var factory = new ProjectApiFactory(_workforceOptions, _fulfillmentOptions); using var op = factory.Actor("operator", TenantA); using var approver = factory.Actor("approver", TenantA); using var foreign = factory.Actor("operator", TenantB);
        var task = plan.Baseline.ScenarioEvidence.Result.Tasks.OrderBy(t => t.Period.Start).First(); var path = $"/api/v1/projects/{plan.ProjectId}/execution-plans/{plan.Id}/tasks"; var body = new { commandId = Guid.NewGuid() };
        Assert.That((await ProjectMutation(op, HttpMethod.Post, path + $"/{task.TaskId}/start", body, null)).StatusCode, Is.EqualTo((HttpStatusCode)428));
        Assert.That((await ProjectMutation(approver, HttpMethod.Post, path + $"/{task.TaskId}/start", body, 1)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var denied = await ProjectBody(await ProjectMutation(op, HttpMethod.Post, path + $"/{task.TaskId}/start", body, 1), HttpStatusCode.UnprocessableEntity);
        Assert.That(denied.GetProperty("accepted").GetBoolean(), Is.False); Assert.That(denied.GetProperty("rejectionCode").GetString(), Is.EqualTo("execution_task_start_window"));
        var retry = await ProjectBody(await ProjectMutation(op, HttpMethod.Post, path + $"/{task.TaskId}/start", body, 1), HttpStatusCode.UnprocessableEntity);
        Assert.That(retry.GetProperty("recordedAt").GetString(), Is.EqualTo(denied.GetProperty("recordedAt").GetString()));
        Assert.That((await foreign.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var tasks = await ProjectBody(await op.GetAsync(path), HttpStatusCode.OK); Assert.That(tasks.GetArrayLength(), Is.EqualTo(2)); Assert.That(tasks.EnumerateArray().Sum(t => t.GetProperty("facts").GetArrayLength()), Is.EqualTo(1));
    }
    [Test]
    public async Task TaskFactsHaveActualTenantPoliciesAndRuntimeAppendOnlyGuard()
    {
        Guid id;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        { var plan = (await ActivationFixture()).Plan; id = plan.Id; await Activate(plan); await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default); await TaskStore().ExecuteAsync(plan.ProjectId, id, plan.Baseline.ScenarioEvidence.Result.Tasks.First().TaskId, 1, ExecutionTaskMutation.Start, new(Guid.NewGuid()), default); }
        await using (var admin = new FulfillmentContext(_fulfillmentOptions, TenantScope.System))
            await admin.Database.ExecuteSqlRawAsync("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='fusionops_task_test') THEN CREATE ROLE fusionops_task_test LOGIN PASSWORD 'FusionOps_test-2026!' NOSUPERUSER NOBYPASSRLS; END IF; END $$; GRANT USAGE ON SCHEMA public TO fusionops_task_test; GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO fusionops_task_test;");
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = "fusionops_task_test", Password = "FusionOps_test-2026!" }; var options = new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql(connection.ConnectionString).Options;
        await using (var foreign = new FulfillmentContext(options, TenantScope.For(TenantB)))
        { Assert.That(await foreign.Set<ExecutionTaskFact>().IgnoreQueryFilters().CountAsync(), Is.Zero); Assert.That(await foreign.Set<ExecutionTask>().IgnoreQueryFilters().CountAsync(), Is.Zero); }
        await using var own = new FulfillmentContext(options, TenantScope.For(TenantA));
        Assert.That(await own.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops_execution_task_facts SET \"Actor\"='changed' WHERE \"PlanId\"={id}"), Is.Zero);
        Assert.That(await own.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ops_execution_task_facts WHERE \"PlanId\"={id}"), Is.Zero);
        var fact = await own.Set<ExecutionTaskFact>().SingleAsync(); own.Entry(fact).Property(f => f.Actor).CurrentValue = "changed";
        Assert.That(Assert.ThrowsAsync<ExecutionPlanRuleException>(() => own.SaveChangesAsync())!.Code, Is.EqualTo("execution_history_immutable"));
    }
}
