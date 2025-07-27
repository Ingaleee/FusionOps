using System.Collections.Generic;
using FusionOps.Domain.Events;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;

namespace FusionOps.Domain.Entities;

/// <summary>
/// Aggregate root encapsulating stock operations and events for a physical warehouse.
/// </summary>
public class Warehouse : IEntity<WarehouseId>, IHasDomainEvents
{
    private readonly Dictionary<string, StockItem> _items = new();
    private readonly List<IDomainEvent> _domainEvents = new();

    public Warehouse(WarehouseId id)
    {
        Id = id;
    }

    public WarehouseId Id { get; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public void PutAway(string sku, int qty, ValueObjects.Money unitCost, int reorderPoint)
    {
        if (!_items.TryGetValue(sku, out var item))
        {
            item = new StockItem(StockItemId.New(), sku, 0, reorderPoint, unitCost);
            _items[sku] = item;
        }
        item.Restock(qty);
        AddDomainEvent(new StockReplenished(Id, sku, qty));
    }

    public void Reserve(string sku, int qty)
    {
        if (!_items.TryGetValue(sku, out var item))
            throw new System.InvalidOperationException("SKU not found");
        item.Deduct(qty);
        if (item.NeedsReorder())
        {
            AddDomainEvent(new ReorderPointReached(Id, sku, item.Quantity));
        }
    }
}