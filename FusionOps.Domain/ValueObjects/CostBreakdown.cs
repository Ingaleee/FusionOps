using System.Collections.Generic;
using System.Linq;

namespace FusionOps.Domain.ValueObjects;

public sealed class CostBreakdown
{
    private readonly IReadOnlyList<CostComponent> _components;
    public IReadOnlyList<CostComponent> Components => _components;
    public Money Total { get; }

    public CostBreakdown(IEnumerable<CostComponent> components)
    {
        var comps = components?.ToList() ?? new List<CostComponent>();
        _components = comps.AsReadOnly();
        if (_components.Count == 0)
        {
            Total = Money.Usd(0);
        }
        else
        {
            var zero = Money.Of(0, _components[0].Amount.Currency);
            Total = _components.Aggregate(zero, (acc, c) => acc + c.Amount);
        }
    }

    public static CostBreakdown Empty(Money zero) => new(new[] { new CostComponent("Total", zero) }.Take(0));

    public CostComponent? Find(string name) => _components.FirstOrDefault(c => c.Name == name);

    public static CostBreakdown Sum(params CostBreakdown[] parts)
    {
        if (parts == null || parts.Length == 0) return new CostBreakdown(Enumerable.Empty<CostComponent>());
        var grouped = parts
            .SelectMany(p => p.Components)
            .GroupBy(c => c.Name)
            .Select(g =>
            {
                var amounts = g.Select(x => x.Amount).ToList();
                if (amounts.Count == 0)
                    return new CostComponent(g.Key, Money.Usd(0));
                var zero = Money.Of(0, amounts[0].Currency);
                var sum = amounts.Aggregate(zero, (acc, m) =>
                {
                    if (acc.Currency != m.Currency)
                        throw new System.InvalidOperationException("Mixed currencies in CostBreakdown.Sum");
                    return acc + m;
                });
                return new CostComponent(g.Key, sum);
            });
        return new CostBreakdown(grouped);
    }
}


