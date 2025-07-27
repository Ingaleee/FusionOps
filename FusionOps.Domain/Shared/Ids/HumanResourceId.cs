using System;

namespace FusionOps.Domain.Shared.Ids;

/// <summary>
/// Strongly typed identifier for a HumanResource entity.
/// </summary>
public readonly record struct HumanResourceId(Guid Value)
{
    public static HumanResourceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(HumanResourceId id) => id.Value;

    public static implicit operator HumanResourceId(Guid value) => new(value);
}