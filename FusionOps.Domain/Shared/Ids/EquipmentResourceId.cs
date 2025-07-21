using System;

namespace FusionOps.Domain.Shared.Ids;

/// <summary>
/// Strongly typed identifier for an EquipmentResource entity.
/// </summary>
public readonly record struct EquipmentResourceId(Guid Value)
{
    public static EquipmentResourceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(EquipmentResourceId id) => id.Value;

    public static implicit operator EquipmentResourceId(Guid value) => new(value);
} 