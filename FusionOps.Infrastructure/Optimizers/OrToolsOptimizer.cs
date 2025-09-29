using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.ValueObjects;
using Google.OrTools.LinearSolver;
using System.Diagnostics.Metrics;

namespace FusionOps.Infrastructure.Optimizers;

public sealed class OrToolsOptimizer : IOptimizerStrategy
{
    private static readonly Meter Meter = new("FusionOps.Optimizer");
    private static readonly Counter<long> Calls = Meter.CreateCounter<long>("optimizer_ilp_calls_total", unit: "calls", description: "Total ILP optimizer calls");
    private static readonly Histogram<double> DurationMs = Meter.CreateHistogram<double>("optimizer_ilp_duration_ms", unit: "ms", description: "ILP solve duration");
    private static readonly Counter<long> StatusCounter = Meter.CreateCounter<long>("optimizer_status_total", unit: "calls", description: "ILP status by result");

    public Task<IReadOnlyCollection<Allocation>> AllocateAsync(IReadOnlyCollection<HumanResource> humans,
                                                               IReadOnlyCollection<EquipmentResource> equipment,
                                                               int requiredHumans,
                                                               int requiredEquipment)
    {
        Calls.Add(1);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var solver = Solver.CreateSolver("SCIP");
        if (solver is null)
        {
            var selected = GreedySelect(humans, equipment, requiredHumans, requiredEquipment);
            sw.Stop();
            DurationMs.Record(sw.Elapsed.TotalMilliseconds);
            return Task.FromResult(selected);
        }

        var humanVars = humans.Select((h, i) => solver.MakeBoolVar($"h_{i}")).ToArray();
        var eqVars = equipment.Select((e, i) => solver.MakeBoolVar($"e_{i}")).ToArray();

        var humanCt = solver.MakeConstraint(requiredHumans, requiredHumans, "human_count");
        foreach (var v in humanVars) humanCt.SetCoefficient(v, 1.0);
        if (eqVars.Length > 0)
        {
            var eqCt = solver.MakeConstraint(requiredEquipment, requiredEquipment, "equipment_count");
            foreach (var v in eqVars) eqCt.SetCoefficient(v, 1.0);
        }
        else if (requiredEquipment != 0)
        {
            sw.Stop();
            DurationMs.Record(sw.Elapsed.TotalMilliseconds);
            return Task.FromResult<IReadOnlyCollection<Allocation>>(Array.Empty<Allocation>());
        }

        var objective = solver.Objective();
        for (int i = 0; i < humanVars.Length; i++)
        {
            objective.SetCoefficient(humanVars[i], (double)humans.ElementAt(i).HourRate.Amount);
        }
        for (int i = 0; i < eqVars.Length; i++)
        {
            objective.SetCoefficient(eqVars[i], (double)equipment.ElementAt(i).HourRate.Amount);
        }
        objective.SetMinimization();

        var status = solver.Solve();
        sw.Stop();
        DurationMs.Record(sw.Elapsed.TotalMilliseconds);
        var statusTag = status.ToString();
        StatusCounter.Add(1, new KeyValuePair<string, object?>("status", statusTag));
        if (status != Solver.ResultStatus.OPTIMAL && status != Solver.ResultStatus.FEASIBLE)
        {
            return Task.FromResult<IReadOnlyCollection<Allocation>>(Array.Empty<Allocation>());
        }

        var chosenHumans = new List<HumanResource>();
        var chosenEquipment = new List<EquipmentResource>();
        for (int i = 0; i < humanVars.Length; i++) if (humanVars[i].SolutionValue() > 0.5) chosenHumans.Add(humans.ElementAt(i));
        for (int i = 0; i < eqVars.Length; i++) if (eqVars[i].SolutionValue() > 0.5) chosenEquipment.Add(equipment.ElementAt(i));

        var allocations = BuildAllocations(chosenHumans, chosenEquipment);
        return Task.FromResult<IReadOnlyCollection<Allocation>>(allocations);
    }

    private static IReadOnlyCollection<Allocation> GreedySelect(IReadOnlyCollection<HumanResource> humans,
                                                                IReadOnlyCollection<EquipmentResource> equipment,
                                                                int requiredHumans,
                                                                int requiredEquipment)
    {
        var chosenHumans = humans.OrderBy(h => h.HourRate.Amount).Take(requiredHumans);
        var chosenEq = equipment.OrderBy(e => e.HourRate.Amount).Take(requiredEquipment);
        return BuildAllocations(chosenHumans, chosenEq);
    }

    private static IReadOnlyCollection<Allocation> BuildAllocations(IEnumerable<HumanResource> humans,
                                                                    IEnumerable<EquipmentResource> equipment)
    {
        var period = new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        var projectId = Guid.Empty;
        var list = new List<Allocation>();
        foreach (var h in humans)
        {
            list.Add(Allocation.Reserve(h.Id.Value, projectId, period, Array.Empty<Allocation>()));
        }
        foreach (var e in equipment)
        {
            list.Add(Allocation.Reserve(e.Id.Value, projectId, period, Array.Empty<Allocation>()));
        }
        return list;
    }
}


