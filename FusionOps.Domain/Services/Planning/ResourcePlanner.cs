using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Services.Planning;

/// <summary>Maximizes fulfilled slots, then minimizes cost, without mutating or reserving resources.</summary>
public sealed class ResourcePlanner
{
    public PlanningResult Plan(PlanningRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        // Stable ordering makes equal-cost decisions reproducible for the same snapshot.
        var slots = request.Slots.OrderBy(s => s.Id).ToArray();
        var candidates = request.Candidates.OrderBy(c => c.Id).ToArray();
        var hours = (request.Period.End - request.Period.Start).Ticks / (decimal)TimeSpan.TicksPerHour;
        var costs = new decimal?[slots.Length, candidates.Length + slots.Length];
        var rejections = new List<RejectedCandidate>();
        decimal maximumCost = 0;
        for (var row = 0; row < slots.Length; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var column = 0; column < candidates.Length; column++)
            {
                var reason = Reject(slots[row], candidates[column], request);
                if (reason is not null)
                {
                    rejections.Add(new(slots[row].Id, candidates[column].Id, reason));
                    continue;
                }
                var cost = candidates[column].HourRate.Amount * hours;
                costs[row, column] = cost;
                maximumCost = Math.Max(maximumCost, cost);
            }
        }

        // One missing slot must cost more than every possible real assignment combined.
        // This enforces maximum cardinality before monetary minimization, without a magic sentinel.
        var unfilledPenalty = maximumCost * slots.Length + 1;
        for (var row = 0; row < slots.Length; row++) costs[row, candidates.Length + row] = unfilledPenalty;
        var solution = HungarianAssignmentSolver.Solve(costs, cancellationToken);
        var assignments = new List<PlannedAssignment>();
        var unfilled = new List<UnfilledSlot>();
        for (var row = 0; row < slots.Length; row++)
        {
            var column = solution.Columns[row];
            if (column >= candidates.Length)
            {
                var hasEligibleCandidate = Enumerable.Range(0, candidates.Length).Any(c => costs[row, c] is not null);
                unfilled.Add(new(slots[row].Id, slots[row].Name,
                    hasEligibleCandidate ? "Eligible resources are needed by other slots" : "No candidate satisfies the constraints"));
            }
            else
            {
                assignments.Add(new(slots[row].Id, slots[row].Name, candidates[column].Id, candidates[column].Name,
                    new Money(costs[row, column]!.Value, request.Currency)));
            }
        }
        return new(request.ProjectId, "hungarian/lexicographic-v1", unfilled.Count == 0,
            new Money(assignments.Sum(a => a.Cost.Amount), request.Currency), assignments, unfilled, rejections,
            solution.TotalCost, solution.RowPotentials.Sum() + solution.ColumnPotentials.Sum());
    }

    private static string? Reject(DemandSlot slot, ResourceCandidate candidate, PlanningRequest request)
    {
        if (candidate.Kind != slot.Kind) return "Resource kind does not match";
        if (candidate.HourRate.Currency != request.Currency) return "Currency does not match the planning currency";
        if (candidate.BusyPeriods.Any(p => p.Overlaps(request.Period))) return "Resource is already booked in this period";
        if (slot.EquipmentType is not null && !string.Equals(slot.EquipmentType, candidate.EquipmentType, StringComparison.OrdinalIgnoreCase))
            return $"Requires equipment type {slot.EquipmentType}";
        foreach (var (name, level) in slot.RequiredSkills)
        {
            var actual = candidate.Skills.Where(s => string.Equals(s.Key, name, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Value).DefaultIfEmpty(-1).Max();
            if (actual < level) return $"Requires skill {name} at level {level}";
        }
        return null;
    }

    private static void Validate(PlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProjectId == Guid.Empty) throw new DomainException("ProjectId is required");
        if (request.Period is null || request.Period.End - request.Period.Start > TimeSpan.FromDays(366))
            throw new DomainException("Planning period must be positive and no longer than 366 days");
        if (request.Slots is null || request.Candidates is null || request.Slots.Count > 50 || request.Candidates.Count > 200)
            throw new DomainException("A plan supports at most 50 slots and 200 candidates");
        if (!FusionOps.Domain.Enumerations.Currency.All.Contains(request.Currency)) throw new DomainException("Unsupported currency");
        if (request.Slots.Any(s => s is null || s.Id == Guid.Empty || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 200 || !Enum.IsDefined(s.Kind)
                || s.EquipmentType?.Length > 64 || s.RequiredSkills is null || s.RequiredSkills.Count > 20
                || s.RequiredSkills.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 64 || p.Value is < 0 or > 5))
            || request.Slots.Select(s => s.Id).Distinct().Count() != request.Slots.Count)
            throw new DomainException("Slots require unique IDs, names, valid kinds and skill levels 0–5");
        if (request.Candidates.Any(c => c is null || c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 200 || !Enum.IsDefined(c.Kind)
                || c.EquipmentType?.Length > 64 || c.HourRate.Amount is < 0 or > 1_000_000
                || c.BusyPeriods is null || c.BusyPeriods.Count > 64 || c.BusyPeriods.Any(p => p is null)
                || c.Skills is null || c.Skills.Count > 20 || c.Skills.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 64 || p.Value is < 0 or > 5))
            || request.Candidates.Select(c => c.Id).Distinct().Count() != request.Candidates.Count)
            throw new DomainException("Candidates require unique IDs, names, valid kinds, rates 0–1000000 and skill levels 0–5");
    }
}
