using FusionOps.Application.Abstractions;
using FusionOps.Domain.Interfaces;
using System.Security.Cryptography;
using System.Text.Json;

namespace FusionOps.Application.UseCases.AllocateResource;

public sealed record AllocateCommand(Guid ProjectId, IReadOnlyCollection<Guid> ResourceIds, DateTimeOffset PeriodFrom, DateTimeOffset PeriodTo,
    string? IdempotencyKey = null) : ICommand<IReadOnlyCollection<Guid>>, IIdempotentCommand
{
    public PersistenceBoundary Boundary => PersistenceBoundary.Workforce;
    public string Operation => "workforce.allocate/v1";
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        ProjectId,
        ResourceIds = ResourceIds.Order().ToArray(),
        FromUtcTicks = PeriodFrom.UtcTicks,
        ToUtcTicks = PeriodTo.UtcTicks
    })));
}
