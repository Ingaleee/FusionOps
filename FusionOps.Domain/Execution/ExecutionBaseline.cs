using System.Security.Cryptography;
using System.Text.Json;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using FusionOps.Domain.Tenancy;

namespace FusionOps.Domain.Execution;

public sealed record ExecutionApprovalPolicy(string Version, bool RequireSeparateApprover, int ReviewLifetimeSeconds, int ApprovalLifetimeSeconds)
{
    public static ExecutionApprovalPolicy Standard(bool separate = true) => new("execution-approval/v1", separate, 86400, 86400);
    public void Validate()
    {
        if (Version != "execution-approval/v1" || ReviewLifetimeSeconds is < 30 or > 604800 || ApprovalLifetimeSeconds is < 30 or > 604800)
            throw new ExecutionPlanRuleException("execution_policy_invalid");
    }
}
public sealed record CapturedScenarioProof(Guid Id, string Actor, DateTimeOffset CreatedAt, string RequestHash, string Digest, string EvidenceJson);
public sealed record ExecutionTaskChange(Guid TaskId, ScheduledProjectTask? Before, ScheduledProjectTask? After, bool DefinitionChanged);
public sealed record ExecutionPlanDiff(Guid? PreviousPlanId, string? PreviousDigest, long? PreviousDefinitionVersion,
    bool CurrencyChanged, decimal? CostDelta, double? FinishDeltaMinutes, IReadOnlyList<ExecutionTaskChange> Tasks);
public sealed record ExecutionBaselineEvidence(CapturedScenarioProof Scenario, ExecutionApprovalPolicy Policy, ExecutionPlanDiff Diff);

/// <summary>Append-only reviewed evidence. Approval and eventual ownership are separate mutable protocols.</summary>
public sealed class ExecutionBaseline : TenantOwnedEntity
{
    private ExecutionBaseline() { }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ScenarioId { get; private set; }
    public Guid? PreviousPlanId { get; private set; }
    public long ProjectVersion { get; private set; }
    public long DefinitionVersion { get; private set; }
    public string DefinitionDigest { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ReviewExpiresAt { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;
    public string EvidenceJson { get; private set; } = string.Empty;
    public decimal TotalCost { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public ExecutionBaselineEvidence Evidence => JsonSerializer.Deserialize<ExecutionBaselineEvidence>(EvidenceJson, TemporalInputCodec.Json)
        ?? throw new ExecutionPlanRuleException("execution_evidence_missing");
    public TemporalScenarioEvidence ScenarioEvidence => JsonSerializer.Deserialize<TemporalScenarioEvidence>(Evidence.Scenario.EvidenceJson, TemporalInputCodec.Json)
        ?? throw new ExecutionPlanRuleException("execution_evidence_missing");

    public static ExecutionBaseline Capture(Guid id, TemporalScenario scenario, string actor, string requestHash,
        ExecutionApprovalPolicy policy, ExecutionBaseline? previous, DateTimeOffset now)
    {
        if (id == Guid.Empty || !ProjectDefinitionCodec.Text(actor, 200) || requestHash.Length != 64)
            throw new ExecutionPlanRuleException("execution_identity_invalid");
        policy.Validate(); scenario.VerifyEvidence();
        if (!scenario.IsComplete) throw new ExecutionPlanRuleException("execution_complete_scenario_required");
        if (previous is not null)
        {
            previous.VerifyEvidence();
            if (previous.Id == id || previous.TenantId != scenario.TenantId || previous.ProjectId != scenario.ProjectId)
                throw new ExecutionPlanRuleException("execution_previous_plan_invalid");
        }
        var baseline = new ExecutionBaseline { Id = id, ProjectId = scenario.ProjectId, ScenarioId = scenario.Id,
            PreviousPlanId = previous?.Id, ProjectVersion = scenario.ProjectVersion, DefinitionVersion = scenario.DefinitionVersion,
            DefinitionDigest = scenario.Evidence.Source.DefinitionDigest, CreatedBy = actor, CreatedAt = Normalize(now), RequestHash = requestHash,
            TotalCost = scenario.TotalCost, Currency = scenario.Evidence.Input.Definition.Currency };
        baseline.BindTenant(scenario.TenantId);
        baseline.ReviewExpiresAt = baseline.CreatedAt.AddSeconds(policy.ReviewLifetimeSeconds);
        baseline.EvidenceJson = JsonSerializer.Serialize(new ExecutionBaselineEvidence(
            new(scenario.Id, scenario.Actor, scenario.CreatedAt, scenario.RequestHash, scenario.Digest, scenario.EvidenceJson),
            policy, Compare(previous, scenario.Evidence)), TemporalInputCodec.Json);
        if (baseline.EvidenceJson.Length > 16_000_000) throw new ExecutionPlanRuleException("execution_evidence_limits");
        baseline.Digest = baseline.CalculateDigest(); baseline.VerifyEvidence(); return baseline;
    }
    public void VerifyEvidence()
    {
        if (Digest != CalculateDigest()) throw new ExecutionPlanRuleException("execution_evidence_invalid");
        var proof = Evidence; proof.Policy.Validate(); var e = ScenarioEvidence;
        var original = TemporalScenario.Create(proof.Scenario.Id, TenantId, proof.Scenario.Actor, proof.Scenario.RequestHash, e, proof.Scenario.CreatedAt);
        if (original.Digest != proof.Scenario.Digest || original.Id != ScenarioId || original.ProjectId != ProjectId
            || original.ProjectVersion != ProjectVersion || original.DefinitionVersion != DefinitionVersion
            || e.Source.DefinitionDigest != DefinitionDigest || !original.IsComplete
            || ReviewExpiresAt != CreatedAt.AddSeconds(proof.Policy.ReviewLifetimeSeconds)
            || proof.Diff.PreviousPlanId != PreviousPlanId || TotalCost != original.TotalCost || Currency != e.Input.Definition.Currency)
            throw new ExecutionPlanRuleException("execution_evidence_invalid");
    }
    private static ExecutionPlanDiff Compare(ExecutionBaseline? previous, TemporalScenarioEvidence current)
    {
        var old = previous?.ScenarioEvidence;
        var before = old?.Result.Tasks.ToDictionary(t => t.TaskId) ?? [];
        var after = current.Result.Tasks.ToDictionary(t => t.TaskId);
        var oldDefinitions = old?.Input.Definition.Tasks.ToDictionary(t => t.Id) ?? [];
        var newDefinitions = current.Input.Definition.Tasks.ToDictionary(t => t.Id);
        var changes = new List<ExecutionTaskChange>();
        foreach (var id in before.Keys.Union(after.Keys).Order())
        {
            before.TryGetValue(id, out var a); after.TryGetValue(id, out var b);
            oldDefinitions.TryGetValue(id, out var da); newDefinitions.TryGetValue(id, out var db);
            var definitionChanged = JsonSerializer.Serialize(da, TemporalInputCodec.Json) != JsonSerializer.Serialize(db, TemporalInputCodec.Json);
            if (definitionChanged || a is null || b is null || a.Period != b.Period || a.Cost != b.Cost || !a.Resources.SequenceEqual(b.Resources))
                changes.Add(new(id, a, b, definitionChanged));
        }
        var currencyChanged = old is not null && old.Input.Definition.Currency != current.Input.Definition.Currency;
        return new(previous?.Id, previous?.Digest, previous?.DefinitionVersion, currencyChanged,
            old is null || currencyChanged ? null : current.Result.TotalCost - old.Result.TotalCost,
            old?.Result.FinishAt is { } aFinish && current.Result.FinishAt is { } bFinish ? (bFinish - aFinish).TotalMinutes : null, changes);
    }
    private string CalculateDigest() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    { Schema = 1, TenantId, Id, ProjectId, ScenarioId, PreviousPlanId, ProjectVersion, DefinitionVersion, DefinitionDigest,
        CreatedBy, CreatedAt, ReviewExpiresAt, RequestHash, TotalCost, Currency, EvidenceJson }, TemporalInputCodec.Json)));
    private static DateTimeOffset Normalize(DateTimeOffset value) => new(value.UtcTicks / 10 * 10, TimeSpan.Zero);
}

public sealed class ExecutionPlanRuleException(string code) : FusionOps.Domain.Shared.DomainException(code)
{
    public string Code { get; } = code;
}
public sealed class ExecutionPlanVersionException() : FusionOps.Domain.Shared.DomainException("The execution plan version changed; reload its ETag");
public sealed class ExecutionPlanSubmissionConflictException() : FusionOps.Domain.Shared.DomainException("The plan ID has another original submission");
