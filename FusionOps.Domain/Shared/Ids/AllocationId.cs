using System;

namespace FusionOps.Domain.Shared.Ids;

/// <summary>
/// Strongly typed identifier for an Allocation aggregate.
/// </summary>
public readonly record struct AllocationId(Guid Value)
{
    public static AllocationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(AllocationId id) => id.Value;

    public static implicit operator AllocationId(Guid value) => new(value);
} 