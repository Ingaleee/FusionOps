using FusionOps.Domain.Entities;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Interfaces;

public interface ICostEngine
{
    CostBreakdown ForAllocation(HumanResource resource, TimeRange period);
    CostBreakdown ForEquipment(EquipmentResource equipment, TimeRange period);
    CostBreakdown ForInventoryHolding(StockItem item, int days);
    CostBreakdown ForBackorder(string sku, int quantity, int daysLate);
    CostBreakdown ForLicensePenalty(string product, int shortageSeats, int days);
    CostBreakdown Sum(params CostBreakdown[] parts);
}


