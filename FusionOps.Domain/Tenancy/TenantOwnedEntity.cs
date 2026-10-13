using FusionOps.Domain.Shared;

namespace FusionOps.Domain.Tenancy;

public static class TenantIdentity
{
    // Existing data is retained in one explicitly documented migration/development tenant.
    public static readonly Guid Legacy = Guid.Parse("11111111-1111-1111-1111-111111111111");
}

public abstract class TenantOwnedEntity
{
    public Guid TenantId { get; private set; }
    public void BindTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty || TenantId != Guid.Empty && TenantId != tenantId)
            throw new TenantBoundaryException();
        TenantId = tenantId;
    }
}

public sealed class TenantBoundaryException() : DomainException("The operation crosses a tenant boundary");
public sealed class TenantRequiredException() : DomainException("A verified tenant context is required");
