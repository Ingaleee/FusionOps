using System.Text.Json;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Scheduling;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Tests;

public sealed class TemporalLicenseTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-20T08:00:00Z");
    private static TemporalLicenseDemand Demand(double from, double to, int seats = 1) => new(Guid.NewGuid(), seats, new(Start.AddHours(from), Start.AddHours(to)));
    [Test]
    public void AdjacentReservationsReuseCapacityAndOverlappingReservationFailsAtomically()
    {
        var pool = new LicensePool("compiler", 1); var project = Guid.NewGuid();
        pool.ReserveTemporal(Guid.NewGuid(), project, [Demand(0, 1), Demand(1, 2)]);
        Assert.That(pool.TemporalReservedPeak, Is.EqualTo(1)); Assert.That(pool.AllocatedSeats, Is.Zero);
        Assert.Throws<LicenseCapacityException>(() => pool.ReserveTemporal(Guid.NewGuid(), project, [Demand(0.5, 1.5)]));
        Assert.That(pool.TemporalReservations, Has.Count.EqualTo(2)); Assert.That(pool.Version, Is.EqualTo(1));
    }
    [Test]
    public void PermanentAndTemporalAllocationShareCapacityInBothDirections()
    {
        var pool = new LicensePool("compiler", 3); var project = Guid.NewGuid(); var plan = Guid.NewGuid();
        pool.Allocate(project, 1); pool.ReserveTemporal(plan, project, [Demand(0, 2, 2)]);
        Assert.Throws<DomainException>(() => pool.Allocate(project, 1));
        Assert.Throws<LicenseCapacityException>(() => pool.Reserve(Guid.NewGuid(), project, 1));
        pool.ReleaseTemporal(plan, project); pool.Reserve(Guid.NewGuid(), project, 2);
        Assert.Throws<LicenseCapacityException>(() => pool.ReserveTemporal(Guid.NewGuid(), project, [Demand(3, 4)]));
        Assert.That(pool.AllocatedSeats, Is.EqualTo(3));
    }
    [Test]
    public void OriginalTemporalIdentityAndReleasePreserveOwnership()
    {
        var pool = new LicensePool("compiler", 3); var project = Guid.NewGuid(); var plan = Guid.NewGuid(); var demand = Demand(0, 1);
        pool.ReserveTemporal(plan, project, [demand]); pool.ReserveTemporal(plan, project, [demand]);
        Assert.That(pool.Version, Is.EqualTo(1));
        Assert.Throws<DomainException>(() => pool.ReserveTemporal(plan, project, [demand with { Seats = 2 }]));
        Assert.Throws<DomainException>(() => pool.ReleaseTemporal(plan, Guid.NewGuid()));
        Assert.That(pool.TemporalReservations, Has.Count.EqualTo(1));
        pool.ReleaseTemporal(plan, project); pool.ReleaseTemporal(plan, project);
        Assert.That(pool.TemporalReservations, Is.Empty); Assert.That(pool.Version, Is.EqualTo(2));
    }
    [Test]
    public void PeakMatchesIndependentDiscreteOracleAcrossSeededCalendars()
    {
        var random = new Random(613);
        for (var run = 0; run < 100; run++)
        {
            var demands = Enumerable.Range(0, 20).Select(_ => { var from = random.Next(0, 12); return Demand(from, random.Next(from + 1, 17), random.Next(1, 5)); }).ToArray();
            var expected = Enumerable.Range(0, 16).Max(hour => demands.Where(d => d.Period.Start <= Start.AddHours(hour) && Start.AddHours(hour) < d.Period.End).Sum(d => d.Seats));
            var pool = new LicensePool("compiler", expected); pool.ReserveTemporal(Guid.NewGuid(), Guid.NewGuid(), demands);
            Assert.That(pool.TemporalReservedPeak, Is.EqualTo(expected));
            var tooSmall = new LicensePool("compiler", expected - 1);
            Assert.Throws<LicenseCapacityException>(() => tooSmall.ReserveTemporal(Guid.NewGuid(), Guid.NewGuid(), demands));
            Assert.That(tooSmall.TemporalReservations, Is.Empty);
        }
    }
    [Test]
    public void PlannerAndIndependentValidatorAccountForCapturedFutureBookings()
    {
        var site = Guid.NewGuid(); var task = Guid.NewGuid(); var project = Guid.NewGuid();
        var definition = new FusionOps.Domain.Projects.ProjectDefinition("Installation", new(Start, Start.AddHours(3)), 100, "USD",
            [new(site, "Site", [new(Start, Start.AddHours(3))])],
            [new(task, "Install", site, 60, Start, Start.AddHours(3), [], [], [], [new("compiler", 1)])], []);
        var input = new TemporalPlanningInput(project, definition, [], new Dictionary<string, int>(), new Dictionary<string, int> { ["compiler"] = 1 },
            GridMinutes: 60, LicenseBookings: [new("compiler", 1, new(Start, Start.AddHours(1)))]);
        var result = new TemporalPlanner().Plan(input);
        Assert.That(result.IsComplete, Is.True); Assert.That(result.Tasks[0].Period.Start, Is.EqualTo(Start.AddHours(1)));
        Assert.That(result.Algorithm, Is.EqualTo("temporal/bounded-grid-cost-finish-v2"));
        var forged = result.Tasks[0] with { Period = new(Start, Start.AddHours(1)) };
        Assert.That(new TemporalPlanValidator().Validate(input, [forged]).Violations.Any(v => v.Code == "license_capacity_conflict"), Is.True);
    }
    [Test]
    public void PreviousPublishedScenarioProofAndDigestSurviveTheNewOptionalCalendar()
    {
        // Captured from the pre-change d820e84 Domain binary, not regenerated by the new serializer.
        using var stream = typeof(TemporalLicenseTests).Assembly.GetManifestResourceStream("FusionOps.Domain.Tests.Fixtures.legacy-temporal-proof.json")!;
        var fixture = JsonSerializer.Deserialize<LegacyProof>(stream, TemporalInputCodec.Json)!;
        var evidence = JsonSerializer.Deserialize<TemporalScenarioEvidence>(fixture.EvidenceJson, TemporalInputCodec.Json)!;
        var reconstructed = TemporalScenario.Create(fixture.Id, fixture.TenantId, fixture.Actor, fixture.RequestHash, evidence, fixture.CreatedAt);
        Assert.That(reconstructed.Digest, Is.EqualTo(fixture.Digest)); Assert.That(reconstructed.EvidenceJson, Is.EqualTo(fixture.EvidenceJson));
        Assert.That(evidence.Input.LicenseBookings, Is.Null); Assert.That(TemporalInputCodec.Write(evidence.Input), Does.Not.Contain("licenseBookings"));
        var replay = new TemporalPlanner().Plan(evidence.Input) with { ElapsedMilliseconds = 0 };
        Assert.That(JsonSerializer.Serialize(replay, TemporalInputCodec.Json), Is.EqualTo(JsonSerializer.Serialize(evidence.Result with { ElapsedMilliseconds = 0 }, TemporalInputCodec.Json)));
    }
    private sealed record LegacyProof(Guid Id, Guid TenantId, string Actor, string RequestHash, DateTimeOffset CreatedAt, string Digest, string EvidenceJson);
}
