using System;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;

namespace FusionOps.Domain.Events;

/// <summary>
/// Raised after stock quantity has been increased by a restock operation.
/// </summary>
public readonly record struct StockReplenished(WarehouseId WarehouseId,
                                               string Sku,
                                               int QuantityAdded) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
} 