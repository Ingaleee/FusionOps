using System;

namespace FusionOps.Domain.Shared.Ids;

/// <summary>
/// Strongly typed identifier for a Warehouse aggregate.
/// </summary>
public readonly record struct WarehouseId(Guid Value)
{
    public static WarehouseId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(WarehouseId id) => id.Value;

    public static implicit operator WarehouseId(Guid value) => new(value);
} 