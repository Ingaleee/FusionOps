using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Scheduling;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace FusionOps.Integration.Tests;

public sealed partial class ReliabilityTests
{
    private PostgresTemporalScenarioStore ScenarioStore(IDbContextFactory<WorkforceContext>? sql = null) =>
        new(new TestContextFactory(_fulfillmentOptions), ProjectStore(), new TestExecutionContext("owner"), TimeProvider.System,
            new(sql ?? new WorkforceContextFactory(_workforceOptions), new TestContextFactory(_fulfillmentOptions)), new TemporalPlanner());
    private async Task<(Guid Project, ProjectDefinition Definition, Guid Engineer, Guid Equipment)> TemporalFixture(int stock = 5)
    {
        var engineer = await RegisterTenantResource(TenantA); var equipment = Guid.NewGuid();
        await CatalogStore().RegisterAsync(equipment, ResourceKind.Equipment,
            new("Installation machine", Money.Usd(10), new Dictionary<string, int>(), "machine"), default);
        var site = Guid.NewGuid(); var design = Guid.NewGuid(); var install = Guid.NewGuid();
        var definition = new ProjectDefinition("Installation", new(ProjectStart, ProjectStart.AddHours(4)), 1000, "USD",
            [new(site, "Customer site", [new(ProjectStart, ProjectStart.AddHours(4))])],
            [new(design, "Design", site, 60, ProjectStart, ProjectStart.AddHours(4), [],
                [new(Guid.NewGuid(), "Engineer", ResourceKind.Human, new Dictionary<string, int>())], [], []),
             new(install, "Install", site, 60, ProjectStart, ProjectStart.AddHours(4), [design],
                [new(Guid.NewGuid(), "Engineer", ResourceKind.Human, new Dictionary<string, int>()),
                 new(Guid.NewGuid(), "Machine", ResourceKind.Equipment, new Dictionary<string, int>(), "machine")], [new("connector", 2)], [new("compiler", 1)])], []);
        var project = Guid.NewGuid(); await ProjectStore().CreateAsync(project, definition, default);
        await using var pg = TenantPg(TenantA);
        pg.Add(new StockItem(StockItemId.New(), "connector", stock, 1, Money.Usd(5)));
        pg.Add(new LicensePool("compiler", 1)); await pg.SaveChangesAsync();
        return (project, definition, engineer, equipment);
    }
    private static TemporalScenarioRequest TemporalRequest(Guid engineer, Guid equipment, Guid? id = null, long version = 1, ScenarioAssumptions? assumptions = null) =>
        new(id ?? Guid.NewGuid(), version, [engineer, equipment], GridMinutes: 60, MaxSearchNodes: 100000, MaxElapsedMilliseconds: 30000, Assumptions: assumptions);
    [Test]
    public async Task ScenarioCapturesAuthoritativeInputsReplaysAfterSourceChangesAndMakesNoReservations()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var fixture = await TemporalFixture(); var store = ScenarioStore();
        var request = TemporalRequest(fixture.Engineer, fixture.Equipment);
        var scenario = await store.CreateAsync(fixture.Project, request, default); var digest = scenario.Digest;
        Assert.That(scenario.IsComplete, Is.True); Assert.That(scenario.SearchComplete, Is.True); Assert.That(scenario.TotalCost, Is.EqualTo(70));
        Assert.That(scenario.FinishAt, Is.EqualTo(ProjectStart.AddHours(2))); Assert.That(scenario.Evidence.Source.Resources, Has.Count.EqualTo(2));
        await ProjectStore().ReplaceAsync(fixture.Project, 1, fixture.Definition with { BudgetAmount = 2000 }, default);
        await CatalogStore().ReviseAsync(fixture.Engineer, 1, new("Changed", Money.Usd(99), new Dictionary<string, int>(), null), default);
        await using (var pg = TenantPg(TenantA)) { var item = await pg.StockItems.SingleAsync(); item.Deduct(5); await pg.SaveChangesAsync(); }
        var fetched = (await store.GetAsync(fixture.Project, scenario.Id, default))!; fetched.VerifyEvidence();
        Assert.That(fetched.Digest, Is.EqualTo(digest)); Assert.That(fetched.Evidence.Input.Candidates.Single(c => c.Id == fixture.Engineer).HourRate.Amount, Is.EqualTo(25));
        var replay = (await store.ReplayAsync(fixture.Project, scenario.Id, default))!; Assert.That(replay.MatchesOriginalDecision, Is.True);
        var retried = await store.CreateAsync(fixture.Project, request with { CandidateIds = request.CandidateIds.Reverse().ToArray() }, default);
        Assert.That(retried.Digest, Is.EqualTo(digest), "Retry must retain original evidence despite source revisions");
        Assert.ThrowsAsync<ScenarioSubmissionConflictException>(() => store.CreateAsync(fixture.Project, request with { GridMinutes = 30 }, default));
        await using var sqlCheck = TenantSql(TenantA); await using var pgCheck = TenantPg(TenantA);
        Assert.That(await sqlCheck.Allocations.CountAsync(), Is.Zero); Assert.That(await sqlCheck.CommandReceipts.CountAsync(), Is.Zero);
        Assert.That((await pgCheck.Set<LicensePool>().SingleAsync()).AllocatedSeats, Is.Zero);
        Assert.That(await pgCheck.Set<TemporalScenario>().CountAsync(), Is.EqualTo(1));
        Assert.That(await pgCheck.OutboxMessages.CountAsync(m => m.Type == "v1.TemporalScenarioCalculated"), Is.EqualTo(1));
    }
    [Test]
    public async Task HypotheticalDelayAndStockAlternativesHaveExplicitImpactWithoutChangingStores()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture(stock: 1); var store = ScenarioStore();
        var baseline = await store.CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default);
        Assert.That(baseline.IsComplete, Is.False); Assert.That(baseline.Evidence.Result.Issues.Any(i => i.Code == "material_capacity_conflict"), Is.True);
        var assumptions = new ScenarioAssumptions([new(f.Equipment, new(ProjectStart, ProjectStart.AddHours(2)))],
            new Dictionary<string, int> { ["connector"] = 1 }, new Dictionary<string, int>());
        var alternative = await store.CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment, assumptions: assumptions), default);
        Assert.That(alternative.IsHypothetical, Is.True); Assert.That(alternative.IsComplete, Is.True); Assert.That(alternative.FinishAt, Is.EqualTo(ProjectStart.AddHours(3)));
        Assert.That(alternative.Evidence.Basis.Stock["connector"], Is.EqualTo(1)); Assert.That(alternative.Evidence.Input.Stock["connector"], Is.EqualTo(2));
        var comparison = (await store.CompareAsync(f.Project, baseline.Id, alternative.Id, default))!;
        Assert.That(comparison.RightHypothetical, Is.True); Assert.That(comparison.ChangedTasks, Has.Count.EqualTo(2));
        await using var pg = TenantPg(TenantA); Assert.That((await pg.StockItems.SingleAsync()).Quantity, Is.EqualTo(1));
        Assert.That((await pg.Set<LicensePool>().SingleAsync()).TotalSeats, Is.EqualTo(1));
    }
    [Test]
    public async Task ConcurrentScenarioSubmissionStoresOneDecisionEventAndRejectsStaleProjectVersions()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture(); var request = TemporalRequest(f.Engineer, f.Equipment);
        var responses = await Task.WhenAll(ScenarioStore().CreateAsync(f.Project, request, default), ScenarioStore().CreateAsync(f.Project, request, default));
        Assert.That(responses[0].Digest, Is.EqualTo(responses[1].Digest));
        await ProjectStore().ReplaceAsync(f.Project, 1, f.Definition with { BudgetAmount = 2000 }, default);
        Assert.ThrowsAsync<ProjectVersionException>(() => ScenarioStore().CreateAsync(f.Project, request with { Id = Guid.NewGuid() }, default));
        await using var pg = TenantPg(TenantA); Assert.That(await pg.Set<TemporalScenario>().CountAsync(), Is.EqualTo(1));
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "v1.TemporalScenarioCalculated"), Is.EqualTo(1));
    }
    [Test]
    public async Task ScenarioHttpEnforcesProjectMembershipAndRetainsBoundedMetadataPages()
    {
        Guid project; Guid scenarioId;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        {
            var f = await TemporalFixture(); project = f.Project; var scenario = await ScenarioStore().CreateAsync(project, TemporalRequest(f.Engineer, f.Equipment), default); scenarioId = scenario.Id;
            await ProjectStore().SetMemberAsync(project, 1, "viewer", ProjectRole.Viewer, default);
        }
        await using var factory = new ProjectApiFactory(_workforceOptions, _fulfillmentOptions);
        using var owner = factory.Actor("owner", TenantA); using var viewer = factory.Actor("viewer", TenantA, managers: false);
        using var outsider = factory.Actor("outsider", TenantA); using var foreign = factory.Actor("owner", TenantB);
        var read = await ProjectBody(await viewer.GetAsync($"/api/v1/projects/{project}/scenarios/{scenarioId}"), HttpStatusCode.OK);
        Assert.That(read.GetProperty("isComplete").GetBoolean(), Is.True);
        var page = await ProjectBody(await viewer.GetAsync($"/api/v1/projects/{project}/scenarios?limit=1"), HttpStatusCode.OK);
        Assert.That(page.GetArrayLength(), Is.EqualTo(1)); Assert.That(page[0].TryGetProperty("evidence", out _), Is.False);
        Assert.That((await outsider.GetAsync($"/api/v1/projects/{project}/scenarios/{scenarioId}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await foreign.GetAsync($"/api/v1/projects/{project}/scenarios/{scenarioId}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await viewer.PostAsync($"/api/v1/projects/{project}/scenarios/{scenarioId}/replay", null)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        var replay = await ProjectBody(await owner.PostAsync($"/api/v1/projects/{project}/scenarios/{scenarioId}/replay", null), HttpStatusCode.OK);
        Assert.That(replay.GetProperty("matchesOriginalDecision").GetBoolean(), Is.True);
    }
    [Test]
    public async Task ScenarioCapturesCommittedBusyPeriodsAndExcludesForeignResources()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture();
        await using (var sql = TenantSql(TenantA)) { sql.Add(Allocation.Reserve(f.Engineer, Guid.NewGuid(), new(ProjectStart, ProjectStart.AddHours(1)), [])); await sql.SaveChangesAsync(); }
        var scenario = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default);
        Assert.That(scenario.FinishAt, Is.EqualTo(ProjectStart.AddHours(3)));
        Assert.That(scenario.Evidence.Input.Candidates.Single(c => c.Id == f.Engineer).BusyPeriods, Has.Count.EqualTo(1));
        Guid foreign;
        using (TenantExecution.Enter(TenantScope.For(TenantB))) foreign = await RegisterTenantResource(TenantB);
        Assert.ThrowsAsync<FusionOps.Domain.Catalog.ResourceAdmissionException>(() => ScenarioStore().CreateAsync(f.Project,
            TemporalRequest(f.Engineer, f.Equipment) with { CandidateIds = [foreign] }, default));
        await using var sqlCheck = TenantSql(TenantA); Assert.That(await sqlCheck.Allocations.CountAsync(), Is.EqualTo(1));
    }
    [Test]
    public async Task ScenarioHttpCreationReplayHypotheticalBindingAndStaleVersionContract()
    {
        Guid project, engineer, equipment;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        { var f = await TemporalFixture(); project = f.Project; engineer = f.Engineer; equipment = f.Equipment; }
        await using var factory = new ProjectApiFactory(_workforceOptions, _fulfillmentOptions); using var owner = factory.Actor("owner", TenantA);
        var route = $"/api/v1/projects/{project}/scenarios"; var request = TemporalRequest(engineer, equipment);
        var response = await owner.PostAsJsonAsync(route, request, ProjectJson); var created = await ProjectBody(response, HttpStatusCode.Created);
        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo($"{route}/{request.Id:D}"));
        Assert.That(response.Headers.ETag?.Tag, Is.EqualTo($"\"{created.GetProperty("digest").GetString()}\""));
        Assert.That(created.GetProperty("evidence").GetProperty("validation").GetProperty("isFeasible").GetBoolean(), Is.True);
        var replay = await ProjectBody(await owner.PostAsJsonAsync(route, request, ProjectJson), HttpStatusCode.Created);
        Assert.That(replay.GetProperty("digest").GetString(), Is.EqualTo(created.GetProperty("digest").GetString()));
        Assert.That((await owner.PostAsJsonAsync(route, request with { MaxSearchNodes = 500 }, ProjectJson)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await owner.PostAsJsonAsync(route, request with { Id = Guid.NewGuid(), ExpectedProjectVersion = 99 }, ProjectJson)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        var assumption = new ScenarioAssumptions([new(equipment, new(ProjectStart, ProjectStart.AddHours(2)))], new Dictionary<string, int>(), new Dictionary<string, int>());
        var alternateRequest = TemporalRequest(engineer, equipment, assumptions: assumption);
        var alternate = await ProjectBody(await owner.PostAsJsonAsync(route, alternateRequest, ProjectJson), HttpStatusCode.Created);
        Assert.That(alternate.GetProperty("isHypothetical").GetBoolean(), Is.True);
        var comparison = await ProjectBody(await owner.GetAsync($"{route}/compare?left={request.Id}&right={alternateRequest.Id}"), HttpStatusCode.OK);
        Assert.That(comparison.GetProperty("finishDeltaMinutes").GetDouble(), Is.EqualTo(60));
        Assert.That(comparison.GetProperty("costDelta").GetDecimal(), Is.Zero);
    }
    [Test]
    public async Task ScenarioEvidenceIsAppendOnlyForRestrictedPrincipalsAndOrmWrites()
    {
        Guid id;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        { var f = await TemporalFixture(); id = (await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default)).Id; }
        await using (var admin = new FulfillmentContext(_fulfillmentOptions, TenantScope.System))
            await admin.Database.ExecuteSqlRawAsync("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fusionops_scenario_test') THEN CREATE ROLE fusionops_scenario_test LOGIN PASSWORD 'FusionOps_test-2026!' NOSUPERUSER NOBYPASSRLS; END IF; END $$; GRANT USAGE ON SCHEMA public TO fusionops_scenario_test; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fusionops_scenario_test;");
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = "fusionops_scenario_test", Password = "FusionOps_test-2026!" };
        var options = new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql(connection.ConnectionString).Options;
        await using (var foreign = new FulfillmentContext(options, TenantScope.For(TenantB)))
            Assert.That(await foreign.Set<TemporalScenario>().IgnoreQueryFilters().CountAsync(), Is.Zero);
        await using var pg = new FulfillmentContext(options, TenantScope.For(TenantA));
        Assert.That(await pg.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops_temporal_scenarios SET \"Digest\"='changed' WHERE \"Id\"={id}"), Is.Zero);
        Assert.That(await pg.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ops_temporal_scenarios WHERE \"Id\"={id}"), Is.Zero);
        var scenario = await pg.Set<TemporalScenario>().SingleAsync(); scenario.VerifyEvidence();
        pg.Entry(scenario).Property(s => s.TotalCost).CurrentValue = 0;
        Assert.That(Assert.ThrowsAsync<TemporalModelException>(() => pg.SaveChangesAsync())!.Code, Is.EqualTo("scenario_immutable"));
    }
    private sealed class PauseSnapshot : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ResourceCatalog WITH (UPDLOCK", StringComparison.Ordinal))
            { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
    [Test]
    public async Task ProjectRevisionDuringCrossStoreCaptureCannotPersistAStaleScenario()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture(); var pause = new PauseSnapshot();
        var options = new DbContextOptionsBuilder<WorkforceContext>(_workforceOptions).AddInterceptors(pause).Options;
        var create = ScenarioStore(new WorkforceContextFactory(options)).CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ProjectStore().ReplaceAsync(f.Project, 1, f.Definition with { BudgetAmount = 2000 }, default);
        }
        finally { pause.Release.TrySetResult(); }
        Assert.ThrowsAsync<ProjectVersionException>(async () => await create);
        await using var pg = TenantPg(TenantA); Assert.That(await pg.Set<TemporalScenario>().CountAsync(), Is.Zero);
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "v1.TemporalScenarioCalculated"), Is.Zero);
    }
}
