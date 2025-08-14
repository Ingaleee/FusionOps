using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Entities;
using FusionOps.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace FusionOps.Infrastructure.Costing;

public sealed class DefaultCostEngine : ICostEngine
{
    private readonly IOptions<CostingOptions> _options;
    public DefaultCostEngine(IOptions<CostingOptions> options) => _options = options;

    public CostBreakdown ForAllocation(HumanResource resource, TimeRange period)
    {
        var hours = (decimal)(period.End - period.Start).TotalHours;
        var amount = resource.HourRate * hours;
        return new CostBreakdown(new[] { new CostComponent("Labor", amount) });
    }

    public CostBreakdown ForEquipment(EquipmentResource equipment, TimeRange period)
    {
        var hours = (decimal)(period.End - period.Start).TotalHours;
        var amount = _options.Value.EquipmentHourlyRate * hours;
        return new CostBreakdown(new[] { new CostComponent("Equipment", amount) });
    }

    public CostBreakdown ForInventoryHolding(StockItem item, int days)
    {
        var rate = _options.Value.InventoryHoldingRatePerDay; // decimal multiplier
        var unitCost = item.UnitCost; // Money
        var amount = unitCost * (rate * days * item.Quantity);
        return new CostBreakdown(new[] { new CostComponent("InventoryHolding", amount) });
    }

    public CostBreakdown ForBackorder(string sku, int quantity, int daysLate)
    {
        var penalty = _options.Value.BackorderPenaltyPerUnitPerDay; // Money per unit per day
        var amount = penalty * (quantity * daysLate);
        return new CostBreakdown(new[] { new CostComponent("BackorderPenalty", amount) });
    }

    public CostBreakdown ForLicensePenalty(string product, int shortageSeats, int days)
    {
        var penalty = _options.Value.LicensePenaltyPerSeatPerDay; // Money per seat per day
        var amount = penalty * (shortageSeats * days);
        return new CostBreakdown(new[] { new CostComponent("LicensePenalty", amount) });
    }

    public CostBreakdown Sum(params CostBreakdown[] parts) => CostBreakdown.Sum(parts);
}

public sealed class CostingOptions
{
    public Money EquipmentHourlyRate { get; init; } = Money.Usd(10);
    public decimal InventoryHoldingRatePerDay { get; init; } = 0.001m; // 0.1%/day
    public Money BackorderPenaltyPerUnitPerDay { get; init; } = Money.Usd(2);
    public Money LicensePenaltyPerSeatPerDay { get; init; } = Money.Usd(5);
}


