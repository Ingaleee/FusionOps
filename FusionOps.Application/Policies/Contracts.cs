using FusionOps.Domain.ValueObjects;

namespace FusionOps.Application.Policies;

public sealed record PolicyInput(
    string TenantId,
    string UserId,
    Guid? ProjectId,
    DateTime NowUtc,
    decimal EstimatedCost,
    bool RequestOvertime,
    TimeRange Period,
    IReadOnlyCollection<string> InvolvedRoles,
    IReadOnlyCollection<string> SkuList);

public sealed record PolicyDecision(
    bool IsAllowed,
    string[] RequiredApprovals,
    string[] Violations,
    IReadOnlyDictionary<string, object> Hints);

public interface IPolicyEngine
{
    Task<PolicyDecision> EvaluateAsync(string policySetName, PolicyInput input, CancellationToken ct);
    Task WarmUpAsync(string tenantId, CancellationToken ct);
}



