using System;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.Events.Attributes;

namespace FusionOps.Domain.Events;

/// <summary>
/// Raised after stock quantity has been increased by a restock operation.
/// </summary>
[EventType("v1.StockReplenished")]
public sealed record StockReplenished(WarehouseId WarehouseId,
                                               string Sku,
                                               int QuantityAdded) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}