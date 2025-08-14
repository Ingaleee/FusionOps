using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using System.Collections.Generic;

namespace FusionOps.Domain.Entities;

/// <summary>
/// Represents a SKU stored in a warehouse with quantity tracking.
/// </summary>
[FusionOps.Domain.Attributes.PartitionedTable("range_month")]
public class StockItem : IEntity<StockItemId>
{
    private StockItem() { }

    public StockItem(StockItemId id, string sku, int quantity, int reorderPoint, Money unitCost)
    {
        Id = id;
        Sku = sku;
        Quantity = quantity;
        ReorderPoint = reorderPoint;
        UnitCost = unitCost;
    }

    public StockItemId Id { get; private set; }
    public string Sku { get; private set; }
    public int Quantity { get; private set; }
    public int ReorderPoint { get; private set; }
    public Money UnitCost { get; private set; }

    public void Deduct(int qty)
    {
        if (qty <= 0) throw new System.ArgumentException("Quantity must be positive", nameof(qty));
        if (qty > Quantity) throw new System.InvalidOperationException("Insufficient stock");
        Quantity -= qty;
    }

    public void Restock(int qty)
    {
        if (qty <= 0) throw new System.ArgumentException("Quantity must be positive", nameof(qty));
        Quantity += qty;
    }

    public bool NeedsReorder() => Quantity <= ReorderPoint;
}