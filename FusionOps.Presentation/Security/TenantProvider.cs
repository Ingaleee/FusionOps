using FusionOps.Application.Abstractions;

namespace FusionOps.Presentation.Security;

public sealed class TenantProvider : ITenantProvider
{
    private string? _tenant;
    public string TenantId => _tenant ?? throw new InvalidOperationException("Tenant not resolved");
    public bool IsSet => _tenant is not null;
    public void Set(string tenant) => _tenant = tenant;
}



