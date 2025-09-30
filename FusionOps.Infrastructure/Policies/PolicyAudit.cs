using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics.Metrics;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Policies;

namespace FusionOps.Infrastructure.Policies;

public interface IPolicyAudit
{
    Task RecordAsync(string policyName, string engine, int version, PolicyDecision decision, PolicyInput input, string? correlationId, CancellationToken ct);
}

public sealed class PolicyAudit : IPolicyAudit
{
    private static readonly Meter Meter = new("FusionOps.Policy");
    private static readonly Counter<long> EvalCounter = Meter.CreateCounter<long>("policy_evaluations_total", unit: "calls");
    private static readonly Histogram<double> EvalMs = Meter.CreateHistogram<double>("policy_eval_duration_ms", unit: "ms");

    private readonly PolicyContext _ctx;
    private readonly ITenantProvider _tenantProvider;

    public PolicyAudit(PolicyContext ctx, ITenantProvider tenantProvider)
    {
        _ctx = ctx;
        _tenantProvider = tenantProvider;
    }

    public async Task RecordAsync(string policyName, string engine, int version, PolicyDecision decision, PolicyInput input, string? correlationId, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(decision);
        var record = new PolicyDecisionRecord
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantProvider.IsSet ? _tenantProvider.TenantId : input.TenantId,
            PolicyName = policyName,
            Version = version,
            Engine = engine,
            DecisionJson = payload,
            InputFingerprint = HashInput(input),
            CorrelationId = correlationId,
            OccurredAt = DateTimeOffset.UtcNow,
            RuleMatchesJson = null
        };
        _ctx.PolicyDecisions.Add(record);
        await _ctx.SaveChangesAsync(ct);

        var tags = new TagList { { "policy", policyName }, { "engine", engine }, { "result", decision.IsAllowed ? "allow" : "deny" } };
        EvalCounter.Add(1, tags);
    }

    private static string HashInput(PolicyInput input)
    {
        var json = JsonSerializer.Serialize(input);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}



