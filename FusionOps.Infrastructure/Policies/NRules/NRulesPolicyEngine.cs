using System.Diagnostics.Metrics;
using FusionOps.Application.Policies;
using FusionOps.Infrastructure.Policies;

namespace FusionOps.Infrastructure.Policies.NRules;

public sealed class NRulesPolicyEngine : IPolicyEngine
{
    private static readonly Meter Meter = new("FusionOps.Policy");
    private static readonly Histogram<double> EvalMs = Meter.CreateHistogram<double>("policy_eval_duration_ms", unit: "ms");

    private readonly IPolicyAudit _audit;
    private readonly NRulesCompiler _compiler;

    public NRulesPolicyEngine(IPolicyAudit audit, NRulesCompiler compiler)
    {
        _audit = audit;
        _compiler = compiler;
    }

    public async Task<PolicyDecision> EvaluateAsync(string policySetName, PolicyInput input, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // TODO: Compile and execute NRules RuleSet for (tenant, policySetName)
        _ = await _compiler.GetSessionFactoryAsync(input.TenantId, policySetName, ct);
        var decision = new PolicyDecision(true, Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, object>());
        sw.Stop();
        EvalMs.Record(sw.Elapsed.TotalMilliseconds, new TagList { { "engine", "nrules" }, { "policy", policySetName } });
        await _audit.RecordAsync(policySetName, "nrules", 1, decision, input, null, ct);
        return decision;
    }

    public Task WarmUpAsync(string tenantId, CancellationToken ct) => Task.CompletedTask;
}


