namespace FusionOps.Application.Abstractions;

public interface ITenantProvider
{
    string TenantId { get; }
    bool IsSet { get; }
}


