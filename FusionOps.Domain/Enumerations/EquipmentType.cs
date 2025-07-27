using System.Collections.Generic;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Enumerations;

/// <summary>
/// Classification of equipment resources available for allocation.
/// </summary>
public readonly record struct EquipmentType(int Value, string Name) : IEnumeration
{
    public static readonly EquipmentType CNC = new(1, nameof(CNC));
    public static readonly EquipmentType Printer = new(2, nameof(Printer));
    public static readonly EquipmentType GPU = new(3, nameof(GPU));

    public static IReadOnlyCollection<EquipmentType> All => new[] { CNC, Printer, GPU };

    public override string ToString() => Name;
}