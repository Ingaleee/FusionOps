using System.Text.Json;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Tests;

public sealed class TemporalPlannerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-20T08:00:00Z");
    private static readonly Guid Site = Id(900);
    private static Guid Id(int i) => new(i, 0, 0, new byte[8]);
    private static ResourceCandidate Resource(int id, decimal rate, int skill = 4, ResourceKind kind = ResourceKind.Human, string? type = null,
        params TimeRange[] busy) => new(Id(id), $"Resource {id}", kind, Money.Usd(rate), new Dictionary<string, int> { ["csharp"] = skill }, busy, type);
    private static ProjectTaskDefinition Task(int id, int duration = 60, IReadOnlyList<Guid>? dependencies = null, int skill = 1) =>
        new(Id(id), $"Task {id}", Site, duration, Start, Start.AddHours(4), dependencies ?? [],
            [new(Id(id + 100), $"Slot {id}", ResourceKind.Human, new Dictionary<string, int> { ["csharp"] = skill })], [], []);
    private static TemporalPlanningInput Input(IReadOnlyList<ProjectTaskDefinition> tasks, params ResourceCandidate[] resources) => new(Id(1000),
        new("Installation", new(Start, Start.AddHours(4)), 10000, "USD", [new(Site, "Site", [new(Start, Start.AddHours(4))])], tasks, []),
        resources, new Dictionary<string, int>(), new Dictionary<string, int>(), GridMinutes: 60, MaxSearchNodes: 1_000_000, MaxElapsedMilliseconds: 30000);
    [Test]
    public void SchedulerJointlyChoosesTimeAndSpecialistsInsteadOfKeepingAGreedyAssignment()
    {
        var general = Task(1) with { DueAt = Start.AddHours(1) };
        var specialist = Task(2, skill: 5) with { DueAt = Start.AddHours(1) };
        var input = Input([general, specialist], Resource(200, 10, 5), Resource(201, 20, 1));
        var result = new TemporalPlanner().Plan(input);
        Assert.That(result.IsComplete, Is.True); Assert.That(result.SearchComplete, Is.True);
        Assert.That(result.TotalCost, Is.EqualTo(30));
        Assert.That(result.Tasks.Single(t => t.TaskId == general.Id).Resources.Single().ResourceId, Is.EqualTo(Id(201)));
        Assert.That(result.Tasks.Single(t => t.TaskId == specialist.Id).Resources.Single().ResourceId, Is.EqualTo(Id(200)));
        Assert.That(new TemporalPlanValidator().Validate(input, result.Tasks).IsFeasible, Is.True);
    }
    [Test]
    public void DependenciesCalendarsExistingBookingsAndEquipmentFormOneResourceBundle()
    {
        var design = Task(1); var install = Task(2, dependencies: [design.Id]) with
        { Resources = [Task(2).Resources[0], new(Id(400), "Crane", ResourceKind.Equipment, new Dictionary<string, int>(), "crane")] };
        var input = Input([install, design], Resource(200, 10, busy: [new(Start, Start.AddHours(1))]), Resource(201, 5, kind: ResourceKind.Equipment, type: "crane"));
        input = input with { Definition = input.Definition with { Sites = [new(Site, "Split shift", [new(Start.AddHours(1), Start.AddHours(3))])] } };
        var result = new TemporalPlanner().Plan(input);
        Assert.That(result.IsComplete, Is.True); Assert.That(result.TotalCost, Is.EqualTo(25)); Assert.That(result.FinishAt, Is.EqualTo(Start.AddHours(3)));
        Assert.That(result.Tasks.Single(t => t.TaskId == install.Id).Period.Start, Is.EqualTo(Start.AddHours(2)));
        Assert.That(result.Tasks.Single(t => t.TaskId == install.Id).Resources, Has.Count.EqualTo(2));
    }
    [Test]
    public void HalfOpenLicenseCapacityAllowsAdjacentTasksAndMaterialPricesEnterTheBudget()
    {
        var a = Task(1) with { Licenses = [new("compiler", 1)], Materials = [new("part", 2)] };
        var b = Task(2) with { Licenses = [new("compiler", 1)], Materials = [new("part", 1)] };
        var input = Input([a, b], Resource(200, 10), Resource(201, 10)) with
        { Stock = new Dictionary<string, int> { ["part"] = 3 }, LicenseCapacity = new Dictionary<string, int> { ["compiler"] = 1 },
            MaterialUnitCosts = new Dictionary<string, Money> { ["part"] = Money.Usd(5) } };
        var result = new TemporalPlanner().Plan(input);
        Assert.That(result.IsComplete, Is.True); Assert.That(result.FinishAt, Is.EqualTo(Start.AddHours(2))); Assert.That(result.TotalCost, Is.EqualTo(35));
        Assert.That(result.Tasks[0].Period.Overlaps(result.Tasks[1].Period), Is.False);
        var tight = input with { Definition = input.Definition with { BudgetAmount = 34 } };
        var rejected = new TemporalPlanner().Plan(tight);
        Assert.That(rejected.IsComplete, Is.False); Assert.That(rejected.SearchComplete, Is.True);
        Assert.That(rejected.Issues.Any(i => i.Code == "project_budget_conflict"), Is.True);
    }
    [Test]
    public void ProvenShortageAndExhaustedSearchHaveDifferentMeaning()
    {
        var input = Input([Task(1)], Resource(200, 10));
        var bounded = new TemporalPlanner().Plan(input with { MaxSearchNodes = 1 });
        Assert.That(bounded.SearchComplete, Is.False); Assert.That(bounded.Termination, Is.EqualTo("node_budget_exhausted"));
        Assert.That(bounded.ExploredNodes, Is.EqualTo(1)); Assert.That(bounded.Issues.Any(i => i.Code == "no_feasible_schedule_on_grid"), Is.False);
        var noResource = new TemporalPlanner().Plan(input with { Candidates = [] });
        Assert.That(noResource.IsComplete, Is.False); Assert.That(noResource.SearchComplete, Is.True);
        Assert.That(noResource.Issues.Any(i => i.Code == "resource_requirement_shortage"), Is.True);
        var incumbent = new TemporalPlanner().Plan(input with { MaxSearchNodes = 6 });
        Assert.That(incumbent.IsComplete, Is.True); Assert.That(incumbent.SearchComplete, Is.False);
        Assert.That(incumbent.TotalCost, Is.EqualTo(10)); Assert.That(incumbent.CostLowerBound, Is.EqualTo(10));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new TemporalPlanner().Plan(input, cancellation.Token));
    }
    [Test]
    public void RetainedScenarioEvidenceCannotBeChangedThroughReturnedInputsAndDetectsTampering()
    {
        var input = Input([Task(1)], Resource(200, 10)); var tenant = Id(5000);
        var result = new TemporalPlanner().Plan(input);
        var source = new TemporalSourceManifest(1, 1, 1,
            ProjectDefinitionCodec.Digest(tenant, input.ProjectId, 1, ProjectDefinitionCodec.Canonicalize(input.Definition)), Start, Start,
            [new(Id(200), 1)], [], []);
        var scenario = TemporalScenario.Create(Id(7000), tenant, "owner", new string('A', 64),
            new(source, input, ScenarioAssumptions.None, input, result, new TemporalPlanValidator().Validate(input, result.Tasks, false)), Start);
        var returned = scenario.Evidence;
        ((IList<ScheduledProjectTask>)returned.Result.Tasks)[0] = returned.Result.Tasks[0] with { Cost = 0 };
        Assert.That(scenario.Evidence.Result.Tasks[0].Cost, Is.EqualTo(10)); scenario.VerifyEvidence();
        typeof(TemporalScenario).GetProperty(nameof(TemporalScenario.EvidenceJson))!.SetValue(scenario, scenario.EvidenceJson.Replace("\"cost\":10", "\"cost\":0", StringComparison.Ordinal));
        Assert.That(Assert.Throws<TemporalModelException>(() => scenario.VerifyEvidence())!.Code, Is.EqualTo("scenario_evidence_invalid"));
    }
    [Test]
    public void IndependentValidatorRejectsForgedCostsTimeAndPartialResourceBundles()
    {
        var input = Input([Task(1), Task(2, dependencies: [Id(1)])], Resource(200, 10));
        var original = new TemporalPlanner().Plan(input); var validator = new TemporalPlanValidator();
        var wrongCost = original.Tasks.Select(t => t with { Cost = 0 }).ToArray();
        Assert.That(validator.Validate(input, wrongCost).Violations.Any(v => v.Code == "task_cost_mismatch"), Is.True);
        var reversed = original.Tasks.Select(t => t with { Period = new(Start, Start.AddHours(1)) }).ToArray();
        var invalid = validator.Validate(input, reversed);
        Assert.That(invalid.Violations.Any(v => v.Code == "dependency_order_conflict"), Is.True);
        Assert.That(invalid.Violations.Any(v => v.Code == "scheduled_resource_conflict"), Is.True);
        Assert.That(validator.Validate(input, [original.Tasks[0]]).IsComplete, Is.False);
        Assert.That(validator.Validate(input, original.Tasks.Select(t => t with { Resources = [] }).ToArray()).IsFeasible, Is.False);
        var offGrid = original.Tasks.Select(t => t with { Period = new(t.Period.Start.AddMinutes(1), t.Period.End.AddMinutes(1)) }).ToArray();
        Assert.That(validator.Validate(input, offGrid).Violations.Any(v => v.Code == "task_grid_conflict"), Is.True);
    }
    [Test]
    public void CancellationDuringLargeSearchStopsWithoutWaitingForTheNodeBudget()
    {
        var task = Task(1) with { DueAt = Start.AddDays(300) };
        var input = Input([task], Enumerable.Range(200, 60).Select(i => Resource(i, 10)).ToArray());
        input = input with { GridMinutes = 1, Definition = input.Definition with { Horizon = new(Start, Start.AddDays(300)),
            Sites = [new(Site, "Long horizon", [new(Start, Start.AddDays(300))])] } };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        Assert.Throws<OperationCanceledException>(() => new TemporalPlanner().Plan(input, cancellation.Token));
    }
    [Test]
    public void EquivalentInputOrderKeepsDecisionsAndNodeCountsDeterministic()
    {
        var input = Input([Task(1), Task(2)], Resource(200, 10), Resource(201, 20));
        var a = new TemporalPlanner().Plan(input);
        var b = new TemporalPlanner().Plan(input with { Candidates = input.Candidates.Reverse().ToArray(), Definition = input.Definition with { Tasks = input.Definition.Tasks.Reverse().ToArray() } });
        Assert.That(JsonSerializer.Serialize(a with { ElapsedMilliseconds = 0 }, TemporalInputCodec.Json),
            Is.EqualTo(JsonSerializer.Serialize(b with { ElapsedMilliseconds = 0 }, TemporalInputCodec.Json)));
        Assert.That(TemporalInputCodec.Write(input), Is.EqualTo(TemporalInputCodec.Write(input with { Candidates = input.Candidates.Reverse().ToArray() })));
    }
    [Test]
    public void SeededSmallSchedulesAgreeWithAnIndependentCartesianOracle()
    {
        var random = new Random(3819);
        for (var sample = 0; sample < 100; sample++)
        {
            var count = random.Next(1, 4);
            var tasks = Enumerable.Range(1, count).Select(i => Task(i, duration: random.Next(1, 3) * 60,
                dependencies: i > 1 && random.Next(2) == 0 ? [Id(i - 1)] : []) with { DueAt = Start.AddHours(3) }).ToArray();
            var candidates = new[] { Resource(200, random.Next(5, 20), busy: random.Next(2) == 0 ? [new(Start, Start.AddHours(1))] : []), Resource(201, random.Next(5, 20)) };
            var input = Input(tasks, candidates) with { Definition = Input(tasks, candidates).Definition with { BudgetAmount = random.Next(20, 100) } };
            var expected = Oracle(input); var actual = new TemporalPlanner().Plan(input);
            Assert.That(actual.SearchComplete, Is.True, $"sample {sample}"); Assert.That(actual.IsComplete, Is.EqualTo(expected is not null), $"sample {sample}");
            if (expected is { } value)
            { Assert.That(actual.TotalCost, Is.EqualTo(value.Cost), $"sample {sample}"); Assert.That(actual.FinishAt, Is.EqualTo(value.Finish), $"sample {sample}"); }
        }
    }
    // Independent Cartesian enumeration has no production graph ordering, search pruning, Tick or validator.
    private static (decimal Cost, DateTimeOffset Finish)? Oracle(TemporalPlanningInput input)
    {
        var tasks = input.Definition.Tasks.ToArray(); var chosen = new (ProjectTaskDefinition Task, ResourceCandidate Resource, DateTimeOffset Start, DateTimeOffset End)[tasks.Length];
        (decimal Cost, DateTimeOffset Finish)? best = null;
        void Enumerate(int index)
        {
            if (index < tasks.Length)
            {
                var task = tasks[index];
                for (var hour = 0; hour < 4; hour++) foreach (var resource in input.Candidates)
                {
                    var start = Start.AddHours(hour); var end = start.AddMinutes(task.DurationMinutes);
                    if (end > task.DueAt || resource.BusyPeriods.Any(p => p.Start < end && start < p.End)) continue;
                    chosen[index] = (task, resource, start, end); Enumerate(index + 1);
                }
                return;
            }
            foreach (var task in chosen)
            {
                if (task.Task.Dependencies.Any(d => chosen.Single(p => p.Task.Id == d).End > task.Start)) return;
                if (chosen.Any(o => o.Task.Id != task.Task.Id && o.Resource.Id == task.Resource.Id && o.Start < task.End && task.Start < o.End)) return;
            }
            var cost = chosen.Sum(t => t.Resource.HourRate.Amount * t.Task.DurationMinutes / 60m); if (cost > input.Definition.BudgetAmount) return;
            var finish = chosen.Max(t => t.End);
            if (best is null || cost < best.Value.Cost || cost == best.Value.Cost && finish < best.Value.Finish) best = (cost, finish);
        }
        Enumerate(0); return best;
    }
}
