using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Services;

/// <summary>
/// Greedy heuristic that selects the cheapest available resources individually without
/// attempting global cost minimisation.
/// </summary>
public sealed class GreedyOptimizerStrategy : IOptimizerStrategy
{
    public Task<IReadOnlyCollection<Allocation>> AllocateAsync(IReadOnlyCollection<HumanResource> humans,
                                                               IReadOnlyCollection<EquipmentResource> equipment,
                                                               int requiredHumans,
                                                               int requiredEquipment)
    {
        var selectedHumans = humans.OrderBy(h => h.HourRate.Amount)
                                   .Take(requiredHumans)
                                   .ToList();

        var selectedEquipment = equipment.OrderBy(e => e.HourRate.Amount)
                                         .Take(requiredEquipment)
                                         .ToList();

        var allocations = BuildAllocations(selectedHumans, selectedEquipment);
        return Task.FromResult<IReadOnlyCollection<Allocation>>(allocations);
    }

    private static IReadOnlyCollection<Allocation> BuildAllocations(IEnumerable<HumanResource> humans,
                                                                    IEnumerable<EquipmentResource> equipment)
    {
        var period = new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        var projectId = Guid.Empty;
        var list = new List<Allocation>();

        foreach (var human in humans)
        {
            var allocation = Allocation.Reserve(human.Id.Value, projectId, period, Array.Empty<Allocation>());
            list.Add(allocation);
        }

        foreach (var eq in equipment)
        {
            var allocation = Allocation.Reserve(eq.Id.Value, projectId, period, Array.Empty<Allocation>());
            list.Add(allocation);
        }

        return list;
    }
} 