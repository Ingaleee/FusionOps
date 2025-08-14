using System;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Entities;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Enumerations;
using FusionOps.Infrastructure.Costing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

[TestFixture]
public class CostEngineTests
{
    private ICostEngine CreateEngine()
    {
        var opts = Options.Create(new CostingOptions
        {
            EquipmentHourlyRate = Money.Usd(10),
            InventoryHoldingRatePerDay = 0.002m,
            BackorderPenaltyPerUnitPerDay = Money.Usd(3),
            LicensePenaltyPerSeatPerDay = Money.Usd(7)
        });
        return new DefaultCostEngine(opts);
    }

    [Test]
    public void LaborCost_Computes_ByHours()
    {
        var engine = CreateEngine();
        var hr = new HumanResource(new FusionOps.Domain.Shared.Ids.HumanResourceId(Guid.NewGuid()), "John", Money.Usd(20));
        var period = new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(3));
        var c = engine.ForAllocation(hr, period);
        Assert.That(c.Total.Amount, Is.EqualTo(60).Within(0.01m));
        Assert.That(c.Total.Currency, Is.EqualTo(Currency.USD));
    }

    [Test]
    public void BackorderPenalty_Computes()
    {
        var engine = CreateEngine();
        var c = engine.ForBackorder("SKU", 10, 2);
        Assert.That(c.Total.Amount, Is.EqualTo(60));
    }

    [Test]
    public void EquipmentCost_Computes_ByHours()
    {
        var engine = CreateEngine();
        var eq = new EquipmentResource(new FusionOps.Domain.Shared.Ids.EquipmentResourceId(Guid.NewGuid()), "Printer-9000", EquipmentType.Printer, Money.Usd(100));
        var period = new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(4));
        var c = engine.ForEquipment(eq, period);
        Assert.That(c.Total.Amount, Is.EqualTo(40).Within(0.01m)); // EquipmentHourlyRate from options (10) * 4
        Assert.That(c.Total.Currency, Is.EqualTo(Currency.USD));
    }

    [Test]
    public void InventoryHolding_Computes_ByRate_Days_Qty()
    {
        var engine = CreateEngine();
        var item = new StockItem(new FusionOps.Domain.Shared.Ids.StockItemId(Guid.NewGuid()), "SKU-1", quantity: 50, reorderPoint: 10, unitCost: Money.Usd(2));
        var c = engine.ForInventoryHolding(item, days: 5);
        // rate 0.002 per day, qty 50, days 5, unit 2 USD => 2 * (0.002 * 5 * 50) = 1.0 USD
        Assert.That(c.Total.Amount, Is.EqualTo(1.0m));
        Assert.That(c.Total.Currency, Is.EqualTo(Currency.USD));
    }

    [Test]
    public void LicensePenalty_Computes()
    {
        var engine = CreateEngine();
        var c = engine.ForLicensePenalty("Prod", shortageSeats: 3, days: 4);
        // 7 USD per seat per day from options => 3 * 4 * 7 = 84
        Assert.That(c.Total.Amount, Is.EqualTo(84m));
        Assert.That(c.Total.Currency, Is.EqualTo(Currency.USD));
    }

    [Test]
    public void Sum_Aggregates_ByName_And_Validates_Currency()
    {
        var a = new CostBreakdown(new[]
        {
            new CostComponent("Labor", Money.Usd(10)),
            new CostComponent("Equipment", Money.Usd(5))
        });
        var b = new CostBreakdown(new[]
        {
            new CostComponent("Labor", Money.Usd(4))
        });
        var sum = CostBreakdown.Sum(a, b);
        Assert.That(sum.Components.Count, Is.EqualTo(2));
        Assert.That(sum.Find("Labor")!.Amount.Amount, Is.EqualTo(14m));
        Assert.That(sum.Find("Equipment")!.Amount.Amount, Is.EqualTo(5m));

        Assert.Throws<InvalidOperationException>(() => CostBreakdown.Sum(a, new CostBreakdown(new[] { new CostComponent("Labor", Money.Eur(1)) })));
    }
}


