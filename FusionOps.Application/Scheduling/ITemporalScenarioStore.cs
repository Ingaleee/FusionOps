using FusionOps.Domain.Scheduling;

namespace FusionOps.Application.Scheduling;

public sealed record TemporalScenarioRequest(Guid Id, long ExpectedProjectVersion, IReadOnlyList<Guid> CandidateIds,
    int GridMinutes = 15, long MaxSearchNodes = 50000, int MaxElapsedMilliseconds = 5000, ScenarioAssumptions? Assumptions = null);
public sealed record TemporalScenarioSummary(Guid Id, long ProjectVersion, long DefinitionVersion, string Digest, string Actor,
    DateTimeOffset CreatedAt, bool IsComplete, bool SearchComplete, bool IsHypothetical, decimal TotalCost, DateTimeOffset? FinishAt);
public sealed record ScenarioReplay(TemporalPlanningResult Result, TemporalValidation Validation, bool MatchesOriginalDecision);
public sealed record ScenarioComparison(Guid Left, Guid Right, long LeftDefinitionVersion, long RightDefinitionVersion,
    bool LeftHypothetical, bool RightHypothetical, decimal CostDelta, double? FinishDeltaMinutes, IReadOnlyList<Guid> ChangedTasks);
public interface ITemporalScenarioStore
{
    Task<TemporalScenario> CreateAsync(Guid projectId, TemporalScenarioRequest request, CancellationToken ct);
    Task<TemporalScenario?> GetAsync(Guid projectId, Guid scenarioId, CancellationToken ct);
    Task<IReadOnlyList<TemporalScenarioSummary>> ListAsync(Guid projectId, Guid? after, int limit, CancellationToken ct);
    Task<ScenarioReplay?> ReplayAsync(Guid projectId, Guid scenarioId, CancellationToken ct);
    Task<ScenarioComparison?> CompareAsync(Guid projectId, Guid left, Guid right, CancellationToken ct);
}
