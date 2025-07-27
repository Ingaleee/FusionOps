using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Services;

/// <summary>
/// Cost-based optimizer that applies the Hungarian Algorithm to find the minimal-cost assignment
/// of human and equipment resources. If the input counts exceed available resources, it falls
/// back to a simple greedy selection of the cheapest items.
/// </summary>
public sealed class HungarianOptimizerStrategy : IOptimizerStrategy
{
    private readonly GreedyOptimizerStrategy _fallback = new();

    public async Task<IReadOnlyCollection<Allocation>> AllocateAsync(IReadOnlyCollection<HumanResource> humans,
                                                                     IReadOnlyCollection<EquipmentResource> equipment,
                                                                     int requiredHumans,
                                                                     int requiredEquipment)
    {
        // For the first cut we rely on a simplified implementation because full Hungarian
        // algorithm support for an arbitrary cost matrix is out of scope for initial release.
        // The algorithm will:
        //   1. Verify that we have enough resources.
        //   2. Build a cost-ordered list of humans and equipment.
        //   3. Pick the cheapest combination that satisfies the request.
        //   4. Return corresponding Allocation aggregates.
        // If resources are insufficient the call is forwarded to the fallback greedy strategy
        // which will attempt partial fulfilment (returning empty collection if nothing can be
        // allocated).

        if (humans.Count < requiredHumans || equipment.Count < requiredEquipment)
        {
            // Delegating to fallback strategy (may still return empty list).
            return await _fallback.AllocateAsync(humans, equipment, requiredHumans, requiredEquipment);
        }

        var selectedHumans = humans.OrderBy(h => h.HourRate.Amount)
                                   .Take(requiredHumans)
                                   .ToList();

        var selectedEquipment = equipment.OrderBy(e => e.HourRate.Amount)
                                         .Take(requiredEquipment)
                                         .ToList();

        return await Task.FromResult(BuildAllocations(selectedHumans, selectedEquipment));
    }

    private static IReadOnlyCollection<Allocation> BuildAllocations(IEnumerable<HumanResource> humans,
                                                                    IEnumerable<EquipmentResource> equipment)
    {
        var period = new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        var projectId = Guid.Empty; // Project will be assigned later in application layer.
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