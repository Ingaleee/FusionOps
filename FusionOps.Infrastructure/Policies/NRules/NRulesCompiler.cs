using FusionOps.Infrastructure.Policies;

namespace FusionOps.Infrastructure.Policies.NRules;

public sealed class NRulesCompiler
{
    public Task<object> GetSessionFactoryAsync(string tenantId, string policyName, CancellationToken ct)
    {
        // Placeholder for Roslyn-based compilation and session factory caching
        return Task.FromResult<object>(new object());
    }
}



