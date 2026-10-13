using FusionOps.Domain.Enumerations;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Services.Planning;

public enum ResourceKind { Human, Equipment }

public sealed record DemandSlot(Guid Id, string Name, ResourceKind Kind,
    IReadOnlyDictionary<string, int> RequiredSkills, string? EquipmentType = null);

public sealed record ResourceCandidate(Guid Id, string Name, ResourceKind Kind, Money HourRate,
    IReadOnlyDictionary<string, int> Skills, IReadOnlyList<TimeRange> BusyPeriods, string? EquipmentType = null);

public sealed record PlanningRequest(Guid ProjectId, TimeRange Period, Currency Currency,
    IReadOnlyList<DemandSlot> Slots, IReadOnlyList<ResourceCandidate> Candidates);

public sealed record PlannedAssignment(Guid SlotId, string SlotName, Guid ResourceId, string ResourceName, Money Cost);
public sealed record RejectedCandidate(Guid SlotId, Guid ResourceId, string Reason);
public sealed record UnfilledSlot(Guid SlotId, string Name, string Reason);
public sealed record PlanningResult(
    Guid ProjectId, string Algorithm, bool IsComplete, Money TotalCost,
    IReadOnlyList<PlannedAssignment> Assignments, IReadOnlyList<UnfilledSlot> UnfilledSlots,
    IReadOnlyList<RejectedCandidate> Rejections, decimal Objective, decimal DualObjective);
