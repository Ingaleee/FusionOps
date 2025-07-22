using FusionOps.Domain.Entities;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using NUnit.Framework;

namespace FusionOps.Domain.Tests;

public class StockItemTests
{
    [Test]
    public void Deduct_MoreThanAvailable_ShouldThrow()
    {
        // Arrange
        var item = new StockItem(StockItemId.New(), "SKU1", 5, 2, Money.Usd(10));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => item.Deduct(10));
    }

    [Test]
    public void Deduct_NegativeQty_ShouldThrow()
    {
        var item = new StockItem(StockItemId.New(), "SKU1", 5, 2, Money.Usd(10));
        Assert.Throws<ArgumentException>(() => item.Deduct(-1));
    }
} 