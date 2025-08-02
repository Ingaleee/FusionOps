using HotChocolate;
using HotChocolate.Types;
using FusionOps.Gateway.Models;

namespace FusionOps.Gateway.GraphQL;

[ExtendObjectType(typeof(Allocation))]
public class AllocationResolvers
{
    // Пример: получить Sku через другой DataLoader
    // public string? Sku([Parent] Allocation alloc, ResourceByIdDataLoader loader, CancellationToken ct)
    //     => loader.LoadAsync(alloc.ResourceId, ct).Result?.Sku;
}
