using System.Net;
using System.Net.Http.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using FusionOps.Infrastructure.Execution;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FusionOps.Integration.Tests;

public sealed partial class ReliabilityTests
{
    private PostgresExecutionPlanStore PlanStore(string actor = "owner") => new(new TestContextFactory(_fulfillmentOptions),
        new TestExecutionContext(actor), TimeProvider.System, ExecutionApprovalPolicy.Standard());
    [Test]
    public async Task ExecutionProposalRetainsImmutableEvidenceAndOriginalReplayAfterProjectRevision()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture();
        var scenario = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default);
        var request = new ExecutionPlanRequest(Guid.NewGuid(), scenario.Id, 1); var store = PlanStore();
        var plan = await store.CreateAsync(f.Project, request, default); var digest = plan.Baseline.Digest;
        Assert.That(plan.Baseline.TotalCost, Is.EqualTo(70)); Assert.That(plan.Baseline.Evidence.Scenario.Digest, Is.EqualTo(scenario.Digest));
        Assert.That(plan.State, Is.EqualTo(ExecutionPlanState.AwaitingApproval));
        await ProjectStore().ReplaceAsync(f.Project, 1, f.Definition with { BudgetAmount = 2000 }, default);
        var retried = await store.CreateAsync(f.Project, request, default);
        Assert.That(retried.Baseline.Digest, Is.EqualTo(digest)); retried.Baseline.VerifyEvidence();
        Assert.ThrowsAsync<ExecutionPlanSubmissionConflictException>(() => store.CreateAsync(f.Project, request with { ExpectedProjectVersion = 2 }, default));
        Assert.ThrowsAsync<ProjectVersionException>(() => store.CreateAsync(f.Project, request with { Id = Guid.NewGuid(), ExpectedProjectVersion = 2 }, default));
        await using var pg = TenantPg(TenantA); await using var sql = TenantSql(TenantA);
        Assert.That(await pg.Set<ExecutionPlanDecision>().CountAsync(), Is.EqualTo(1));
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "v1.ExecutionPlanChanged"), Is.EqualTo(1));
        Assert.That(await sql.Allocations.CountAsync(), Is.Zero, "Proposal is evidence, not resource acceptance");
    }
    [Test]
    public async Task CompetingPlanDecisionsRetainOneVersionOneDecisionAndOneEvent()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture();
        await ProjectStore().SetMemberAsync(f.Project, 1, "approver", ProjectRole.Approver, default);
        var scenario = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment, version: 2), default);
        var plan = await PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), scenario.Id, 2), default);
        Assert.ThrowsAsync<ExecutionPlanRuleException>(() => PlanStore().MutateAsync(f.Project, plan.Id, 1,
            ExecutionPlanMutation.Approve, plan.Baseline.Digest, null, default));
        async Task<string> Decide(ExecutionPlanMutation action)
        {
            try
            {
                await PlanStore("approver").MutateAsync(f.Project, plan.Id, 1, action, plan.Baseline.Digest,
                    action == ExecutionPlanMutation.Reject ? "Resource assumptions need revision" : null, default);
                return "accepted";
            }
            catch (ExecutionPlanVersionException) { return "stale"; }
        }
        var results = await Task.WhenAll(Decide(ExecutionPlanMutation.Approve), Decide(ExecutionPlanMutation.Reject));
        Assert.That(results.Count(r => r == "accepted"), Is.EqualTo(1)); Assert.That(results.Count(r => r == "stale"), Is.EqualTo(1));
        var current = (await PlanStore().GetAsync(f.Project, plan.Id, default))!;
        Assert.That(current.Plan.Version, Is.EqualTo(2)); Assert.That(current.Decisions, Has.Count.EqualTo(2));
        Assert.That(current.Decisions[1].ReviewedDigest, Is.EqualTo(plan.Baseline.Digest));
        await using var pg = TenantPg(TenantA);
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "v1.ExecutionPlanChanged"), Is.EqualTo(2));
    }
    [Test]
    public async Task RevisedDefinitionCannotInheritOldApprovalAndNewPlanRecordsDiff()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture();
        await ProjectStore().SetMemberAsync(f.Project, 1, "approver", ProjectRole.Approver, default);
        var scenario = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment, version: 2), default);
        var plan = await PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), scenario.Id, 2), default);
        var changed = f.Definition with { Tasks = f.Definition.Tasks.Select(t => t.Dependencies.Count == 0 ? t with { DurationMinutes = 120 } : t).ToArray() };
        await ProjectStore().ReplaceAsync(f.Project, 2, changed, default);
        Assert.ThrowsAsync<ProjectVersionException>(() => PlanStore("approver").MutateAsync(f.Project, plan.Id, 1,
            ExecutionPlanMutation.Approve, plan.Baseline.Digest, null, default));
        var fresh = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment, version: 3), default);
        var revised = await PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), fresh.Id, 3, plan.Id), default);
        Assert.That(revised.Baseline.Evidence.Diff.FinishDeltaMinutes, Is.EqualTo(60));
        Assert.That(revised.Baseline.Evidence.Diff.CostDelta, Is.EqualTo(25));
        Assert.That(revised.Baseline.Evidence.Diff.Tasks, Has.Count.EqualTo(2));
        Assert.That(revised.State, Is.EqualTo(ExecutionPlanState.AwaitingApproval));
        var withdrawn = await PlanStore().MutateAsync(f.Project, plan.Id, 1, ExecutionPlanMutation.Withdraw,
            plan.Baseline.Digest, "Replaced by revised proposal", default);
        Assert.That(withdrawn!.State, Is.EqualTo(ExecutionPlanState.Withdrawn));
    }
    [Test]
    public async Task PartialScenarioCannotEnterExecutionReview()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture(stock: 1);
        var scenario = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default);
        Assert.That(scenario.IsComplete, Is.False);
        Assert.That(Assert.ThrowsAsync<ExecutionPlanRuleException>(() => PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), scenario.Id, 1), default))!.Code,
            Is.EqualTo("execution_complete_scenario_required"));
        await using var pg = TenantPg(TenantA); Assert.That(await pg.Set<ExecutionPlan>().CountAsync(), Is.Zero);
    }
    [Test]
    public async Task ExecutionHistoryIsAppendOnlyForRestrictedDatabaseRoleAndTenantScoped()
    {
        Guid id;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        {
            var f = await TemporalFixture(); var s = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default);
            id = (await PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), s.Id, 1), default)).Id;
        }
        await using (var admin = new FulfillmentContext(_fulfillmentOptions, TenantScope.System))
            await admin.Database.ExecuteSqlRawAsync("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fusionops_plan_test') THEN CREATE ROLE fusionops_plan_test LOGIN PASSWORD 'FusionOps_test-2026!' NOSUPERUSER NOBYPASSRLS; END IF; END $$; GRANT USAGE ON SCHEMA public TO fusionops_plan_test; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fusionops_plan_test;");
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = "fusionops_plan_test", Password = "FusionOps_test-2026!" };
        var options = new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql(connection.ConnectionString).Options;
        await using (var foreign = new FulfillmentContext(options, TenantScope.For(TenantB)))
        {
            Assert.That(await foreign.Set<ExecutionPlan>().IgnoreQueryFilters().CountAsync(), Is.Zero);
            Assert.That(await foreign.Set<ExecutionBaseline>().IgnoreQueryFilters().CountAsync(), Is.Zero);
            Assert.That(await foreign.Set<ExecutionPlanDecision>().IgnoreQueryFilters().CountAsync(), Is.Zero);
        }
        await using var pg = new FulfillmentContext(options, TenantScope.For(TenantA));
        Assert.That(await pg.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops_execution_baselines SET \"Digest\"='changed' WHERE \"Id\"={id}"), Is.Zero);
        Assert.That(await pg.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ops_execution_baselines WHERE \"Id\"={id}"), Is.Zero);
        Assert.That(await pg.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops_execution_plan_decisions SET \"Actor\"='changed' WHERE \"PlanId\"={id}"), Is.Zero);
        var b = await pg.Set<ExecutionBaseline>().SingleAsync(); b.VerifyEvidence();
        pg.Entry(b).Property(x => x.Digest).CurrentValue = "changed";
        Assert.That(Assert.ThrowsAsync<ExecutionPlanRuleException>(() => pg.SaveChangesAsync())!.Code, Is.EqualTo("execution_history_immutable"));
    }
    [Test]
    public async Task ExecutionPlanProductionHttpContractRequiresReviewedDigestRoleAndCurrentETag()
    {
        Guid project, scenario;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        {
            var f = await TemporalFixture(); project = f.Project;
            await ProjectStore().SetMemberAsync(project, 1, "approver", ProjectRole.Approver, default);
            await ProjectStore().SetMemberAsync(project, 2, "viewer", ProjectRole.Viewer, default);
            scenario = (await ScenarioStore().CreateAsync(project, TemporalRequest(f.Engineer, f.Equipment, version: 3), default)).Id;
        }
        await using var factory = new ProjectApiFactory(_workforceOptions, _fulfillmentOptions);
        using var owner = factory.Actor("owner", TenantA); using var approver = factory.Actor("approver", TenantA);
        using var viewer = factory.Actor("viewer", TenantA); using var foreign = factory.Actor("owner", TenantB); using var outsider = factory.Actor("outsider", TenantA);
        var id = Guid.NewGuid(); var path = $"/api/v1/projects/{project}/execution-plans";
        var request = new ExecutionPlanRequest(id, scenario, 3);
        using var response = await owner.PostAsJsonAsync(path, request);
        var plan = await ProjectBody(response, HttpStatusCode.Created); var digest = plan.GetProperty("digest").GetString()!;
        Assert.That(response.Headers.ETag!.Tag, Is.EqualTo("\"1\"")); Assert.That(response.Headers.Location!.ToString(), Does.EndWith(id.ToString("D")));
        await ProjectBody(await approver.PostAsJsonAsync($"{path}/{id}/approve", new { reviewedDigest = digest }), (HttpStatusCode)428);
        await ProjectBody(await ProjectMutation(owner, HttpMethod.Post, $"{path}/{id}/approve", new { reviewedDigest = digest }, 1), HttpStatusCode.UnprocessableEntity);
        await ProjectBody(await ProjectMutation(approver, HttpMethod.Post, $"{path}/{id}/approve", new { reviewedDigest = "wrong" }, 1), HttpStatusCode.UnprocessableEntity);
        await ProjectBody(await ProjectMutation(viewer, HttpMethod.Post, $"{path}/{id}/approve", new { reviewedDigest = digest }, 1), HttpStatusCode.NotFound);
        var approved = await ProjectBody(await ProjectMutation(approver, HttpMethod.Post, $"{path}/{id}/approve", new { reviewedDigest = digest }, 1), HttpStatusCode.OK);
        Assert.That(approved.GetProperty("state").GetString(), Is.EqualTo("Approved"));
        await ProjectBody(await ProjectMutation(approver, HttpMethod.Post, $"{path}/{id}/reject", new { reviewedDigest = digest, reason = "Changed" }, 1), HttpStatusCode.PreconditionFailed);
        Assert.That((await foreign.GetAsync($"{path}/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await outsider.GetAsync($"{path}/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var read = await ProjectBody(await viewer.GetAsync($"{path}/{id}"), HttpStatusCode.OK);
        Assert.That(read.GetProperty("decisions").GetArrayLength(), Is.EqualTo(2));
        var page = await ProjectBody(await viewer.GetAsync(path + "?limit=1"), HttpStatusCode.OK);
        Assert.That(page.GetArrayLength(), Is.EqualTo(1)); Assert.That(page[0].TryGetProperty("evidence", out _), Is.False);
        var replay = await ProjectBody(await owner.PostAsJsonAsync(path, request), HttpStatusCode.Created);
        Assert.That(replay.GetProperty("digest").GetString(), Is.EqualTo(digest));
    }
    [Test]
    public async Task ConcurrentOriginalPlanProposalCreatesOneBaselineDecisionAndOutboxEvent()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture();
        var scenario = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default);
        var request = new ExecutionPlanRequest(Guid.NewGuid(), scenario.Id, 1);
        var plans = await Task.WhenAll(PlanStore().CreateAsync(f.Project, request, default), PlanStore().CreateAsync(f.Project, request, default));
        Assert.That(plans[0].Baseline.Digest, Is.EqualTo(plans[1].Baseline.Digest));
        await using var pg = TenantPg(TenantA);
        Assert.That(await pg.Set<ExecutionBaseline>().CountAsync(), Is.EqualTo(1));
        Assert.That(await pg.Set<ExecutionPlanDecision>().CountAsync(), Is.EqualTo(1));
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "v1.ExecutionPlanChanged"), Is.EqualTo(1));
    }
}
