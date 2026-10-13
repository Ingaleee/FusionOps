using FusionOps.Domain.Tenancy;

namespace FusionOps.Application.Abstractions;

public sealed record TenantScope(Guid Id, bool IsSystem = false)
{
    public static TenantScope Legacy { get; } = new(TenantIdentity.Legacy);
    public static TenantScope System { get; } = new(Guid.Empty, true);
    public static TenantScope For(Guid id) => id != Guid.Empty ? new(id) : throw new TenantRequiredException();
}

/// <summary>Execution-local scope; a DbContext captures it once and never follows subsequent ambient changes.</summary>
public static class TenantExecution
{
    private static readonly AsyncLocal<TenantScope?> Slot = new();
    public static TenantScope? Current => Slot.Value;
    public static Guid RequiredTenant => Current is { IsSystem: false, Id: var id } && id != Guid.Empty ? id : throw new TenantRequiredException();
    public static IDisposable Enter(TenantScope scope)
    {
        var previous = Slot.Value;
        Slot.Value = scope;
        return new Restore(previous);
    }
    private sealed class Restore(TenantScope? previous) : IDisposable
    {
        public void Dispose() => Slot.Value = previous;
    }
}
