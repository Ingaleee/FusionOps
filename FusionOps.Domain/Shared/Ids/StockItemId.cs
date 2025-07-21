using System;

namespace FusionOps.Domain.Shared.Ids;

/// <summary>
/// Strongly typed identifier for a StockItem entity.
/// </summary>
public readonly record struct StockItemId(Guid Value)
{
    public static StockItemId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(StockItemId id) => id.Value;

    public static implicit operator StockItemId(Guid value) => new(value);
} 