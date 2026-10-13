using System.Net;
using System.Net.Http.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Projects;
using FusionOps.Infrastructure.Execution;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FusionOps.Integration.Tests;

public sealed partial class ReliabilityTests
{
    private PostgresExecutionActivationStore ActivationStore(string actor = "operator") => new(new TestContextFactory(_fulfillmentOptions), new TestExecutionContext(actor));
    private ExecutionActivationRunner ActivationRunner(ITemporalWorkforceParticipant? workforce = null, ITemporalLicenseParticipant? licenses = null) => new(ActivationStore(), workforce ?? TemporalWorkforce(), licenses ?? TemporalLicenses(), NullLogger<ExecutionActivationRunner>.Instance);
    private async Task<(ExecutionPlan Plan, Guid Engineer, Guid Equipment)> ActivationFixture(bool materials = false)
    {
        var f = await TemporalFixture(); long version = 1; var start = TaskNow().AddDays(1);
        // Admission uses the actual database clock; the fixture must remain future-dated after October 2026.
        var definition = f.Definition with { Horizon = new(start, start.AddHours(4)),
            Sites = f.Definition.Sites.Select(s => s with { OpenWindows = [new(start, start.AddHours(4))] }).ToArray(),
            Tasks = f.Definition.Tasks.Select(t => t with { NotBefore = start, DueAt = start.AddHours(4), Materials = materials ? t.Materials : [] }).ToArray() };
        await ProjectStore().ReplaceAsync(f.Project, version++, definition, default);
        await ProjectStore().SetMemberAsync(f.Project, version++, "approver", ProjectRole.Approver, default);
        await ProjectStore().SetMemberAsync(f.Project, version++, "operator", ProjectRole.Operator, default);
        var scenario = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment, version: version), default);
        var plan = await PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), scenario.Id, version), default);
        plan = (await PlanStore("approver").MutateAsync(f.Project, plan.Id, 1, ExecutionPlanMutation.Approve, plan.Baseline.Digest, null, default))!;
        return (plan, f.Engineer, f.Equipment);
    }
    private Task<ExecutionActivation> Activate(ExecutionPlan plan) => ActivationStore().RequestAsync(plan.ProjectId, plan.Id, plan.Version, plan.Baseline.Digest, default);
    private static ActivationStepResult Confirm(ExecutionParticipantResult r) => r.Succeeded
        ? new(ActivationStepOutcome.Confirmed, Resources: r.Allocations.Select(a => new ExecutionResourceOwnership(a.TaskId, a.ResourceId, a.AllocationId)).ToArray())
        : new(ActivationStepOutcome.Rejected, r.RejectionCode);
    private async Task ExpireActivationLease(Guid id)
    {
        await using var pg = new FulfillmentContext(_fulfillmentOptions, TenantScope.System);
        await pg.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops_execution_activations SET \"LeaseExpiresAt\"=clock_timestamp()-interval '1 second', \"NextAttemptAt\"=clock_timestamp()-interval '1 second' WHERE \"Id\"={id}");
    }
    [Test]
    public async Task ActivationOwnsTaskIntervalsThenCancelsOnlyOriginalEffects()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ActivationFixture(); var requested = await Activate(f.Plan);
        Assert.That(requested.State, Is.EqualTo(ActivationState.ReservingResources));
        Assert.That((await Activate(f.Plan)).Version, Is.EqualTo(1));
        Assert.ThrowsAsync<ExecutionPlanRuleException>(() => PlanStore().MutateAsync(f.Plan.ProjectId, f.Plan.Id, 2, ExecutionPlanMutation.Withdraw, f.Plan.Baseline.Digest, "withdraw", default));
        await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default);
        var active = (await ActivationStore().GetAsync(f.Plan.ProjectId, f.Plan.Id, default))!;
        Assert.That(active.Activation.State, Is.EqualTo(ActivationState.Active)); Assert.That(active.Activation.ResourceOwnership, Has.Count.EqualTo(3));
        Assert.That(active.Decisions.Select(d => d.Version), Is.EqualTo(new long[] { 1, 2, 3 }));
        await ActivationStore().MutateAsync(f.Plan.ProjectId, f.Plan.Id, 3, ActivationMutation.Cancel, "operator_cancelled", default);
        await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default);
        var done = (await ActivationStore().GetAsync(f.Plan.ProjectId, f.Plan.Id, default))!; Assert.That(done.Activation.State, Is.EqualTo(ActivationState.Compensated));
        await using var sql = TenantSql(TenantA); await using var pg = TenantPg(TenantA);
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        Assert.That((await pg.Set<LicensePool>().SingleAsync()).TemporalReservations, Is.Empty);
        Assert.That(done.Decisions, Has.Count.EqualTo(6));
        Assert.That((await Activate(f.Plan)).State, Is.EqualTo(ActivationState.Compensated));
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task CommittedParticipantResultSurvivesAbandonedWorkerAndFencesOldClaim(bool resourceStep)
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ActivationFixture(); await Activate(f.Plan);
        if (!resourceStep) await ActivationRunner().RunOnceAsync(default);
        var store = ActivationStore(); var lostClaim = (await store.ClaimAsync(TimeSpan.FromSeconds(5), default))!;
        var original = resourceStep ? await TemporalWorkforce().ReserveAsync(f.Plan.Baseline, default) : await TemporalLicenses().ReserveAsync(f.Plan.Baseline, default);
        // Simulate a process disappearing after the real participant commit and before coordinator completion.
        await ExpireActivationLease(f.Plan.Id); var replacement = (await store.ClaimAsync(TimeSpan.FromSeconds(45), default))!;
        Assert.That(replacement.Epoch, Is.GreaterThan(lostClaim.Epoch));
        Assert.That(await store.CompleteAsync(lostClaim, Confirm(original), 5, default), Is.False);
        var replay = resourceStep ? await TemporalWorkforce().ReserveAsync(f.Plan.Baseline, default) : await TemporalLicenses().ReserveAsync(f.Plan.Baseline, default);
        Assert.That(await store.CompleteAsync(replacement, Confirm(replay), 5, default), Is.True);
        if (resourceStep) await ActivationRunner().RunOnceAsync(default);
        Assert.That((await store.GetAsync(f.Plan.ProjectId, f.Plan.Id, default))!.Activation.State, Is.EqualTo(ActivationState.Active));
        await using var sql = TenantSql(TenantA); await using var pg = TenantPg(TenantA);
        Assert.That(await sql.Allocations.CountAsync(), Is.EqualTo(3)); Assert.That((await pg.Set<LicensePool>().SingleAsync()).TemporalReservations, Has.Count.EqualTo(1));
        if (resourceStep) Assert.That(replay.Allocations, Is.EqualTo(original.Allocations));
    }
    [Test]
    public async Task SameProjectConcurrentActivationHasOneAuthoritativeOwner()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ActivationFixture();
        var second = await PlanStore().CreateAsync(f.Plan.ProjectId, new(Guid.NewGuid(), f.Plan.Baseline.ScenarioId, f.Plan.Baseline.ProjectVersion), default);
        second = (await PlanStore("approver").MutateAsync(second.ProjectId, second.Id, 1, ExecutionPlanMutation.Approve, second.Baseline.Digest, null, default))!;
        async Task<string> Attempt(ExecutionPlan plan) { try { await Activate(plan); return "accepted"; } catch (ExecutionActivationConflictException) { return "conflict"; } }
        var outcomes = await Task.WhenAll(Attempt(f.Plan), Attempt(second)); Assert.That(outcomes, Is.EquivalentTo(new[] { "accepted", "conflict" }));
        await using var pg = TenantPg(TenantA); Assert.That(await pg.Set<ExecutionActivation>().CountAsync(), Is.EqualTo(1));
        Assert.That(await pg.Set<ExecutionActivationDecision>().CountAsync(), Is.EqualTo(1));
    }
    [Test]
    public async Task MaterialDemandsAndUnapprovedOrChangedDefinitionCannotActivate()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ActivationFixture(materials: true);
        Assert.That(Assert.ThrowsAsync<ExecutionPlanRuleException>(() => Activate(f.Plan))!.Code, Is.EqualTo("execution_material_participant_unsupported"));
        var pending = await PlanStore().CreateAsync(f.Plan.ProjectId, new(Guid.NewGuid(), f.Plan.Baseline.ScenarioId, f.Plan.Baseline.ProjectVersion), default);
        Assert.That(Assert.ThrowsAsync<ExecutionPlanRuleException>(() => Activate(pending))!.Code, Is.EqualTo("execution_valid_approval_required"));
        await ProjectStore().ReplaceAsync(f.Plan.ProjectId, f.Plan.Baseline.ProjectVersion, f.Plan.Baseline.ScenarioEvidence.Input.Definition with { BudgetAmount = 2000 }, default);
        Assert.ThrowsAsync<ProjectVersionException>(() => Activate(f.Plan));
        await using var pg = TenantPg(TenantA); await using var sql = TenantSql(TenantA);
        Assert.That(await pg.Set<ExecutionActivation>().CountAsync(), Is.Zero); Assert.That(await sql.Allocations.CountAsync(), Is.Zero);
    }
    [Test]
    public async Task LicenseRejectionCompensatesWorkforceAndKeepsNegativeDecision()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ActivationFixture(); await Activate(f.Plan);
        await ActivationRunner().RunOnceAsync(default);
        await using (var pg = TenantPg(TenantA)) { (await pg.Set<LicensePool>().SingleAsync()).Allocate(Guid.NewGuid(), 1); await pg.SaveChangesAsync(); }
        await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default);
        var done = (await ActivationStore().GetAsync(f.Plan.ProjectId, f.Plan.Id, default))!;
        Assert.That(done.Activation.State, Is.EqualTo(ActivationState.Compensated)); Assert.That(done.Activation.FailureCode, Is.EqualTo("license_capacity"));
        await using var sql = TenantSql(TenantA); Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        Assert.That((await TemporalLicenses().ReserveAsync(f.Plan.Baseline, default)).Succeeded, Is.False);
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task RevokedAuthorityAfterParticipantCommitCompensatesRatherThanActivating(bool resourceStep)
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ActivationFixture(); await Activate(f.Plan);
        if (!resourceStep) await ActivationRunner().RunOnceAsync(default);
        var store = ActivationStore(); var claim = (await store.ClaimAsync(TimeSpan.FromSeconds(45), default))!;
        var result = resourceStep ? await TemporalWorkforce().ReserveAsync(f.Plan.Baseline, default) : await TemporalLicenses().ReserveAsync(f.Plan.Baseline, default);
        await ProjectStore().RemoveMemberAsync(f.Plan.ProjectId, f.Plan.Baseline.ProjectVersion, "operator", default);
        Assert.That(await store.CompleteAsync(claim, Confirm(result), 5, default), Is.True);
        await ActivationRunner().RunOnceAsync(default); if (!resourceStep) await ActivationRunner().RunOnceAsync(default);
        var done = (await ActivationStore("owner").GetAsync(f.Plan.ProjectId, f.Plan.Id, default))!.Activation;
        Assert.That(done.State, Is.EqualTo(ActivationState.Compensated)); Assert.That(done.FailureCode, Is.EqualTo("execution_authority_revoked"));
    }
    [Test]
    public async Task CancellationDuringLicenseCommitResolvesAndReleasesBothOwners()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ActivationFixture(); await Activate(f.Plan); await ActivationRunner().RunOnceAsync(default);
        var store = ActivationStore(); var claim = (await store.ClaimAsync(TimeSpan.FromSeconds(45), default))!;
        var committed = await TemporalLicenses().ReserveAsync(f.Plan.Baseline, default);
        await store.MutateAsync(f.Plan.ProjectId, f.Plan.Id, 2, ActivationMutation.Cancel, "operator_cancelled", default);
        await store.CompleteAsync(claim, Confirm(committed), 5, default); await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default);
        Assert.That((await store.GetAsync(f.Plan.ProjectId, f.Plan.Id, default))!.Activation.State, Is.EqualTo(ActivationState.Compensated));
        await using var pg = TenantPg(TenantA); Assert.That((await pg.Set<LicensePool>().SingleAsync()).TemporalReservations, Is.Empty);
    }
    [Test]
    public async Task UnknownLicenseResultWaitsForOperatorResumeAndOriginalReceipt()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ActivationFixture(); await Activate(f.Plan); await ActivationRunner().RunOnceAsync(default);
        var store = ActivationStore(); var claim = (await store.ClaimAsync(TimeSpan.FromSeconds(45), default))!; await TemporalLicenses().ReserveAsync(f.Plan.Baseline, default);
        await store.CompleteAsync(claim, new(ActivationStepOutcome.Unresolved, "TimeoutException"), 1, default);
        var unknown = (await store.GetAsync(f.Plan.ProjectId, f.Plan.Id, default))!.Activation;
        Assert.That(unknown.State, Is.EqualTo(ActivationState.NeedsAttention)); Assert.That(unknown.PausedStep, Is.EqualTo(ActivationState.ReservingLicenses));
        Assert.That(await ActivationRunner().RunOnceAsync(default), Is.False);
        await store.MutateAsync(f.Plan.ProjectId, f.Plan.Id, unknown.Version, ActivationMutation.Resume, null, default); await ActivationRunner().RunOnceAsync(default);
        Assert.That((await store.GetAsync(f.Plan.ProjectId, f.Plan.Id, default))!.Activation.State, Is.EqualTo(ActivationState.Active));
        await using var pg = TenantPg(TenantA); Assert.That((await pg.Set<LicensePool>().SingleAsync()).Version, Is.EqualTo(1));
    }
    [Test]
    public async Task ActivationPoliciesEnforceTenantAndAppendOnlyDecisionHistory()
    {
        Guid project, id;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        { var f = await ActivationFixture(); project = f.Plan.ProjectId; id = f.Plan.Id; await Activate(f.Plan); await ActivationRunner().RunOnceAsync(default); }
        await using (var admin = new FulfillmentContext(_fulfillmentOptions, TenantScope.System))
            await admin.Database.ExecuteSqlRawAsync("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='fusionops_activation_test') THEN CREATE ROLE fusionops_activation_test LOGIN PASSWORD 'FusionOps_test-2026!' NOSUPERUSER NOBYPASSRLS; END IF; END $$; GRANT USAGE ON SCHEMA public TO fusionops_activation_test; GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO fusionops_activation_test;");
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = "fusionops_activation_test", Password = "FusionOps_test-2026!" };
        var options = new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql(connection.ConnectionString).Options;
        await using (var foreign = new FulfillmentContext(options, TenantScope.For(TenantB)))
        {
            Assert.That(await foreign.Set<ExecutionActivation>().IgnoreQueryFilters().CountAsync(), Is.Zero);
            Assert.That(await foreign.Set<ExecutionActivationDecision>().IgnoreQueryFilters().CountAsync(), Is.Zero);
            Assert.That(await foreign.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops_execution_activations SET \"State\"='Active' WHERE \"Id\"={id}"), Is.Zero);
        }
        await using var own = new FulfillmentContext(options, TenantScope.For(TenantA));
        Assert.That(await own.Set<ExecutionActivationDecision>().CountAsync(), Is.EqualTo(2));
        Assert.That(await own.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops_execution_activation_decisions SET \"Actor\"='changed' WHERE \"ActivationId\"={id}"), Is.Zero);
        Assert.That(await own.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ops_execution_activation_decisions WHERE \"ActivationId\"={id}"), Is.Zero);
        var decision = await own.Set<ExecutionActivationDecision>().FirstAsync(); own.Entry(decision).Property(d => d.Actor).CurrentValue = "changed";
        Assert.That(Assert.ThrowsAsync<ExecutionPlanRuleException>(() => own.SaveChangesAsync())!.Code, Is.EqualTo("execution_history_immutable"));
    }
    [Test]
    public async Task ActivationProductionHttpUsesPlanETagProjectOperatorAndSeparateActivationETag()
    {
        ExecutionPlan plan;
        using (TenantExecution.Enter(TenantScope.For(TenantA))) { plan = (await ActivationFixture()).Plan; }
        await using var factory = new ProjectApiFactory(_workforceOptions, _fulfillmentOptions);
        using var op = factory.Actor("operator", TenantA); using var approver = factory.Actor("approver", TenantA); using var foreign = factory.Actor("operator", TenantB);
        var path = $"/api/v1/projects/{plan.ProjectId}/execution-plans/{plan.Id}/activation"; var body = new { reviewedDigest = plan.Baseline.Digest };
        Assert.That((await op.PostAsJsonAsync(path, body)).StatusCode, Is.EqualTo((HttpStatusCode)428));
        Assert.That((await ProjectMutation(op, HttpMethod.Post, path, body, 1)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        Assert.That((await ProjectMutation(approver, HttpMethod.Post, path, body, 2)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await ProjectMutation(foreign, HttpMethod.Post, path, body, 2)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var response = await ProjectMutation(op, HttpMethod.Post, path, body, 2); var created = await ProjectBody(response, HttpStatusCode.Accepted);
        Assert.That(response.Headers.ETag!.Tag, Is.EqualTo("\"1\"")); Assert.That(created.GetProperty("state").GetString(), Is.EqualTo("ReservingResources"));
        using (TenantExecution.Enter(TenantScope.For(TenantA))) { await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default); }
        var current = await ProjectBody(await op.GetAsync(path), HttpStatusCode.OK); Assert.That(current.GetProperty("activation").GetProperty("state").GetString(), Is.EqualTo("Active"));
        Assert.That(current.GetProperty("decisions").GetArrayLength(), Is.EqualTo(3)); Assert.That((await foreign.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await ProjectMutation(op, HttpMethod.Post, path + "/cancel", new { reason = "operator_cancelled" }, 1)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        await ProjectBody(await ProjectMutation(op, HttpMethod.Post, path + "/cancel", new { reason = "operator_cancelled" }, 3), HttpStatusCode.OK);
        using (TenantExecution.Enter(TenantScope.For(TenantA))) { await ActivationRunner().RunOnceAsync(default); await ActivationRunner().RunOnceAsync(default); }
        var done = await ProjectBody(await op.GetAsync(path), HttpStatusCode.OK); Assert.That(done.GetProperty("activation").GetProperty("state").GetString(), Is.EqualTo("Compensated"));
    }
    [Test]
    public async Task HostedActivationWorkerReachesActiveAndCompensatedThroughRealHttp()
    {
        ExecutionPlan plan;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        { plan = (await ActivationFixture()).Plan; await ProjectStore().SetMemberAsync(plan.ProjectId, plan.Baseline.ProjectVersion, "system", ProjectRole.Operator, default); }
        await using var factory = new ReservationFactory(_workforceOptions, _fulfillmentOptions, runActivationWorker: true); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantA.ToString("D"));
        var path = $"/api/v1/projects/{plan.ProjectId}/execution-plans/{plan.Id}/activation";
        await ProjectBody(await ProjectMutation(client, HttpMethod.Post, path, new { reviewedDigest = plan.Baseline.Digest }, 2), HttpStatusCode.Accepted);
        async Task<System.Text.Json.JsonElement> Wait(string state)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            do { var body = await ProjectBody(await client.GetAsync(path), HttpStatusCode.OK); if (body.GetProperty("activation").GetProperty("state").GetString() == state) return body; await Task.Delay(100); } while (DateTimeOffset.UtcNow < deadline);
            throw new InvalidOperationException("Hosted activation did not reach " + state);
        }
        var active = await Wait("Active"); await ProjectBody(await ProjectMutation(client, HttpMethod.Post, path + "/cancel", new { reason = "operator_cancelled" }, active.GetProperty("activation").GetProperty("version").GetInt64()), HttpStatusCode.OK);
        await Wait("Compensated");
    }
}
