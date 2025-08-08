using System.Collections.Generic;

namespace FusionOps.Application.Dto;

public sealed class PagedResult<T>
{
    public int Total { get; }
    public IReadOnlyList<T> Items { get; }

    public PagedResult(int total, IReadOnlyList<T> items)
    {
        Total = total;
        Items = items;
    }
}
