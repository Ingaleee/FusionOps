using FusionOps.Domain.Projects;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Scheduling;

public sealed record TemporalPlanningInput(Guid ProjectId, ProjectDefinition Definition,
    IReadOnlyList<ResourceCandidate> Candidates, IReadOnlyDictionary<string, int> Stock,
    IReadOnlyDictionary<string, int> LicenseCapacity, int GridMinutes = 15, long MaxSearchNodes = 50000,
    IReadOnlyDictionary<string, Money>? MaterialUnitCosts = null, int MaxElapsedMilliseconds = 5000);
public sealed record ScheduledResource(Guid SlotId, Guid ResourceId);
public sealed record ScheduledProjectTask(Guid TaskId, TimeRange Period, IReadOnlyList<ScheduledResource> Resources, decimal Cost);
public sealed record TemporalPlanningIssue(string Code, Guid? TaskId = null, string? Reference = null);
public sealed record TemporalPlanningResult(Guid ProjectId, string Algorithm, bool IsComplete, bool SearchComplete,
    long ExploredNodes, decimal TotalCost, DateTimeOffset? FinishAt, IReadOnlyList<ScheduledProjectTask> Tasks,
    IReadOnlyList<TemporalPlanningIssue> Issues, string Termination, long ElapsedMilliseconds, decimal CostLowerBound);
public sealed record TemporalValidation(bool IsFeasible, bool IsComplete, decimal ComputedCost,
    IReadOnlyList<TemporalPlanningIssue> Violations);
public sealed class TemporalModelException(string code) : DomainException(code) { public string Code { get; } = code; }

public static class TemporalModel
{
    public static void Validate(TemporalPlanningInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ProjectId == Guid.Empty || input.GridMinutes is < 1 or > 60 || input.MaxSearchNodes is < 1 or > 1_000_000
            || input.MaxElapsedMilliseconds is < 1 or > 30000)
            throw new TemporalModelException("temporal_model_limits");
        ProjectDefinitionCodec.Canonicalize(input.Definition);
        if (input.Candidates is null || input.Candidates.Count > 200 || input.Candidates.Any(c => c is null || c.Id == Guid.Empty)
            || input.Candidates.Select(c => c.Id).Distinct().Count() != input.Candidates.Count)
            throw new TemporalModelException("temporal_candidate_identity");
        foreach (var c in input.Candidates)
        {
            if (!ProjectDefinitionCodec.Text(c.Name, 200) || !Enum.IsDefined(c.Kind) || c.HourRate.Amount is < 0 or > 1_000_000
                || decimal.Round(c.HourRate.Amount, 6) != c.HourRate.Amount || !FusionOps.Domain.Enumerations.Currency.All.Contains(c.HourRate.Currency)
                || c.Skills is null || c.Skills.Count > 20 || c.Skills.Any(s => !ProjectDefinitionCodec.Text(s.Key, 64) || s.Value is < 0 or > 5)
                || c.Skills.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != c.Skills.Count
                || c.BusyPeriods is null || c.BusyPeriods.Count > 5000 || c.BusyPeriods.Any(p => p is null)
                || c.Kind == ResourceKind.Human && c.EquipmentType is not null
                || c.Kind == ResourceKind.Equipment && !ProjectDefinitionCodec.Text(c.EquipmentType, 64))
                throw new TemporalModelException("temporal_candidate_invalid");
        }
        if (input.Candidates.Sum(c => (long)c.BusyPeriods.Count) > 20000) throw new TemporalModelException("temporal_calendar_limits");
        foreach (var capacities in new[] { input.Stock, input.LicenseCapacity })
            if (capacities is null || capacities.Count > 4000 || capacities.Any(p => !ProjectDefinitionCodec.Text(p.Key, 128) || p.Value is < 0 or > 1_000_000))
                throw new TemporalModelException("temporal_capacity_invalid");
        if (input.MaterialUnitCosts is { } prices && (prices.Count > 4000 || prices.Any(p => !ProjectDefinitionCodec.Text(p.Key, 128)
            || p.Value.Amount is < 0 or > 1_000_000 || decimal.Round(p.Value.Amount, 6) != p.Value.Amount
            || p.Value.Currency.Name != input.Definition.Currency))) throw new TemporalModelException("temporal_material_price_invalid");
    }
}
