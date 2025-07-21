using System;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;

namespace FusionOps.Domain.Events;

/// <summary>
/// Raised when stock quantity drops to or below its reorder point threshold.
/// </summary>
public readonly record struct ReorderPointReached(WarehouseId WarehouseId,
                                                  string Sku,
                                                  int QuantityLeft) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
} 