using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Enumerations;

public readonly record struct CostBucket(int Value, string Name) : IEnumeration
{
    public static readonly CostBucket Labor = new(1, nameof(Labor));
    public static readonly CostBucket Equipment = new(2, nameof(Equipment));
    public static readonly CostBucket InventoryHolding = new(3, nameof(InventoryHolding));
    public static readonly CostBucket BackorderPenalty = new(4, nameof(BackorderPenalty));
    public static readonly CostBucket LicensePenalty = new(5, nameof(LicensePenalty));
    public static readonly CostBucket Overtime = new(6, nameof(Overtime));
    public static readonly CostBucket Capex = new(7, nameof(Capex));
}


