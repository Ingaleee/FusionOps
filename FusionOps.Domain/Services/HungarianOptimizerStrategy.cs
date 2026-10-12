using FusionOps.Domain.Entities;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Services;

/// <summary>Compatibility adapter for count-only callers. The explicit planning API also supports skills and calendars.</summary>
public sealed class HungarianOptimizerStrategy : IOptimizerStrategy
{
    public Task<IReadOnlyCollection<Allocation>> AllocateAsync(IReadOnlyCollection<HumanResource> humans,
        IReadOnlyCollection<EquipmentResource> equipment, int requiredHumans, int requiredEquipment)
    {
        if (requiredHumans < 0 || requiredEquipment < 0) throw new ArgumentOutOfRangeException(nameof(requiredHumans));
        var project = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var period = new TimeRange(now, now.AddHours(1));
        var currency = humans.FirstOrDefault()?.HourRate.Currency ?? equipment.FirstOrDefault()?.HourRate.Currency ?? Currency.USD;
        var slots = Enumerable.Range(0, requiredHumans).Select(i => new DemandSlot(Guid.NewGuid(), $"Human {i}", ResourceKind.Human, new Dictionary<string, int>()))
            .Concat(Enumerable.Range(0, requiredEquipment).Select(i => new DemandSlot(Guid.NewGuid(), $"Equipment {i}", ResourceKind.Equipment, new Dictionary<string, int>())))
            .ToArray();
        var candidates = humans.Select(h => new ResourceCandidate(h.Id.Value, h.FullName, ResourceKind.Human, h.HourRate,
                h.Skills.ToDictionary(s => s.Name, s => s.Level.Value), []))
            .Concat(equipment.Select(e => new ResourceCandidate(e.Id.Value, e.Model, ResourceKind.Equipment, e.HourRate,
                new Dictionary<string, int>(), [], e.Type.Name))).ToArray();
        var result = new ResourcePlanner().Plan(new(project, period, currency, slots, candidates));
        return Task.FromResult<IReadOnlyCollection<Allocation>>(result.Assignments.Select(a => Allocation.Reserve(a.ResourceId, project, period, [])).ToArray());
    }
}
