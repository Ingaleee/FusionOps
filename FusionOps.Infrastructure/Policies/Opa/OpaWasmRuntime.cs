namespace FusionOps.Infrastructure.Policies.Opa;

public sealed class OpaWasmRuntime
{
    public Task<object> GetModuleAsync(string tenantId, string policyName, CancellationToken ct)
    {
        // Placeholder for WASM module caching/loading
        return Task.FromResult<object>(new object());
    }
}



