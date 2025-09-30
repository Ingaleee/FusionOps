using System.Diagnostics.Metrics;
using FusionOps.Application.Policies;
using FusionOps.Infrastructure.Policies;

namespace FusionOps.Infrastructure.Policies.Opa;

public sealed class OpaPolicyEngine : IPolicyEngine
{
    private static readonly Meter Meter = new("FusionOps.Policy");
    private static readonly Histogram<double> EvalMs = Meter.CreateHistogram<double>("policy_eval_duration_ms", unit: "ms");

    private readonly IPolicyAudit _audit;
    private readonly OpaWasmRuntime _runtime;

    public OpaPolicyEngine(IPolicyAudit audit, OpaWasmRuntime runtime)
    {
        _audit = audit;
        _runtime = runtime;
    }

    public async Task<PolicyDecision> EvaluateAsync(string policySetName, PolicyInput input, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // TODO: Load WASM module for (tenant, policySetName) and evaluate
        _ = await _runtime.GetModuleAsync(input.TenantId, policySetName, ct);
        var decision = new PolicyDecision(true, Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, object>());
        sw.Stop();
        EvalMs.Record(sw.Elapsed.TotalMilliseconds, new TagList { { "engine", "rego" }, { "policy", policySetName } });
        await _audit.RecordAsync(policySetName, "rego", 1, decision, input, null, ct);
        return decision;
    }

    public Task WarmUpAsync(string tenantId, CancellationToken ct) => Task.CompletedTask;
}


