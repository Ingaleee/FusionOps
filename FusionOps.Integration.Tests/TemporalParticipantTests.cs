using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using FusionOps.Domain.ValueObjects;
using FusionOps.Infrastructure.Execution;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FusionOps.Integration.Tests;

public sealed partial class ReliabilityTests
{
    private SqlTemporalWorkforceParticipant TemporalWorkforce() => new(new WorkforceContextFactory(_workforceOptions), TimeProvider.System);
    private PostgresTemporalLicenseParticipant TemporalLicenses() => new(new TestContextFactory(_fulfillmentOptions), TimeProvider.System);
    private async Task<(Guid Project, Guid Engineer, Guid Equipment, ExecutionBaseline Baseline)> ParticipantFixture()
    {
        var f = await TemporalFixture(); var scenario = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default);
        var plan = await PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), scenario.Id, 1), default);
        return (f.Project, f.Engineer, f.Equipment, plan.Baseline);
    }
    [Test]
    public async Task TemporalWorkforceReservesActualIntervalsAndReplaysOriginalCommitAfterSourceChange()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ParticipantFixture();
        var first = await TemporalWorkforce().ReserveAsync(f.Baseline, default);
        Assert.That(first.Succeeded, Is.True); Assert.That(first.Allocations, Has.Count.EqualTo(3));
        await CatalogStore().ReviseAsync(f.Engineer, 1, new("Changed after commit", Money.Usd(99), new Dictionary<string, int>(), null), default);
        var replay = await TemporalWorkforce().ReserveAsync(f.Baseline, default);
        Assert.That(replay.Allocations, Is.EqualTo(first.Allocations));
        await using var sql = TenantSql(TenantA); var allocations = await sql.Allocations.ToListAsync();
        Assert.That(allocations, Has.Count.EqualTo(3));
        Assert.That(allocations.Count(a => a.ResourceId == f.Engineer), Is.EqualTo(2));
        Assert.That(allocations.Single(a => a.ResourceId == f.Equipment).Period, Is.EqualTo(new TimeRange(ProjectStart.AddHours(1), ProjectStart.AddHours(2))));
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(1));
        Assert.That(await sql.OutboxMessages.CountAsync(m => m.Type == "resource_allocated" || m.Type == "v1.ResourceAllocated"), Is.EqualTo(3));
    }
    [Test]
    public async Task TemporalWorkforceCompensationPreservesOtherOwnersAndLateReserveReplayCannotRecreateEffects()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ParticipantFixture();
        var first = await TemporalWorkforce().ReserveAsync(f.Baseline, default);
        Guid other;
        await using (var sql = TenantSql(TenantA))
        {
            var allocation = Allocation.Reserve(f.Engineer, Guid.NewGuid(), new(ProjectStart.AddHours(2), ProjectStart.AddHours(3)), []);
            other = allocation.Id.Value; sql.Add(allocation); await sql.SaveChangesAsync();
        }
        await TemporalWorkforce().ReleaseAsync(f.Baseline, default); await TemporalWorkforce().ReleaseAsync(f.Baseline, default);
        Assert.That((await TemporalWorkforce().ReserveAsync(f.Baseline, default)).Allocations, Is.EqualTo(first.Allocations));
        await using var check = TenantSql(TenantA); var remaining = await check.Allocations.Where(a => !a.IsCancelled).ToListAsync();
        Assert.That(remaining.Select(a => a.Id.Value), Is.EqualTo(new[] { other }));
        Assert.That(await check.CommandReceipts.CountAsync(), Is.EqualTo(2));
    }
    [Test]
    public async Task TemporalWorkforceNegativeDecisionIsAtomicAndSurvivesCompetitorCancellation()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ParticipantFixture();
        await using (var sql = TenantSql(TenantA))
        { sql.Add(Allocation.Reserve(f.Equipment, Guid.NewGuid(), new(ProjectStart.AddHours(1), ProjectStart.AddHours(2)), [])); await sql.SaveChangesAsync(); }
        var rejected = await TemporalWorkforce().ReserveAsync(f.Baseline, default);
        Assert.That(rejected.RejectionCode, Is.EqualTo("resource_conflict"));
        await using (var sql = TenantSql(TenantA))
        {
            Assert.That(await sql.Allocations.CountAsync(), Is.EqualTo(1));
            (await sql.Allocations.SingleAsync()).Cancel(); await sql.SaveChangesAsync();
        }
        var replay = await TemporalWorkforce().ReserveAsync(f.Baseline, default);
        Assert.That(replay.Succeeded, Is.False); Assert.That(replay.RejectionCode, Is.EqualTo(rejected.RejectionCode));
        await using var check = TenantSql(TenantA); Assert.That(await check.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        Assert.That(await check.CommandReceipts.CountAsync(), Is.EqualTo(1));
    }
    [Test]
    public async Task ConcurrentOriginalTemporalWorkforceDecisionCommitsOneAllocationSet()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ParticipantFixture();
        var decisions = await Task.WhenAll(TemporalWorkforce().ReserveAsync(f.Baseline, default), TemporalWorkforce().ReserveAsync(f.Baseline, default));
        Assert.That(decisions[0].Allocations, Is.EqualTo(decisions[1].Allocations));
        await using var sql = TenantSql(TenantA); Assert.That(await sql.Allocations.CountAsync(), Is.EqualTo(3));
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(1));
    }
    [Test]
    public async Task TemporalLicenseCommitIsReplayedAndBecomesAuthoritativeScenarioCalendar()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ParticipantFixture();
        Assert.That((await TemporalLicenses().ReserveAsync(f.Baseline, default)).Succeeded, Is.True);
        Assert.That((await TemporalLicenses().ReserveAsync(f.Baseline, default)).Succeeded, Is.True);
        await using (var pg = TenantPg(TenantA))
        {
            var pool = await pg.Set<LicensePool>().SingleAsync(); Assert.That(pool.TemporalReservations, Has.Count.EqualTo(1));
            Assert.That(pool.AllocatedSeats, Is.Zero); Assert.That(pool.TemporalReservedPeak, Is.EqualTo(1));
            Assert.Throws<FusionOps.Domain.Shared.DomainException>(() => pool.Allocate(f.Project, 1));
            Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "v1.TemporalLicenseCapacityChanged"), Is.EqualTo(1));
        }
        var next = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment), default);
        Assert.That(next.Evidence.Basis.LicenseBookings, Has.Count.EqualTo(1));
        Assert.That(next.FinishAt, Is.EqualTo(ProjectStart.AddHours(3))); Assert.That(next.Evidence.Result.Algorithm, Does.EndWith("-v2"));
        Assert.That((await ScenarioStore().ReplayAsync(f.Project, next.Id, default))!.MatchesOriginalDecision, Is.True);
        await TemporalLicenses().ReleaseAsync(f.Baseline, default); await TemporalLicenses().ReleaseAsync(f.Baseline, default);
        Assert.That((await TemporalLicenses().ReserveAsync(f.Baseline, default)).Succeeded, Is.True, "Original receipt replays without recreating released capacity");
        await using var check = TenantPg(TenantA); Assert.That((await check.Set<LicensePool>().SingleAsync()).TemporalReservations, Is.Empty);
        Assert.That(await check.Set<TemporalLicenseEffectReceipt>().CountAsync(), Is.EqualTo(2));
    }
    [Test]
    public async Task LicenseRejectionRollsBackEveryPoolAndCannotSucceedAfterCapacityReturns()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await TemporalFixture();
        var definition = f.Definition with { Tasks = f.Definition.Tasks.Select(t => t.Licenses.Count > 0 ? t with { Licenses = [new("compiler", 1), new("z-tool", 1)] } : t).ToArray() };
        await ProjectStore().ReplaceAsync(f.Project, 1, definition, default);
        await using (var pg = TenantPg(TenantA)) { pg.Add(new LicensePool("z-tool", 1)); await pg.SaveChangesAsync(); }
        var s = await ScenarioStore().CreateAsync(f.Project, TemporalRequest(f.Engineer, f.Equipment, version: 2), default);
        var b = (await PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), s.Id, 2), default)).Baseline;
        await using (var pg = TenantPg(TenantA)) { (await pg.Set<LicensePool>().SingleAsync(p => p.Product == "z-tool")).Allocate(f.Project, 1); await pg.SaveChangesAsync(); }
        var rejected = await TemporalLicenses().ReserveAsync(b, default); Assert.That(rejected.RejectionCode, Is.EqualTo("license_capacity"));
        await using (var pg = TenantPg(TenantA))
        {
            var pool = await pg.Set<LicensePool>().SingleAsync(p => p.Product == "compiler"); Assert.That(pool.Version, Is.Zero); Assert.That(pool.TemporalReservations, Is.Empty);
            (await pg.Set<LicensePool>().SingleAsync(p => p.Product == "z-tool")).Release(f.Project, 1); await pg.SaveChangesAsync();
        }
        var replay = await TemporalLicenses().ReserveAsync(b, default);
        Assert.That(replay.Succeeded, Is.False); Assert.That(replay.RejectionCode, Is.EqualTo(rejected.RejectionCode));
        await using var check = TenantPg(TenantA);
        Assert.That(await check.OutboxMessages.CountAsync(m => m.Type == "v1.TemporalLicenseCapacityChanged"), Is.Zero);
        Assert.That(await check.Set<TemporalLicenseEffectReceipt>().CountAsync(), Is.EqualTo(1));
    }
    [Test]
    public async Task ConcurrentOriginalLicenseDecisionUsesOneReceiptAndOnePoolVersion()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ParticipantFixture();
        var decisions = await Task.WhenAll(TemporalLicenses().ReserveAsync(f.Baseline, default), TemporalLicenses().ReserveAsync(f.Baseline, default));
        Assert.That(decisions.All(d => d.Succeeded), Is.True);
        await using var pg = TenantPg(TenantA); Assert.That((await pg.Set<LicensePool>().SingleAsync()).Version, Is.EqualTo(1));
        Assert.That(await pg.Set<TemporalLicenseEffectReceipt>().CountAsync(), Is.EqualTo(1));
    }
    [Test]
    public async Task TemporalBookingAndEffectHistoryRespectActualRestrictedTenantPolicies()
    {
        Guid plan;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        { var f = await ParticipantFixture(); plan = f.Baseline.Id; await TemporalLicenses().ReserveAsync(f.Baseline, default); }
        await using (var admin = new FulfillmentContext(_fulfillmentOptions, TenantScope.System))
            await admin.Database.ExecuteSqlRawAsync("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fusionops_temporal_license_test') THEN CREATE ROLE fusionops_temporal_license_test LOGIN PASSWORD 'FusionOps_test-2026!' NOSUPERUSER NOBYPASSRLS; END IF; END $$; GRANT USAGE ON SCHEMA public TO fusionops_temporal_license_test; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fusionops_temporal_license_test;");
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = "fusionops_temporal_license_test", Password = "FusionOps_test-2026!" };
        var options = new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql(connection.ConnectionString).Options;
        await using (var foreign = new FulfillmentContext(options, TenantScope.For(TenantB)))
        {
            Assert.That(await foreign.Set<TemporalLicenseEffectReceipt>().IgnoreQueryFilters().CountAsync(), Is.Zero);
            Assert.That(await foreign.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM license_temporal_reservations").SingleAsync(), Is.Zero);
        }
        await using var pg = new FulfillmentContext(options, TenantScope.For(TenantA));
        Assert.That(await pg.Database.ExecuteSqlInterpolatedAsync($"UPDATE execution_license_effects SET \"Digest\"='changed' WHERE \"PlanId\"={plan}"), Is.Zero);
        Assert.That(await pg.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM execution_license_effects WHERE \"PlanId\"={plan}"), Is.Zero);
        var receipt = await pg.Set<TemporalLicenseEffectReceipt>().SingleAsync(); pg.Entry(receipt).Property(r => r.Digest).CurrentValue = "changed";
        Assert.ThrowsAsync<ExecutionPlanRuleException>(() => pg.SaveChangesAsync());
    }
    [Test]
    public async Task CompetingTemporalPlansCannotOverbookTheSameResourceIntervals()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ParticipantFixture();
        var other = (await PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), f.Baseline.ScenarioId, 1), default)).Baseline;
        var decisions = await Task.WhenAll(TemporalWorkforce().ReserveAsync(f.Baseline, default), TemporalWorkforce().ReserveAsync(other, default));
        Assert.That(decisions.Count(d => d.Succeeded), Is.EqualTo(1)); Assert.That(decisions.Count(d => d.RejectionCode == "resource_conflict"), Is.EqualTo(1));
        await using var sql = TenantSql(TenantA); Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.EqualTo(3));
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(2));
    }
    [Test]
    public async Task CompetingTemporalLicensePlansCannotOverbookTheSameSeatInterval()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ParticipantFixture();
        var other = (await PlanStore().CreateAsync(f.Project, new(Guid.NewGuid(), f.Baseline.ScenarioId, 1), default)).Baseline;
        var decisions = await Task.WhenAll(TemporalLicenses().ReserveAsync(f.Baseline, default), TemporalLicenses().ReserveAsync(other, default));
        Assert.That(decisions.Count(d => d.Succeeded), Is.EqualTo(1)); Assert.That(decisions.Count(d => d.RejectionCode == "license_capacity"), Is.EqualTo(1));
        await using var pg = TenantPg(TenantA); Assert.That((await pg.Set<LicensePool>().SingleAsync()).TemporalReservations, Has.Count.EqualTo(1));
        Assert.That(await pg.Set<TemporalLicenseEffectReceipt>().CountAsync(), Is.EqualTo(2));
    }
    [Test]
    public async Task AdjacentProjectLicenseReservationsReuseRealCapacity()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var f = await ParticipantFixture();
        await TemporalLicenses().ReserveAsync(f.Baseline, default);
        var project = Guid.NewGuid(); await ProjectStore().CreateAsync(project, f.Baseline.ScenarioEvidence.Input.Definition, default);
        var scenario = await ScenarioStore().CreateAsync(project, TemporalRequest(f.Engineer, f.Equipment), default);
        var next = (await PlanStore().CreateAsync(project, new(Guid.NewGuid(), scenario.Id, 1), default)).Baseline;
        Assert.That((await TemporalLicenses().ReserveAsync(next, default)).Succeeded, Is.True);
        await using var pg = TenantPg(TenantA); var pool = await pg.Set<LicensePool>().SingleAsync();
        Assert.That(pool.TemporalReservations, Has.Count.EqualTo(2)); Assert.That(pool.TemporalReservedPeak, Is.EqualTo(1));
        Assert.That(pool.TemporalReservations.Select(r => r.Period.Start).Order(), Is.EqualTo(new[] { ProjectStart.AddHours(1), ProjectStart.AddHours(2) }));
    }
}
