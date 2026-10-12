using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Tests;

public sealed class ResourcePlannerTests
{
    private static readonly TimeRange Period = new(DateTimeOffset.Parse("2026-10-20T08:00:00Z"), DateTimeOffset.Parse("2026-10-20T10:00:00Z"));
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    private static DemandSlot Slot(int id, Dictionary<string, int> skills) => new(Id(id), $"Slot {id}", ResourceKind.Human, skills);
    private static ResourceCandidate Candidate(int id, decimal rate, Dictionary<string, int> skills) =>
        new(Id(id), $"Person {id}", ResourceKind.Human, Money.Usd(rate), skills, []);

    [Test]
    public void SpecialistIsPreservedForTheOnlySlotTheyCanFill()
    {
        var request = new PlanningRequest(Id(100), Period, Currency.USD,
            [Slot(1, new()), Slot(2, new() { ["CSharp"] = 3 })],
            [Candidate(10, 10, new() { ["csharp"] = 4 }), Candidate(11, 20, new())]);
        var result = new ResourcePlanner().Plan(request);
        Assert.That(result.IsComplete, Is.True);
        Assert.That(result.Assignments.Single(a => a.SlotId == Id(2)).ResourceId, Is.EqualTo(Id(10)));
        Assert.That(result.TotalCost.Amount, Is.EqualTo(60));
        Assert.That(result.Objective, Is.EqualTo(result.DualObjective));
    }

    [Test]
    public void UnavailableAndWrongCurrencyCandidatesAreExplained()
    {
        var busy = Candidate(10, 10, new()) with { BusyPeriods = [Period] };
        var euro = Candidate(11, 10, new()) with { HourRate = Money.Eur(10) };
        var result = new ResourcePlanner().Plan(new(Id(100), Period, Currency.USD, [Slot(1, new())], [busy, euro]));
        Assert.That(result.IsComplete, Is.False);
        Assert.That(result.UnfilledSlots.Count, Is.EqualTo(1));
        Assert.That(result.Rejections.Select(r => r.Reason), Has.Some.Contains("booked"));
        Assert.That(result.Rejections.Select(r => r.Reason), Has.Some.Contains("Currency"));
        Assert.That(result.TotalCost.Amount, Is.Zero);
    }

    [Test]
    public void MaximumFulfillmentTakesPrecedenceOverCost()
    {
        var result = new ResourcePlanner().Plan(new(Id(100), Period, Currency.USD,
            [Slot(1, new()), Slot(2, new())], [Candidate(10, 0, new()), Candidate(11, 1000, new())]));
        Assert.That(result.IsComplete, Is.True);
        Assert.That(result.TotalCost.Amount, Is.EqualTo(2000));
    }

    [Test]
    public void ReorderedInputProducesTheSameAssignmentsAndDoesNotMutateCandidates()
    {
        var candidates = new[] { Candidate(10, 10, new()), Candidate(11, 10, new()) };
        var slots = new[] { Slot(1, new()), Slot(2, new()) };
        var planner = new ResourcePlanner();
        var first = planner.Plan(new(Id(100), Period, Currency.USD, slots, candidates));
        var second = planner.Plan(new(Id(100), Period, Currency.USD, slots.Reverse().ToArray(), candidates.Reverse().ToArray()));
        Assert.That(second.Assignments, Is.EqualTo(first.Assignments));
        Assert.That(candidates.All(c => c.BusyPeriods.Count == 0), Is.True);
    }

    [Test]
    public void DuplicateResourcesAndOversizedRequestsAreRejected()
    {
        var candidate = Candidate(10, 10, new());
        Assert.Throws<DomainException>(() => new ResourcePlanner().Plan(new(Id(100), Period, Currency.USD, [], [candidate, candidate])));
        Assert.Throws<DomainException>(() => new ResourcePlanner().Plan(new(Id(100), Period, Currency.USD,
            Enumerable.Range(1, 51).Select(i => Slot(i, new())).ToArray(), [])));
    }
}
