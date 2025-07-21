using System.Collections.Generic;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Enumerations;

/// <summary>
/// ISO currency codes supported by the system.
/// </summary>
public readonly record struct Currency(int Value, string Name) : IEnumeration
{
    public static readonly Currency USD = new(1, nameof(USD));
    public static readonly Currency EUR = new(2, nameof(EUR));
    public static readonly Currency RUB = new(3, nameof(RUB));

    public static IReadOnlyCollection<Currency> All => new[] { USD, EUR, RUB };

    public override string ToString() => Name;
} 